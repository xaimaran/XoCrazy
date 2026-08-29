using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Formatting;

namespace XoCrazy.Core
{
    /// <summary>
    /// Pushes a colour into the maps the editor actually paints from.
    ///
    /// This is the half that <see cref="FontColorStore"/> cannot do. Writing through
    /// <c>IVsFontAndColorStorage.SetItem</c> updates where the value is *persisted*;
    /// <c>ClearCache</c>/<c>RefreshCache</c> rebuilds the *shell's* cache of it. Neither one
    /// touches the WPF editor, which paints from two MEF maps that were populated from Fonts
    /// and Colors once and cached per view:
    ///
    ///   <see cref="IClassificationFormatMap"/> — keyword, string, class name, every Roslyn
    ///   classification.
    ///   <see cref="IEditorFormatMap"/> — Selected Text, Line Number, brace matching, the
    ///   marker and adornment definitions, which have no classification type at all.
    ///
    /// The built-in Fonts and Colors page gets the editor to repaint because committing the
    /// options page raises the shell's font-and-colour change event, which the editor's format
    /// map providers subscribe to. <c>RefreshCache</c> on its own does not raise it — which is
    /// why writes landed in storage and nothing on screen moved.
    ///
    /// Both maps are keyed by the same string the storage uses, so no separate table is needed:
    /// if the classification registry knows the name it is a classification, otherwise it is a
    /// format definition.
    /// </summary>
    internal sealed class EditorFormatBridge
    {
        /// <summary>The appearance category the code editor views use.</summary>
        private const string AppearanceCategory = "text";

        /// <summary>
        /// Surfaces the editor paints from a classification type as well as from the format
        /// definition of the same-but-differently-cased name.
        ///
        /// The gutter is the case that matters. The format definition is <c>Line Number</c>;
        /// the classification type the margin actually renders the digits with is
        /// <c>line number</c>, lowercase, and they are two distinct entries. Writing only the
        /// first is why the trace reads "bridge 'Line Number' -&gt; format map OK" on every
        /// preset while a classification dump taken seconds later still shows
        /// <c>'line number' fg=#000000</c> and the gutter on screen never moves.
        ///
        /// <c>IClassificationTypeRegistryService.GetClassificationType</c> is an ordinal
        /// lookup, so it will not find one from the other — the pairing has to be stated.
        /// Kept to an explicit table rather than a case-insensitive scan: applying "Plain Text"
        /// to the <c>text</c> classification as well would put the preset background back on
        /// the text runs, which is the light-band-behind-the-code regression.
        /// </summary>
        private static readonly Dictionary<string, string> SurfaceClassificationAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Line Number", "line number" },

                // The collapsed-region box. The format definition is "Collapsible Text
                // (Collapsed)"; the classification the adornment renders the hint text with
                // carries a suffix — a dump shows it sitting at fg=#808080, which is the
                // unreadable grey on a collapsed #region, while our write to the unsuffixed
                // name reported OK.
                { "Collapsible Text (Collapsed)", "Collapsible Text (Collapsed) {LegacyMarker}" },
                { "Collapsible Text (Expanded)", "Collapsible Text (Expanded) {LegacyMarker}" },
            };

        /// <summary>
        /// What each format definition held before we first touched it, keyed by format name.
        ///
        /// This exists because a format definition has no inheritance chain. A classification
        /// type does — <c>ClearForegroundBrush</c> on one genuinely means "fall back to the
        /// classification below me" — but an <see cref="IEditorFormatMap"/> entry is the whole
        /// answer for that surface, so removing the Color key does not restore Visual Studio's
        /// value, it deletes it. The trace shows exactly that: after a reset, the read-back of
        /// 'TextView Background' reports the map is EMPTY, and 'Collapsible Text (Collapsed)'
        /// comes back holding nothing but IsBold and IsItalic. A collapsed <c>#region</c> with
        /// no foreground entry is not painted grey, it is not painted at all — which is the
        /// missing collapsed-region text — and the same deletion is what strips the overview
        /// margin of the entries that separate it from the page.
        ///
        /// So the original is snapshotted on first touch and put back verbatim on inherit.
        /// Snapshotted rather than re-read: by the time the user asks to inherit, the live
        /// dictionary holds our colour, so it is no longer a source of the default.
        /// </summary>
        /// Static, because the thing it describes is static. The format maps belong to the MEF
        /// composition and live as long as the process; a bridge instance lives as long as the
        /// tool window. Reopening the window built a fresh bridge that snapshotted "pristine"
        /// from a map already holding our colours — the trace catches it taking the snapshot
        /// seconds after the write it is supposed to predate:
        ///
        ///   bridge pristine 'outlining.chevron.collapsed' captured: 4 entry(ies)
        ///
        /// From that point "inherit" restores our own colour, which is why setting every slot to
        /// None stopped undoing anything and why toggling the VS theme was the only way back.
        private static readonly Dictionary<string, ResourceDictionary> _pristineFormats =
            new Dictionary<string, ResourceDictionary>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The channels this session has actually painted, as "key/channel".
        ///
        /// Inherit means "give this channel back", and a channel we never took is not ours to
        /// give back — touching it is how choosing a Foreground palette with Text area set to
        /// None repainted the page white. Compose emits every channel on every apply, including
        /// the ones a None slot owns, so the bridge is the only place that knows the difference
        /// between "the user cleared this" and "this was never set".
        /// </summary>
        /// Static for the same reason as <see cref="_pristineFormats"/>: ownership is a fact about
        /// the map, not about the window. A per-instance set meant a reopened window believed it
        /// had painted nothing, so every "inherit" became a no-op and None reverted nothing.
        private static readonly HashSet<string> _painted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly IClassificationTypeRegistryService _types;
        private readonly IClassificationFormatMap _classifications;
        private readonly IEditorFormatMap _formats;

        private EditorFormatBridge(
            IClassificationTypeRegistryService types,
            IClassificationFormatMap classifications,
            IEditorFormatMap formats)
        {
            _types = types;
            _classifications = classifications;
            _formats = formats;
        }

        public static EditorFormatBridge Create(IServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var components = services.GetService(typeof(SComponentModel)) as IComponentModel;
            if (components == null)
                return null;

            var types = components.GetService<IClassificationTypeRegistryService>();
            var classificationMaps = components.GetService<IClassificationFormatMapService>();
            var formatMaps = components.GetService<IEditorFormatMapService>();
            if (types == null || classificationMaps == null || formatMaps == null)
                return null;

            var bridge = new EditorFormatBridge(
                types,
                classificationMaps.GetClassificationFormatMap(AppearanceCategory),
                formatMaps.GetEditorFormatMap(AppearanceCategory));

            // Indexed here, outside any batch. CurrentPriorityOrder does not answer the same way
            // from inside BeginBatchUpdate: DumpAll, which runs on demand and unbatched, lists
            // 'Collapsible Text (Collapsed) {LegacyMarker}' plainly, while the identical scan run
            // from ApplyOne — which is always inside a batch — reported no candidate containing
            // "ollaps" at all. The lookup was not matching the wrong name; it was reading an
            // empty list. So the names are captured once, up front, and the batch never asks.
            bridge.IndexClassifications();
            return bridge;
        }

        /// <summary>
        /// Repaints every open view for the whole batch. Batched on both maps: each map raises
        /// a changed event per set, and every view re-does its line layout on that event.
        /// </summary>
        /// <returns>The number of items that could not be pushed.</returns>
        public int Apply(IEnumerable<ItemViewModel> batch)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            int failed = 0;

            // BeginBatchUpdate throws if a batch is already open — including one left open by
            // an earlier throw. Batching is an optimisation, not a requirement: if it cannot
            // be opened, apply unbatched rather than losing the whole flush.
            // Tracked per map. Opening them under one flag meant that when the second throw,
            // the first stayed open forever and poisoned every subsequent flush with
            // "BeginBatchUpdate called twice".
            // Re-indexed here, before any batch opens. Indexing once at construction was still
            // too early: the index came back with 711 names and none of them collapsed-region
            // types, because those are registered lazily the first time an outlining region
            // renders — which is after the package loads. And it cannot be done inside the
            // batch, where the priority order reads empty. Before the batch, on every apply, is
            // the only window where the list is both populated and readable.
            if (!_byName.ContainsKey("Collapsible Text (Collapsed)"))
                IndexClassifications();

            // Two passes, each inside its own map's batch, and never nested.
            //
            // The classification format map opens a batch on the underlying editor format map as
            // part of its own — which is why asking for a second one throws "BeginBatchUpdate
            // called twice". Writing format-definition keys while that borrowed batch is open
            // means the classification map closes it, and the FormatMappingChanged that carries
            // those keys is raised as part of *its* commit. The consumers that redraw from a
            // format definition directly — the selection layer above all, which reads
            // 'Selected Text' out of the map and caches the brush until it hears otherwise —
            // do not see it, so the new selection colour was written correctly, verified
            // correctly, and only appeared after a restart rebuilt the maps from storage.
            //
            // Splitting the passes lets the editor format map open, and more importantly close,
            // its own batch, which is what raises the event those consumers listen to.
            var formatItems = new List<ItemViewModel>();
            var classificationItems = new List<ItemViewModel>();
            foreach (var item in batch)
            {
                if (RoutesToFormatMap(item))
                    formatItems.Add(item);
                else
                    classificationItems.Add(item);
            }

            failed += ApplyPass(formatItems, _formats.BeginBatchUpdate, _formats.EndBatchUpdate, "format");
            failed += ApplyPass(classificationItems,
                                _classifications.BeginBatchUpdate, _classifications.EndBatchUpdate,
                                "classification");

            // Written here rather than per channel: the baselines this batch captured are worth
            // nothing if the process ends before they reach disk, and the batch is the natural
            // boundary — one file write per apply, not one per item.
            PristineStore.Save();
            return failed;
        }

        /// <summary>
        /// Which map an item's write will land in. Mirrors <see cref="ApplyOne"/> — the two must
        /// agree, or an item is batched on one map and written to the other, which is the
        /// situation the split exists to prevent.
        /// </summary>
        private bool RoutesToFormatMap(ItemViewModel item)
        {
            if (item.IsSurface)
                return true;

            try { return _types.GetClassificationType(item.StorageName) == null; }
            catch { return true; }
        }

        /// <summary>Applies one map's worth of items inside that map's own batch.</summary>
        private int ApplyPass(List<ItemViewModel> items, Action begin, Action end, string label)
        {
            if (items.Count == 0)
                return 0;

            bool batched = false;
            try
            {
                begin();
                batched = true;
            }
            catch (Exception ex)
            {
                // Batching is an optimisation, not a requirement: apply unbatched rather than
                // lose the flush.
                Diag.Log("  bridge " + label + " BeginBatchUpdate failed (" + ex.Message + "); unbatched");
            }

            int failed = 0;
            try
            {
                foreach (var item in items)
                {
                    if (!ApplyOne(item))
                        failed++;
                }
            }
            finally
            {
                if (batched)
                    try { end(); }
                    catch (Exception ex) { Diag.Log("  bridge " + label + " EndBatchUpdate failed: " + ex.Message); }
            }
            return failed;
        }

        /// <summary>
        /// Logs every classification type the editor knows about with the colour the format map
        /// is currently painting it. When the colour on screen matches no item you are editing,
        /// this is what names the classification that actually owns it.
        /// </summary>
        public void DumpAll()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            int count = 0;
            try
            {
                // CurrentPriorityOrder is the supported enumeration: every classification type
                // the map knows about, in the order they layer. The registry service itself
                // exposes no enumeration API — reflecting for a "ClassificationTypes" property
                // finds nothing, which is why the first attempt at this dump came back empty.
                var all = _classifications.CurrentPriorityOrder;
                if (all == null)
                {
                    Diag.Log("DUMP: format map exposes no priority order.");
                    return;
                }

                Diag.Log("DUMP BEGIN ===== " + all.Count + " types, lowest priority first =====");
                foreach (var entry in all)
                {
                    var type = entry as IClassificationType;
                    if (type == null || string.IsNullOrEmpty(type.Classification))
                        continue;

                    string fg = "<inherited>", bg = "<inherited>";
                    bool bold = false;
                    try
                    {
                        var props = _classifications.GetTextProperties(type);
                        if (!props.ForegroundBrushEmpty)
                            fg = ColorMath.ToHex(BrushToColorRef(props.ForegroundBrush, 0));
                        if (!props.BackgroundBrushEmpty)
                            bg = ColorMath.ToHex(BrushToColorRef(props.BackgroundBrush, 0));
                        bold = !props.BoldEmpty && props.Bold;
                    }
                    catch (Exception ex)
                    {
                        fg = "<error: " + ex.Message + ">";
                    }

                    Diag.Log("DUMP  fg=" + fg.PadRight(11) + " bg=" + bg.PadRight(11)
                             + (bold ? " bold" : "     ") + "  '" + type.Classification + "'");
                    count++;
                }
                Diag.Log("DUMP END: " + count + " classification type(s).");
            }
            catch (Exception ex)
            {
                Diag.Log("DUMP failed: " + ex);
            }
        }

        /// <summary>
        /// Logs every editor format definition with the colours the map is holding for it right
        /// now. The classification dump answers "which classification owns this text"; this one
        /// answers "which format definition owns this box", and they are disjoint sets — the
        /// collapsed-region indicator is in the second and has never appeared in the first.
        ///
        /// Read straight out of the map rather than from our own records, so an item we write
        /// and an item something else overwrites afterwards look different here.
        /// </summary>
        public void DumpFormats(IEnumerable<string> keys)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            int count = 0;
            Diag.Log("FORMATS BEGIN =====");
            foreach (var key in keys)
            {
                try
                {
                    var props = _formats.GetProperties(key);
                    if (props == null)
                    {
                        Diag.Log("FORMATS  <no dictionary>            '" + key + "'");
                        continue;
                    }

                    uint value;
                    string fg = TryReadChannel(props, EditorFormatDefinition.ForegroundColorId,
                                               EditorFormatDefinition.ForegroundBrushId, out value)
                        ? ColorMath.ToHex(value) : "<unset>";
                    string bg = TryReadChannel(props, EditorFormatDefinition.BackgroundColorId,
                                               EditorFormatDefinition.BackgroundBrushId, out value)
                        ? ColorMath.ToHex(value) : "<unset>";

                    Diag.Log("FORMATS  fg=" + fg.PadRight(11) + " bg=" + bg.PadRight(11)
                             + " keys=" + props.Count + "  '" + key + "'");
                    count++;
                }
                catch (Exception ex)
                {
                    Diag.Log("FORMATS  <error: " + ex.Message + ">  '" + key + "'");
                }
            }
            Diag.Log("FORMATS END: " + count + " format definition(s).");
        }

        /// <summary>
        /// The editor's real plain-text colours, as the views are painting them right now.
        ///
        /// <c>GetRGBOfIndex(CI_SYSPLAINTEXT_BK)</c> answers a different question — the *system*
        /// plain text colour, which is white regardless of the VS theme. Using it made every
        /// preview swatch white, every inherited foreground black, and every contrast ratio
        /// wrong, on a dark theme.
        /// </summary>
        public bool TryGetEditorColors(out uint foreground, out uint background)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreground = 0;
            background = 0;

            try
            {
                var defaults = _classifications.DefaultTextProperties;
                if (defaults == null)
                    return false;

                bool any = false;
                if (!defaults.ForegroundBrushEmpty)
                {
                    foreground = BrushToColorRef(defaults.ForegroundBrush, foreground);
                    any = true;
                }
                if (!defaults.BackgroundBrushEmpty)
                {
                    background = BrushToColorRef(defaults.BackgroundBrush, background);
                    any = true;
                }

                // The view background lives on the editor format map, not on the text run.
                var plain = _formats.GetProperties("Plain Text");
                if (plain != null)
                {
                    uint value;
                    if (TryReadChannel(plain, EditorFormatDefinition.ForegroundColorId, EditorFormatDefinition.ForegroundBrushId, out value))
                    {
                        foreground = value;
                        any = true;
                    }
                    if (TryReadChannel(plain, EditorFormatDefinition.BackgroundColorId, EditorFormatDefinition.BackgroundBrushId, out value))
                    {
                        background = value;
                        any = true;
                    }
                }

                return any;
            }
            catch (Exception ex)
            {
                Diag.Log("  bridge editor colours failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Reads an item's current colours straight from the maps the editor paints from.
        ///
        /// This is the fallback for everything <c>IVsFontAndColorStorage.GetItem</c> rejects
        /// with REGDB_E_KEYMISSING. Registration in the Fonts and Colors category is a
        /// persistence detail; it is not what decides whether the editor can paint the item,
        /// and gating the list on it left 43 of 45 rows invisible.
        /// </summary>
        /// <summary>
        /// Every classification type the editor knows about. The registry service exposes no
        /// enumeration API; the format map's priority order does.
        /// </summary>
        public IEnumerable<string> EnumerateClassificationNames()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var order = _classifications.CurrentPriorityOrder;
            if (order == null)
                yield break;

            foreach (var type in order)
            {
                if (type != null && !string.IsNullOrEmpty(type.Classification))
                    yield return type.Classification;
            }
        }

        public bool TryRead(string storageName, out ItemColors colors)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            colors = null;

            try
            {
                var type = _types.GetClassificationType(storageName);
                if (type != null)
                {
                    var props = _classifications.GetTextProperties(type);
                    colors = new ItemColors
                    {
                        ForegroundInherited = props.ForegroundBrushEmpty,
                        BackgroundInherited = props.BackgroundBrushEmpty,
                        ForegroundRgb = BrushToColorRef(props.ForegroundBrushEmpty ? null : props.ForegroundBrush, 0x00000000u),
                        BackgroundRgb = BrushToColorRef(props.BackgroundBrushEmpty ? null : props.BackgroundBrush, 0x00FFFFFFu),
                        Bold = !props.BoldEmpty && props.Bold
                    };
                    return true;
                }

                var dict = _formats.GetProperties(storageName);
                if (dict == null)
                    return false;

                uint fg, bg;
                bool fgSet = TryReadChannel(dict, EditorFormatDefinition.ForegroundColorId, EditorFormatDefinition.ForegroundBrushId, out fg);
                bool bgSet = TryReadChannel(dict, EditorFormatDefinition.BackgroundColorId, EditorFormatDefinition.BackgroundBrushId, out bg);

                colors = new ItemColors
                {
                    ForegroundRgb = fgSet ? fg : 0x00000000u,
                    BackgroundRgb = bgSet ? bg : 0x00FFFFFFu,
                    ForegroundInherited = !fgSet,
                    BackgroundInherited = !bgSet,
                    Bold = false
                };
                return true;
            }
            catch (Exception ex)
            {
                Diag.Log("  bridge read '" + storageName + "' failed: " + ex.Message);
                return false;
            }
        }

        private static bool TryReadChannel(ResourceDictionary props, string colorId, string brushId, out uint rgb)
        {
            rgb = 0;

            if (props.Contains(colorId) && props[colorId] is System.Windows.Media.Color)
            {
                rgb = ColorMath.ToColorRef((System.Windows.Media.Color)props[colorId]);
                return true;
            }

            var brush = props.Contains(brushId) ? props[brushId] as System.Windows.Media.SolidColorBrush : null;
            if (brush != null)
            {
                rgb = ColorMath.ToColorRef(brush.Color);
                return true;
            }

            return false;
        }

        private static uint BrushToColorRef(System.Windows.Media.Brush brush, uint fallback)
        {
            var solid = brush as System.Windows.Media.SolidColorBrush;
            return solid != null ? ColorMath.ToColorRef(solid.Color) : fallback;
        }

        private bool ApplyOne(ItemViewModel item)
        {
            try
            {
                // Surfaces go to the editor format map even when a classification type of the
                // same name exists. The classification map only ever paints text runs, which is
                // why "Plain Text" produced light bands behind the code instead of repainting
                // the editor, and why the gutter never changed at all.
                if (item.IsSurface && ApplyFormatDefinition(item.StorageName, item.Colors))
                {
                    // A surface may also own a classification type under a differently cased
                    // name, and for the gutter that second one is what paints. Both or neither.
                    string alias;
                    if (SurfaceClassificationAliases.TryGetValue(item.StorageName, out alias))
                    {
                        var aliasType = ResolveClassification(alias);
                        if (aliasType != null)
                        {
                            ApplyClassification(aliasType, item.Colors);
                            Diag.Log("  bridge '" + item.StorageName + "' -> format map + classification '"
                                     + alias + "' OK (surface)");
                            return true;
                        }
                        Diag.Log("  bridge '" + item.StorageName + "' -> format map OK (surface); "
                                 + "classification '" + alias + "' not registered yet");
                    }

                    Diag.Log("  bridge '" + item.StorageName + "' -> format map OK (surface)");
                    return true;
                }

                var type = _types.GetClassificationType(item.StorageName);
                if (type != null)
                {
                    ApplyClassification(type, item.Colors);
                    Diag.Log("  bridge '" + item.StorageName + "' -> classification map OK");
                    return true;
                }

                bool known = ApplyFormatDefinition(item.StorageName, item.Colors);
                Diag.Log("  bridge '" + item.StorageName + "' -> format map "
                         + (known ? "OK" : "KEY NOT IN MAP"));
                return known;
            }
            catch (Exception ex)
            {
                // One unknown name must not abort the rest of the batch. The item is still
                // written to storage; only the live repaint is missing for it.
                Diag.Log("  bridge '" + item.StorageName + "' -> EXCEPTION " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Finds a classification type by name, falling back to the format map's own priority
        /// order when the registry does not know it.
        ///
        /// The registry answers only for types registered through MEF. The collapsed-region text
        /// is not one of them: a dump lists 'Collapsible Text (Collapsed) {LegacyMarker}' among
        /// the types the format map is painting, while
        /// <c>GetClassificationType</c> on that exact string returns null — which is why every
        /// apply logged "classification ... not registered yet" and skipped the only write that
        /// would have coloured the text. The format definition of the same name does take the
        /// write, and the trace proves it holds a readable colour afterwards; nothing paints
        /// from it.
        ///
        /// The priority order is the authoritative list of what this map can paint, so it is
        /// the right thing to ask. Matched case-insensitively, because the gutter has already
        /// shown that these names differ from their display names only by case.
        /// </summary>
        /// <summary>
        /// Every classification the format map can paint, by name and by stem, captured once
        /// while unbatched.
        /// </summary>
        private readonly Dictionary<string, IClassificationType> _byName =
            new Dictionary<string, IClassificationType>(StringComparer.OrdinalIgnoreCase);

        private void IndexClassifications()
        {
            try
            {
                var order = _classifications.CurrentPriorityOrder;
                if (order == null)
                {
                    Diag.Log("bridge index: the format map exposes no priority order yet.");
                    return;
                }

                foreach (var entry in order)
                {
                    var type = entry as IClassificationType;
                    if (type == null || string.IsNullOrEmpty(type.Classification))
                        continue;

                    _byName[type.Classification] = type;

                    // Also under the stem, so "Collapsible Text (Collapsed)" finds the suffixed
                    // type without anyone having to spell the suffix correctly.
                    string stem = Stem(type.Classification);
                    if (!_byName.ContainsKey(stem))
                        _byName[stem] = type;
                }

                Diag.Log("bridge index: " + _byName.Count + " classification name(s) captured.");
            }
            catch (Exception ex)
            {
                Diag.Log("bridge index failed: " + ex.Message);
            }
        }

        private IClassificationType ResolveClassification(string name)
        {
            var direct = _types.GetClassificationType(name);
            if (direct != null)
                return direct;

            IClassificationType indexed;
            if (_byName.TryGetValue(name, out indexed)
                || _byName.TryGetValue(Stem(name), out indexed))
            {
                Diag.Log("  bridge classification '" + name + "' resolved from the index to '"
                         + indexed.Classification + "'");
                return indexed;
            }

            var order = _classifications.CurrentPriorityOrder;
            if (order == null)
                return null;

            foreach (var entry in order)
            {
                var type = entry as IClassificationType;
                if (type != null && string.Equals(type.Classification, name, StringComparison.OrdinalIgnoreCase))
                {
                    Diag.Log("  bridge classification '" + name + "' resolved from the format map's "
                             + "priority order (the registry does not know it)");
                    return type;
                }
            }

            // The suffix is not a stable contract. "{LegacyMarker}" was read off one dump of one
            // build, and matching it exactly is how this lookup kept returning null while the
            // type sat in the priority order under a slightly different tail. Match on the stem
            // instead — the part before the brace — which is the name the surface is keyed by.
            string stem = Stem(name);
            foreach (var entry in order)
            {
                var type = entry as IClassificationType;
                if (type == null || string.IsNullOrEmpty(type.Classification))
                    continue;

                if (string.Equals(Stem(type.Classification), stem, StringComparison.OrdinalIgnoreCase))
                {
                    Diag.Log("  bridge classification '" + name + "' resolved by stem to '"
                             + type.Classification + "'");
                    return type;
                }
            }

            // Nothing matched. Name the near misses rather than logging a bare failure: the
            // deciding fact is what this build actually calls the type, and only the map knows.
            var near = new List<string>();
            foreach (var entry in order)
            {
                var type = entry as IClassificationType;
                if (type != null && !string.IsNullOrEmpty(type.Classification)
                    && type.Classification.IndexOf("ollaps", StringComparison.OrdinalIgnoreCase) >= 0)
                    near.Add(type.Classification);
            }
            Diag.Log("  bridge classification '" + name + "' NOT FOUND. Candidates containing "
                     + "'ollaps': " + (near.Count == 0 ? "<none>" : string.Join(" | ", near.ToArray())));
            return null;
        }

        /// <summary>The part of a classification name before any decorating suffix.</summary>
        private static string Stem(string name)
        {
            int brace = name.IndexOf('{');
            return (brace < 0 ? name : name.Substring(0, brace)).Trim();
        }

        private void ApplyClassification(IClassificationType type, ItemColors colors)
        {
            var props = _classifications.GetTextProperties(type);

            // Same ownership rule as the format map. Clearing a channel we never set is not a
            // no-op: it drops the run's inherited colour and the text comes back as the
            // default, which is the whole editor going white the moment a slot is set to None.
            string fgOwned = type.Classification + "/fg";
            string bgOwned = type.Classification + "/bg";

            // What this classification looked like before we first touched it. Clearing a brush
            // does not hand back the theme's colour — it drops the run to the editor default,
            // which is why setting every slot to None left the file dim and grey instead of
            // back on Visual Studio's own dark palette. The themed value lives in this map and
            // nowhere else once we have overwritten it, so it has to be kept.
            var pristine = PristineRun(type, props);

            string name = type.Classification;

            if (colors.ForegroundCleared)
            {
                // No ownership test, for the reason given in SetChannel. On a run, "painted with
                // nothing" is what ClearForegroundBrush means, so the channel falls through to
                // whatever sits below it instead of to a colour we chose.
                CaptureRunBaseline(pristine, name, PristineStore.Foreground);
                props = props.ClearForegroundBrush();
                _painted.Remove(fgOwned);
            }
            else if (colors.ForegroundInherited)
            {
                // Ownership survives the restart through the baseline, for the reason given in
                // SetChannel: a run painted last session is still ours to hand back.
                if (_painted.Remove(fgOwned) || PristineStore.Has(name, PristineStore.Foreground))
                {
                    _painted.Remove(fgOwned);
                    props = RestoreRun(props, pristine, name, PristineStore.Foreground);
                }
            }
            else
            {
                CaptureRunBaseline(pristine, name, PristineStore.Foreground);
                props = props.SetForeground(ColorMath.ToWpf(colors.ForegroundRgb));
                _painted.Add(fgOwned);
            }

            if (colors.BackgroundCleared)
            {
                CaptureRunBaseline(pristine, name, PristineStore.Background);
                props = props.ClearBackgroundBrush();
                _painted.Remove(bgOwned);
            }
            else if (colors.BackgroundInherited)
            {
                if (_painted.Remove(bgOwned) || PristineStore.Has(name, PristineStore.Background))
                {
                    _painted.Remove(bgOwned);
                    props = RestoreRun(props, pristine, name, PristineStore.Background);
                }
            }
            else
            {
                CaptureRunBaseline(pristine, name, PristineStore.Background);
                props = props.SetBackground(ColorMath.ToWpf(colors.BackgroundRgb));
                _painted.Add(bgOwned);
            }

            props = props.SetBold(colors.Bold);

            _classifications.SetTextProperties(type, props);
        }

        /// <summary>The run equivalent of <see cref="CaptureBaseline"/>.</summary>
        private static void CaptureRunBaseline(
            TextFormattingRunProperties pristine, string name, string channel)
        {
            if (PristineStore.Has(name, channel))
                return;

            bool foreground = channel == PristineStore.Foreground;
            bool empty = pristine == null
                || (foreground ? pristine.ForegroundBrushEmpty : pristine.BackgroundBrushEmpty);

            uint rgb = 0;
            bool set = false;
            if (!empty)
            {
                var brush = foreground ? pristine.ForegroundBrush : pristine.BackgroundBrush;
                var solid = brush as System.Windows.Media.SolidColorBrush;
                if (solid != null)
                {
                    rgb = ColorMath.ToColorRef(solid.Color);
                    set = !ThemeStore.WroteChannel(name, foreground, rgb);
                }
            }

            PristineStore.Capture(name, channel, set, rgb);
        }

        /// <summary>The run equivalent of <see cref="RestoreChannel"/>.</summary>
        private static TextFormattingRunProperties RestoreRun(
            TextFormattingRunProperties props, TextFormattingRunProperties pristine,
            string name, string channel)
        {
            bool foreground = channel == PristineStore.Foreground;

            bool wasSet;
            uint rgb;
            if (!PristineStore.TryGet(name, channel, out wasSet, out rgb))
            {
                bool empty = foreground ? pristine.ForegroundBrushEmpty : pristine.BackgroundBrushEmpty;
                if (empty)
                    return foreground ? props.ClearForegroundBrush() : props.ClearBackgroundBrush();

                return foreground
                    ? props.SetForegroundBrush(pristine.ForegroundBrush)
                    : props.SetBackgroundBrush(pristine.BackgroundBrush);
            }

            if (!wasSet)
                return foreground ? props.ClearForegroundBrush() : props.ClearBackgroundBrush();

            var brush = new System.Windows.Media.SolidColorBrush(ColorMath.ToWpf(rgb));
            brush.Freeze();
            return foreground ? props.SetForegroundBrush(brush) : props.SetBackgroundBrush(brush);
        }

        /// <summary>
        /// Names whose write is worth verifying by reading it straight back.
        ///
        /// "format map OK" is a weaker claim than it looks: <see cref="IEditorFormatMap"/>
        /// hands back a dictionary for a key nothing has ever registered, so a write to a name
        /// that no margin consumes succeeds and logs OK exactly like one that works. The
        /// breakpoint bar is the open case — the trace shows the write succeeding at every
        /// layer while the bar on screen does not move — so log what the map holds afterwards.
        /// A read-back that shows our colour proves the map took it and moves the question to
        /// the margin; one that shows nothing proves the key is a phantom.
        /// </summary>
        private static readonly HashSet<string> VerifyWrites =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Indicator Margin",
                "TextView Background",
                "Plain Text",
                "Selected Text",
                "Collapsible Text (Collapsed)",
                "outlining.chevron.collapsed",

                // The format key is lowercase and dotted. "Outlining Vertical Rule" is the
                // display name, and probing under it meant this surface — the one drawing the
                // guide lines that come out uneven — was never actually verified.
                "outlining.verticalrule",
                "OverviewMarginBackground",
            };

        private bool ApplyFormatDefinition(string key, ItemColors colors)
        {
            var props = _formats.GetProperties(key);
            if (props == null)
                return false;

            var pristine = Pristine(key, props);

            SetChannel(props, pristine, key, "fg",
                EditorFormatDefinition.ForegroundColorId,
                EditorFormatDefinition.ForegroundBrushId,
                colors.ForegroundInherited,
                colors.ForegroundCleared,
                colors.ForegroundRgb);

            SetChannel(props, pristine, key, "bg",
                EditorFormatDefinition.BackgroundColorId,
                EditorFormatDefinition.BackgroundBrushId,
                colors.BackgroundInherited,
                colors.BackgroundCleared,
                colors.BackgroundRgb);

            _formats.SetProperties(key, props);

            if (VerifyWrites.Contains(key))
                LogReadBack(key);

            return true;
        }

        /// <summary>Logs every entry the format map holds for a key, right after a write.</summary>
        private void LogReadBack(string key)
        {
            try
            {
                var stored = _formats.GetProperties(key);
                if (stored == null)
                {
                    Diag.Log("  verify '" + key + "': map returned null after the write.");
                    return;
                }

                if (stored.Count == 0)
                {
                    Diag.Log("  verify '" + key + "': map is EMPTY after the write — "
                             + "nothing registered this name, so nothing paints from it.");
                    return;
                }

                var parts = new List<string>();
                foreach (var entry in stored.Keys)
                {
                    object value = stored[entry];
                    parts.Add(entry + "=" + (value == null ? "<null>" : value.ToString()));
                }
                Diag.Log("  verify '" + key + "': " + string.Join(", ", parts.ToArray()));
            }
            catch (Exception ex)
            {
                Diag.Log("  verify '" + key + "' failed: " + ex.Message);
            }
        }

        /// <summary>
        /// The first-touch snapshot of a format definition, taken before we write to it.
        /// </summary>
        /// <summary>Text-run properties as they were before this session first painted them.</summary>
        /// Static, for the reason given on <see cref="_pristineFormats"/>.
        private static readonly Dictionary<string, TextFormattingRunProperties> _pristineRuns =
            new Dictionary<string, TextFormattingRunProperties>(StringComparer.OrdinalIgnoreCase);

        private TextFormattingRunProperties PristineRun(IClassificationType type, TextFormattingRunProperties live)
        {
            TextFormattingRunProperties snapshot;
            if (!_pristineRuns.TryGetValue(type.Classification, out snapshot))
            {
                snapshot = live;
                _pristineRuns[type.Classification] = snapshot;
            }
            return snapshot;
        }

        private ResourceDictionary Pristine(string key, ResourceDictionary live)
        {
            ResourceDictionary snapshot;
            if (_pristineFormats.TryGetValue(key, out snapshot))
                return snapshot;

            snapshot = new ResourceDictionary();
            foreach (var entry in live.Keys)
            {
                try { snapshot[entry] = live[entry]; }
                catch (Exception ex) { Diag.Log("  bridge pristine '" + key + "' entry skipped: " + ex.Message); }
            }

            _pristineFormats[key] = snapshot;
            Diag.Log("  bridge pristine '" + key + "' captured: " + snapshot.Count + " entry(ies)");
            return snapshot;
        }

        /// <summary>
        /// Writes both the Color key and the Brush key for a channel, to the same value.
        ///
        /// Dropping the brush and writing only the colour — which is what this did — is what
        /// turned the collapsed-region indicator black. The two keys are not alternatives with
        /// the brush merely winning: which one a consumer reads depends on the consumer. Text
        /// runs go through the classification format map, which recomputes a brush from the
        /// colour; an adornment drawn in WPF reads <c>props[ForegroundBrushId]</c> straight out
        /// of the dictionary and has nothing to paint with once that key is gone. A differential
        /// dump catches it in the key count alone:
        ///
        ///   GOOD: fg=#DCDCDC bg=#1E1E1E keys=6  'outlining.chevron.collapsed'
        ///   BAD:  fg=#ABB2BF bg=#3E4451 keys=4  'outlining.chevron.collapsed'
        ///
        /// Right colour, two keys missing, black box on screen. Visual Studio's own entries carry
        /// both — every verify read-back of an untouched item shows 'Foreground' beside
        /// 'ForegroundColor' — so writing both is the convention, not a belt-and-braces measure.
        ///
        /// Inherit restores what <paramref name="pristine"/> held for this channel rather than
        /// removing the keys. Removing them leaves the surface with no colour at all, which for
        /// a format definition is not "inherited", it is "unpainted".
        /// </summary>
        private void SetChannel(
            ResourceDictionary props, ResourceDictionary pristine, string key, string channel,
            string colorId, string brushId, bool inherited, bool cleared, uint rgb)
        {
            string owned = key + "/" + channel;

            if (cleared)
            {
                // An instruction, not a default, so no ownership test: the whole point is to
                // remove a colour XoCrazy did not put there. Both keys go, because a format
                // definition with neither is the only thing the editor treats as unpainted —
                // see the SetChannel remarks on why writing one without the other is not it.
                CaptureBaseline(pristine, key, channel, colorId, brushId);
                props.Remove(brushId);
                props.Remove(colorId);
                _painted.Remove(owned);
                return;
            }

            if (inherited)
            {
                // Not ours, so leave it exactly as it is. This is the guard that keeps a
                // Foreground-only change from reaching the page background.
                //
                // A persisted baseline counts as ownership too: it is only ever written when we
                // paint, and it is the half of the answer that survives a restart. Without it a
                // background applied last session could not be cleared this session — the set
                // was empty, so Clear returned here having done nothing, which is half of the
                // transparent-does-nothing bug.
                if (!_painted.Contains(owned) && !PristineStore.Has(key, channel))
                    return;

                RestoreChannel(props, pristine, key, channel, colorId, brushId);
                _painted.Remove(owned);
                return;
            }

            CaptureBaseline(pristine, key, channel, colorId, brushId);

            var color = ColorMath.ToWpf(rgb);

            // Frozen: the dictionary is shared across every view in the process, and an unfrozen
            // brush left in it is one the editor may not touch from its own thread.
            var brush = new System.Windows.Media.SolidColorBrush(color);
            brush.Freeze();

            props[brushId] = brush;
            props[colorId] = color;
            _painted.Add(owned);
        }

        private static void RestoreEntry(ResourceDictionary props, ResourceDictionary pristine, string id)
        {
            if (pristine != null && pristine.Contains(id))
                props[id] = pristine[id];
            else
                props.Remove(id);
        }

        /// <summary>
        /// Records what this channel held before we painted it, for the one process that gets to
        /// see the honest answer. See <see cref="PristineStore"/> for why the in-process
        /// snapshot is not enough on its own.
        /// </summary>
        private static void CaptureBaseline(
            ResourceDictionary pristine, string key, string channel, string colorId, string brushId)
        {
            if (PristineStore.Has(key, channel))
                return;

            uint rgb = 0;
            bool set = pristine != null && TryReadChannel(pristine, colorId, brushId, out rgb);
            if (set && ThemeStore.WroteChannel(key, channel == PristineStore.Foreground, rgb))
                set = false;   // our own colour from an earlier session, not the theme's

            PristineStore.Capture(key, channel, set, rgb);
        }

        /// <summary>
        /// Puts a channel back the way Visual Studio had it. The persisted baseline wins over
        /// the in-process snapshot, which is only trustworthy in the process that took it.
        ///
        /// A baseline of "unpainted" is restored by removing both keys, and that is deliberate:
        /// it is what makes the picker's Clear button produce a genuinely transparent channel
        /// instead of writing some colour back.
        /// </summary>
        private static void RestoreChannel(
            ResourceDictionary props, ResourceDictionary pristine, string key, string channel,
            string colorId, string brushId)
        {
            bool wasSet;
            uint rgb;
            if (!PristineStore.TryGet(key, channel, out wasSet, out rgb))
            {
                RestoreEntry(props, pristine, brushId);
                RestoreEntry(props, pristine, colorId);
                return;
            }

            if (!wasSet)
            {
                props.Remove(brushId);
                props.Remove(colorId);
                return;
            }

            var color = ColorMath.ToWpf(rgb);
            var brush = new System.Windows.Media.SolidColorBrush(color);
            brush.Freeze();
            props[brushId] = brush;
            props[colorId] = color;
        }
    }
}
