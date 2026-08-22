using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;

namespace ThemeForge.Core
{
    /// <summary>
    /// Owns the working set: reads every tracked item once, applies edits live, and can put
    /// everything back the way it was. One session per tool window instance.
    /// </summary>
    internal sealed class ThemeForgeSession : IDisposable
    {
        private readonly IServiceProvider _services;
        private readonly FontColorStore _store;
        private readonly EditorFormatBridge _bridge;
        private readonly LiveApplyQueue _queue;
        private readonly EditHistory _history = new EditHistory();

        /// <summary>Each item as it was at the end of the last apply — the undo "before".</summary>
        private readonly Dictionary<string, ItemColors> _committed =
            new Dictionary<string, ItemColors>(StringComparer.Ordinal);

        private bool _suppressHistory;

        /// <summary>Set while previewing: the editor repaints, the saved theme does not move.</summary>
        private bool _suppressStore;
        private uint _cachedEditorBackground;
        private uint _cachedEditorForeground;

        public ObservableCollection<ItemViewModel> Items { get; private set; }

        /// <summary>Raised after a live apply lands, so the UI can refresh derived state.</summary>
        public event EventHandler Applied;

        public bool IsReady { get { return _store != null; } }

        /// <summary>
        /// Set when the last apply could not fully land. Null when everything went through.
        /// A write that fails is otherwise indistinguishable from one that worked.
        /// </summary>
        public string LastApplyError { get; private set; }

        public ThemeForgeSession(IServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _services = services;
            _store = FontColorStore.Create(services);
            if (_store != null)
                _store.NameMapServices = services;   // lets writes resolve display item names
            _bridge = EditorFormatBridge.Create(services);
            Diag.Log("Session created; store=" + (_store != null) + " bridge=" + (_bridge != null));
            Items = new ObservableCollection<ItemViewModel>();
            _queue = new LiveApplyQueue(Flush);

            RefreshEditorColors();
        }

        // ---- loading -----------------------------------------------------------------

        /// <summary>Loads the curated short list. This is the default view.</summary>
        public void LoadCurated()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var models = ClassificationCatalog.Curated.Select(e =>
                new ItemViewModel(FontColorCategories.TextEditor, e.StorageName, e.DisplayName, e.Group, e.Hint, EditorBackground))
                .ToList();

            // The discovered margins join the list as ordinary rows rather than being a hidden
            // side channel used only by "paint the whole editor". As rows they get undo, the
            // picker, the store and revert for free — a separate path would have needed all
            // four reimplemented, and would still have made undo miss the gutter.
            var surfaces = new HashSet<string>(Surfaces(), StringComparer.OrdinalIgnoreCase);
            var known = new HashSet<string>(models.Select(m => m.StorageName), StringComparer.OrdinalIgnoreCase);

            foreach (var surface in surfaces)
            {
                if (known.Contains(surface))
                    continue;
                models.Add(new ItemViewModel(FontColorCategories.TextEditor, surface, surface,
                                             ClassificationCatalog.GroupSurface, surface, EditorBackground));
            }

            foreach (var model in models)
                model.IsSurface = surfaces.Contains(model.StorageName);

            Load(models);
        }

        /// <summary>
        /// Loads every classification the editor knows about. Reachable, but not the
        /// default — the whole point of the tool is not making you read this list.
        /// </summary>
        public void LoadAll()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in ClassificationCatalog.Curated)
                names.Add(e.StorageName);

            // The format map's priority order, not reflection over the registry service — that
            // reflection found no property and silently yielded nothing, so "Show every item"
            // only ever widened to the curated list it started from.
            if (_bridge != null)
            {
                foreach (var name in _bridge.EnumerateClassificationNames())
                    names.Add(name);
            }

            Diag.Log("LoadAll: " + names.Count + " candidate item(s).");

            var surfaceNames = new HashSet<string>(Surfaces(), StringComparer.OrdinalIgnoreCase);
            foreach (var surface in surfaceNames)
                names.Add(surface);

            Load(names.Select(name =>
            {
                var known = ClassificationCatalog.Find(name);
                var model = new ItemViewModel(
                    FontColorCategories.TextEditor,
                    name,
                    known != null ? known.DisplayName : name,
                    known != null ? known.Group : "All items",
                    known != null ? known.Hint : name,
                    EditorBackground);

                model.IsSurface = surfaceNames.Contains(name);
                return model;
            }));
        }

        private static IEnumerable<string> EnumerateClassificationTypes(IClassificationTypeRegistryService registry)
        {
            // The registry exposes the collection through an interface member on the concrete
            // implementation; reflection keeps this working across editor versions where the
            // public surface has moved.
            var property = registry.GetType().GetProperty("ClassificationTypes");
            if (property == null)
                yield break;

            var value = property.GetValue(registry, null) as System.Collections.IEnumerable;
            if (value == null)
                yield break;

            foreach (var entry in value)
            {
                var type = entry as IClassificationType;
                if (type != null && !string.IsNullOrEmpty(type.Classification))
                    yield return type.Classification;
            }
        }

        private void Load(IEnumerable<ItemViewModel> models)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (var existing in Items)
                existing.Changed -= OnItemChanged;
            Items.Clear();

            if (_store == null)
                return;

            RefreshEditorColors();

            int skipped = 0;
            foreach (var model in models)
            {
                var colors = _store.Read(model.Category, model.StorageName);

                if (colors == null && _bridge != null)
                {
                    // Not registered in the Fonts and Colors category — which says nothing
                    // about whether the editor can paint it. Read what the editor is actually
                    // using instead of dropping the row.
                    ItemColors fromEditor;
                    if (_bridge.TryRead(model.StorageName, out fromEditor))
                    {
                        // An unset channel paints as the editor's plain text colour, not as
                        // black-on-white. Resolve it so inherited rows preview truthfully.
                        if (fromEditor.ForegroundInherited)
                            fromEditor.ForegroundRgb = _cachedEditorForeground;
                        if (fromEditor.BackgroundInherited)
                            fromEditor.BackgroundRgb = _cachedEditorBackground;

                        colors = fromEditor;
                    }
                }

                if (colors == null)
                {
                    // Dropping these silently is how 43 of 45 rows disappeared with no clue why.
                    skipped++;
                    Diag.Log("Load skipped '" + model.StorageName + "' — " + _store.LastReadFailure
                             + "; editor format maps have no entry either.");
                    continue;
                }

                model.SetColors(colors);
                model.Original = colors.Clone();
                model.Changed += OnItemChanged;
                Items.Add(model);

                // The undo baseline follows the reload; the history itself does not. Reloading
                // happens every time the tool window becomes visible, and dropping the stack
                // there would silently make undo forget everything on a tab switch.
                _committed[KeyOf(model)] = colors.Clone();
            }

            Diag.Log("Load complete: " + Items.Count + " item(s) listed, " + skipped + " skipped.");
        }

        private uint EditorBackground()
        {
            return _cachedEditorBackground;
        }

        /// <summary>
        /// Refreshes the cached editor plain-text colours. The format map is asked first: it
        /// reports what the views actually paint, theme included. The shell resolver's
        /// CI_SYSPLAINTEXT_* indices report the system colours, which are white-on-black
        /// regardless of theme and are only a last resort.
        /// </summary>
        private void RefreshEditorColors()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_store == null)
                return;

            uint fg, bg;
            if (_bridge != null && _bridge.TryGetEditorColors(out fg, out bg))
            {
                _cachedEditorForeground = fg;
                _cachedEditorBackground = bg;
            }
            else
            {
                _cachedEditorForeground = _store.Resolver.EditorForeground();
                _cachedEditorBackground = _store.Resolver.EditorBackground();
            }

            Diag.Log("Editor colours: fg=" + ColorMath.ToHex(_cachedEditorForeground)
                     + " bg=" + ColorMath.ToHex(_cachedEditorBackground));
        }

        // ---- live apply --------------------------------------------------------------

        private void OnItemChanged(object sender, EventArgs e)
        {
            _queue.Queue((ItemViewModel)sender);
        }

        /// <summary>Applies immediately — call on mouse-up so a drag settles without waiting.</summary>
        public void FlushNow()
        {
            _queue.FlushNow();
        }

        /// <summary>
        /// The queue's drain callback runs on a dispatcher tick. An exception escaping it is
        /// not recoverable from here — the tick is gone and every later edit silently does
        /// nothing while the window still looks alive. Nothing may throw out of this method.
        /// </summary>
        private void Flush(IReadOnlyCollection<ItemViewModel> batch)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                FlushCore(batch);
            }
            catch (Exception ex)
            {
                Diag.Log("Flush threw " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
                LastApplyError = "Apply failed: " + ex.Message;
                if (Applied != null) Applied(this, EventArgs.Empty);
            }
        }

        private void FlushCore(IReadOnlyCollection<ItemViewModel> batch)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_store == null || batch.Count == 0)
                return;

            RecordHistory(batch);

            var touched = new HashSet<Guid>();
            int writeFailures = 0;
            string unpersisted = null;
            foreach (var item in batch)
            {
                bool ok = _store.Write(item.Category, item.StorageName, item.Colors);
                if (!ok)
                    unpersisted = item.DisplayName;
                Diag.Log("Flush write '" + item.StorageName + "' fg=" + ColorMath.ToHex(item.Colors.ForegroundRgb)
                         + " bg=" + ColorMath.ToHex(item.Colors.BackgroundRgb)
                         + " fgInherit=" + item.Colors.ForegroundInherited
                         + " bgInherit=" + item.Colors.BackgroundInherited
                         + " -> " + (ok ? "OK" : "storage: " + _store.LastWriteFailure));
                if (ok)
                    touched.Add(item.Category);
                else
                    writeFailures++;

                // The durable copy, and the one that does not depend on the item having a
                // Fonts and Colors entry. This is what survives the restart.
                if (!_suppressStore)
                    ThemeStore.Record(item);
            }
            if (!_suppressStore)
                ThemeStore.Save();

            // One refresh per category, not per item: each one re-classifies every open view.
            // Isolated: a cache manager throw here must not take the repaint below with it.
            foreach (var category in touched)
            {
                try
                {
                    _store.Refresh(category);
                }
                catch (Exception ex)
                {
                    Diag.Log("  Refresh(" + category.ToString("B") + ") threw "
                             + ex.GetType().Name + ": " + ex.Message);
                }
            }

            // Persistence is done; now make the editor actually show it. The format maps are
            // what the views paint from, and nothing above this line touches them.
            int paintFailures = 0;
            if (_bridge != null)
                paintFailures = _bridge.Apply(batch);

            LastApplyError = BuildApplyError(writeFailures, paintFailures, unpersisted);

            RefreshEditorColors();
            foreach (var item in Items)
                item.RaiseAll();

            if (Applied != null) Applied(this, EventArgs.Empty);
        }

        // ---- undo / redo -------------------------------------------------------------

        private static string KeyOf(ItemViewModel item)
        {
            return item.Category.ToString("N") + "|" + item.StorageName.ToLowerInvariant();
        }

        /// <summary>
        /// Turns one apply into one history step, using the state captured at the end of the
        /// previous apply as the "before". Also refreshes that baseline, which is why it runs
        /// even while history is suppressed — an undo that did not update the baseline would
        /// make the next edit's "before" a state the item was never in.
        /// </summary>
        private void RecordHistory(IReadOnlyCollection<ItemViewModel> batch)
        {
            var before = new Dictionary<string, ItemColors>(StringComparer.Ordinal);
            var after = new Dictionary<string, ItemColors>(StringComparer.Ordinal);

            foreach (var item in batch)
            {
                var key = KeyOf(item);
                ItemColors previous;
                before[key] = _committed.TryGetValue(key, out previous)
                    ? previous
                    : (item.Original != null ? item.Original.Clone() : item.Colors.Clone());
                after[key] = item.Colors.Clone();
            }

            if (!_suppressHistory)
            {
                string label = batch.Count == 1
                    ? batch.First().DisplayName
                    : batch.Count + " items";
                _history.Push(label, before, after);
            }

            foreach (var pair in after)
                _committed[pair.Key] = pair.Value.Clone();
        }

        public bool CanUndo { get { return _history.CanUndo; } }
        public bool CanRedo { get { return _history.CanRedo; } }
        public string UndoLabel { get { return _history.UndoLabel; } }
        public string RedoLabel { get { return _history.RedoLabel; } }

        /// <summary>
        /// Ends the current gesture, so the next edit starts a new undo step. Called on
        /// mouse-up and after every discrete action — without it a drag and the click after it
        /// would collapse into one step.
        /// </summary>
        public void CloseHistoryGroup()
        {
            _history.CloseGroup();
        }

        public bool Undo()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var step = _history.Undo();
            if (step == null) return false;
            ApplyStates(step.Before, "undo " + step.Label);
            return true;
        }

        public bool Redo()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var step = _history.Redo();
            if (step == null) return false;
            ApplyStates(step.After, "redo " + step.Label);
            return true;
        }

        /// <summary>Current colours of every loaded row, keyed for <see cref="ApplyStates"/>.</summary>
        public Dictionary<string, ItemColors> CaptureAll()
        {
            var map = new Dictionary<string, ItemColors>(StringComparer.Ordinal);
            foreach (var item in Items)
                map[KeyOf(item)] = item.Colors.Clone();
            return map;
        }

        /// <summary>
        /// Pushes a captured set of states back onto the rows. Never recorded as a new step:
        /// this is the mechanism undo, redo and preview are all built from, and a preview that
        /// filled the undo stack would be worse than no preview.
        /// </summary>
        public void ApplyStates(Dictionary<string, ItemColors> states, string reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (states == null || states.Count == 0)
                return;

            var byKey = Items.ToDictionary(KeyOf, StringComparer.Ordinal);

            _suppressHistory = true;
            try
            {
                foreach (var pair in states)
                {
                    ItemViewModel item;
                    if (!byKey.TryGetValue(pair.Key, out item))
                        continue;
                    if (item.Colors.SameAs(pair.Value))
                        continue;

                    item.SetColors(pair.Value.Clone());
                    _queue.Queue(item);
                }
                _queue.FlushNow();
            }
            finally
            {
                _suppressHistory = false;
            }

            _history.CloseGroup();
            Diag.Log("ApplyStates(" + reason + "): " + states.Count + " item(s).");
        }

        private string BuildApplyError(int writeFailures, int paintFailures, string unpersisted)
        {
            if (_bridge == null)
                return "The editor format maps are unavailable, so open views will not repaint. "
                     + "Colours are still saved and will be applied after a restart.";

            // A storage failure is no longer a data-loss event: ThemeStore has the value and
            // ThemeApplier re-applies it on the next start. It only means the colour will not
            // show up in the shell's own Fonts and Colors page, which is worth saying once
            // rather than raising as an error.
            if (writeFailures > 0)
                Diag.Log("Flush: " + writeFailures + " item(s) have no Fonts and Colors entry ("
                         + (unpersisted ?? "?") + "); saved to " + ThemeStore.CurrentPath + " instead.");

            if (paintFailures > 0)
                return paintFailures + " item(s) saved but are not painted by the editor's format maps.";

            return null;
        }

        // ---- bulk operations ---------------------------------------------------------

        public void RevertAll()
        {
            foreach (var item in Items)
            {
                if (item.Original == null || item.Original.SameAs(item.Colors))
                    continue;
                item.SetColors(item.Original.Clone());
                _queue.Queue(item);
            }
            _queue.FlushNow();
            _history.CloseGroup();
        }

        public void Revert(ItemViewModel item)
        {
            if (item.Original == null) return;
            item.SetColors(item.Original.Clone());
            _queue.Queue(item);
            _queue.FlushNow();
            _history.CloseGroup();
        }

        /// <summary>
        /// Applies a whole palette.
        ///
        /// The records are saved first and applied second, deliberately. A preset names items
        /// that may not be loaded yet — Roslyn's classifications do not exist until a C# file
        /// has been opened — and those rows would otherwise be dropped on the floor. Saved
        /// first, <see cref="ThemeApplier"/> picks them up as soon as they register.
        /// </summary>
        public void ApplyPreset(ThemePreset preset, BackgroundMode mode, Dictionary<string, ItemColors> baseline)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (preset == null) return;

            var records = preset.ToRecords(FontColorCategories.TextEditor, mode, Surfaces());
            ThemeStore.RecordRange(records);
            ThemeStore.Save();

            ApplyStates(StatesFor(records), "preset " + preset.Name);

            // One undo step for the whole preset, measured from wherever the user was before
            // the picker opened — not from the last preview they hovered over.
            if (baseline != null)
                PushHistoryStep("preset " + preset.Name, baseline);

            ThemeApplier.Reassert("preset " + preset.Name);
            Diag.Log("Preset '" + preset.Name + "' applied: " + records.Count + " item(s), background mode="
                     + mode);
        }

        /// <summary>
        /// Applies the three-slot selection: a syntax palette, a text-area palette and an
        /// editor-surface palette, any of which may be null for "leave it to Visual Studio".
        ///
        /// Saved before applied, for the same reason as <see cref="ApplyPreset"/>: a palette
        /// names items no view has registered yet, and those rows would otherwise be dropped.
        /// </summary>
        public void ApplySelection(ThemePreset foreground, ThemePreset textArea, ThemePreset editor,
                                   Dictionary<string, ItemColors> baseline)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var records = ThemePreset.Compose(FontColorCategories.TextEditor,
                                              foreground, textArea, editor, Surfaces());

            ThemeStore.RecordRange(records);
            ThemeStore.Save();

            ApplyStates(StatesFor(records), "selection");

            if (baseline != null)
                PushHistoryStep("theme selection", baseline);

            ThemeApplier.Reassert("selection");
            Diag.Log("Selection applied: fg=" + (foreground != null ? foreground.Name : "None")
                     + " textArea=" + (textArea != null ? textArea.Name : "None")
                     + " editor=" + (editor != null ? editor.Name : "None")
                     + "; " + records.Count + " item(s).");
        }

        /// <summary>Paints a three-slot selection live without saving it anywhere.</summary>
        public void PreviewSelection(ThemePreset foreground, ThemePreset textArea, ThemePreset editor)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var records = ThemePreset.Compose(FontColorCategories.TextEditor,
                                              foreground, textArea, editor, Surfaces());
            PreviewStates(StatesFor(records), "preview selection");
        }

        /// <summary>
        /// The surfaces the editor paints a background on, discovered from the running
        /// composition. Cached by <see cref="SurfaceCatalog"/>.
        /// </summary>
        private IReadOnlyList<string> Surfaces()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return SurfaceCatalog.Discover(_services, _bridge);
        }

        /// <summary>
        /// Paints one colour across the whole editor window — text area, gutter, breakpoint
        /// bar, outlining strip, overview margin — without touching a single foreground.
        ///
        /// Written straight to the store and re-asserted rather than applied only to the
        /// loaded rows: most margins are not in the curated list, so a rows-only apply would
        /// visibly repaint the text and leave the bands it was supposed to fix.
        /// </summary>
        public int PaintEditorBackground(uint rgb)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var hex = ColorMath.ToHex(rgb);
            var records = new List<Snapshot.Record>();

            foreach (var surface in Surfaces())
            {
                var existing = Items.FirstOrDefault(
                    i => string.Equals(i.StorageName, surface, StringComparison.OrdinalIgnoreCase));

                records.Add(new Snapshot.Record
                {
                    Category = FontColorCategories.TextEditor,
                    Item = surface,
                    // Foregrounds are preserved: the gutter's numbers and the margin's glyphs
                    // are coloured by their own roles and blanking them would empty the gutter.
                    Foreground = existing != null && !existing.Colors.ForegroundInherited
                        ? ColorMath.ToHex(existing.Colors.ForegroundRgb)
                        : null,
                    Background = hex,
                    Bold = existing != null && existing.Colors.Bold
                });
            }

            var baseline = CaptureAll();

            ThemeStore.RecordRange(records);
            ThemeStore.Save();
            ApplyStates(StatesFor(records), "editor background " + hex);
            PushHistoryStep("editor background " + hex, baseline);
            ThemeApplier.Reassert("editor background");

            Diag.Log("Editor background " + hex + " applied to " + records.Count + " surface(s).");
            return records.Count;
        }

        /// <summary>
        /// Paints a palette onto the real editor without saving it anywhere. This is what
        /// makes the picker answer "what does this look like in my code" instead of "what does
        /// this look like on a card".
        /// </summary>
        public void PreviewPreset(ThemePreset preset, BackgroundMode mode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (preset == null) return;

            var records = preset.ToRecords(FontColorCategories.TextEditor, mode, Surfaces());
            PreviewStates(StatesFor(records), "preview " + preset.Name);
        }

        /// <summary>Applies states live, leaving the saved theme on disk untouched.</summary>
        public void PreviewStates(Dictionary<string, ItemColors> states, string reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _suppressStore = true;
            try
            {
                ApplyStates(states, reason);
            }
            finally
            {
                _suppressStore = false;
            }
        }

        /// <summary>Maps records onto the rows that are actually loaded.</summary>
        private Dictionary<string, ItemColors> StatesFor(IEnumerable<Snapshot.Record> records)
        {
            var byKey = Items.ToDictionary(KeyOf, StringComparer.Ordinal);
            var states = new Dictionary<string, ItemColors>(StringComparer.Ordinal);

            foreach (var record in records)
            {
                var key = record.Category.ToString("N") + "|" + record.Item.ToLowerInvariant();
                ItemViewModel item;
                if (!byKey.TryGetValue(key, out item))
                    continue;
                states[key] = Snapshot.ToColors(record, item.Colors);
            }
            return states;
        }

        /// <summary>
        /// Records one step spanning everything that changed since <paramref name="baseline"/>
        /// was captured. Used for actions that reach the rows through several applies.
        /// </summary>
        public void PushHistoryStep(string label, Dictionary<string, ItemColors> baseline)
        {
            var byKey = Items.ToDictionary(KeyOf, StringComparer.Ordinal);
            var before = new Dictionary<string, ItemColors>(StringComparer.Ordinal);
            var after = new Dictionary<string, ItemColors>(StringComparer.Ordinal);

            foreach (var pair in baseline)
            {
                ItemViewModel item;
                if (!byKey.TryGetValue(pair.Key, out item))
                    continue;
                if (item.Colors.SameAs(pair.Value))
                    continue;      // unchanged rows would make the step needlessly wide

                before[pair.Key] = pair.Value.Clone();
                after[pair.Key] = item.Colors.Clone();
            }

            if (after.Count == 0)
                return;

            _history.Push(label, before, after);
            _history.CloseGroup();
        }


        /// <summary>Drops the saved theme so the next start shows the VS theme untouched.</summary>
        public void ForgetSaved()
        {
            ThemeStore.Clear();
        }

        /// <summary>
        /// Undoes ThemeForge entirely: every item it has ever touched goes back to whatever the
        /// active Visual Studio theme says, right now, permanently.
        ///
        /// This is not <see cref="RevertAll"/>. That one restores the state the window opened
        /// with, which on the second session is still a ThemeForged editor — the saved theme was
        /// re-applied at startup before the window ever loaded a row. Nor is it
        /// <see cref="ForgetSaved"/>, which only stops the *next* start from re-asserting and
        /// leaves the current screen painted.
        ///
        /// Three separate places hold an override and all three have to be cleared, in this
        /// order, or the colours come straight back:
        ///
        ///   1. The MEF format maps — clearing the brush is what makes a definition fall back
        ///      to its themed default. Writing the theme's colour explicitly would look right
        ///      and then survive the next VS theme switch, which is the bug being fixed.
        ///   2. Fonts and Colors storage — set to CT_AUTOMATIC, so the shell's own page agrees
        ///      and a cache rebuild does not resurrect the value.
        ///   3. ThemeStore — dropped, so <see cref="ThemeApplier"/> has nothing to re-assert.
        ///
        /// The set spans the store as well as the loaded rows: a preset writes items no view
        /// has registered yet, and those are exactly the ones a rows-only reset would leave
        /// behind to reappear at the next restart.
        /// </summary>
        /// <returns>How many distinct items were handed back to the theme.</returns>
        public int ResetToVisualStudioDefaults()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_store == null)
                return 0;

            var targets = new Dictionary<string, Snapshot.Record>(StringComparer.Ordinal);

            foreach (var record in ThemeStore.All())
                targets[record.Category.ToString("N") + "|" + record.Item.ToLowerInvariant()] =
                    new Snapshot.Record { Category = record.Category, Item = record.Item };

            foreach (var item in Items)
                targets[KeyOf(item)] = new Snapshot.Record
                {
                    Category = item.Category,
                    Item = item.StorageName
                };

            // Foreground, Background and Bold all left at their defaults: null, null, false —
            // which Snapshot.ToColors reads as "inherit both channels", and which the bridge
            // turns into a cleared brush rather than a written colour.
            var defaults = new List<Snapshot.Record>(targets.Values);
            var surfaces = new HashSet<string>(Surfaces(), StringComparer.OrdinalIgnoreCase);

            // Straight to the bridge for the whole set. ApplyStates would silently drop every
            // item that is not a currently loaded row, and those are the ones that matter most.
            if (_bridge != null)
            {
                var batch = defaults.Select(record =>
                {
                    var known = ClassificationCatalog.Find(record.Item);
                    var model = new ItemViewModel(
                        record.Category,
                        record.Item,
                        known != null ? known.DisplayName : record.Item,
                        known != null ? known.Group : "Reset",
                        known != null ? known.Hint : record.Item,
                        EditorBackground);
                    model.SetColors(Snapshot.ToColors(record, null));
                    model.IsSurface = surfaces.Contains(record.Item);
                    return model;
                }).ToList();

                int failed = _bridge.Apply(batch);
                Diag.Log("Reset: " + (batch.Count - failed) + "/" + batch.Count + " item(s) cleared in the format maps.");
            }

            int storageCleared = 0;
            foreach (var record in defaults)
            {
                if (_store.Write(record.Category, record.Item, Snapshot.ToColors(record, null)))
                    storageCleared++;
            }

            ThemeStore.Clear();
            PresetSelection.Clear();

            // Rebuild the shell's cache from the theme. Without this the Fonts and Colors page
            // keeps showing the old values until the next restart even though the editor has
            // already let go of them.
            _store.Refresh(FontColorCategories.TextEditor);

            // The applier is driven by the store; with the store empty it stops on its own at
            // the next tick, but a theme switch in between would otherwise re-push the batch it
            // is still holding.
            ThemeApplier.Reassert("reset to VS defaults");

            // The history describes edits against colours that no longer exist. Keeping it
            // would let one Ctrl+Z put the whole theme back, which is not what a reset means.
            _history.Clear();
            _committed.Clear();

            Diag.Log("Reset to VS defaults: " + defaults.Count + " item(s), "
                     + storageCleared + " also cleared in Fonts and Colors storage.");
            return defaults.Count;
        }

        public void Apply(IEnumerable<Snapshot.Record> records)
        {
            var byKey = Items.ToDictionary(
                i => i.Category.ToString("N") + "|" + i.StorageName,
                StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                ItemViewModel item;
                if (!byKey.TryGetValue(record.Category.ToString("N") + "|" + record.Item, out item))
                    continue;
                item.SetColors(Snapshot.ToColors(record, item.Colors));
                _queue.Queue(item);
            }
            _queue.FlushNow();
            _history.CloseGroup();
        }

        /// <summary>Writes the full classification-to-colour map to the diagnostic log.</summary>
        public void DumpClassifications()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_bridge == null)
            {
                Diag.Log("DUMP: no editor format bridge.");
                return;
            }

            _bridge.DumpAll();

            // And the other half. A surface that paints from a format definition never shows up
            // in the classification dump, so a colour on screen that matches nothing in that list
            // is not evidence that nothing owns it — only that the wrong list was searched.
            try
            {
                _bridge.DumpFormats(FontColorNameMap.AllFormatNames(_services));
            }
            catch (Exception ex)
            {
                Diag.Log("FORMATS dump failed: " + ex.Message);
            }
        }

        public int DirtyCount
        {
            get { return Items.Count(i => i.IsDirty); }
        }

        public void Dispose()
        {
            _queue.FlushNow();
            _queue.Dispose();
            foreach (var item in Items)
                item.Changed -= OnItemChanged;
        }
    }
}
