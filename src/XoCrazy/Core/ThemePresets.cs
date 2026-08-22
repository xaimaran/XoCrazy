using System;
using System.Collections.Generic;
using System.Linq;

namespace XoCrazy.Core
{
    /// <summary>
    /// A palette expressed in roles rather than in Visual Studio item names.
    ///
    /// Published themes describe themselves as "keyword", "string", "type" — one colour per
    /// syntactic role. Visual Studio describes itself as forty-odd display items, several of
    /// which share a role (<c>class name</c>, <c>struct name</c>, <c>record class name</c> are
    /// one colour in every theme anybody actually ships). Storing the role and expanding to
    /// items at apply time is what keeps a preset to twenty lines instead of forty rows, and
    /// what lets a theme that never named a role fall back sensibly instead of painting black.
    /// </summary>
    /// <summary>How far a preset's background is allowed to travel.</summary>
    internal enum BackgroundMode
    {
        /// <summary>Syntax colours only. The VS theme keeps every surface.</summary>
        None = 0,

        /// <summary>The code area only — margins stay on the VS theme.</summary>
        TextArea = 1,

        /// <summary>Text area, gutter, breakpoint bar, outlining strip, overview margin.</summary>
        WholeEditor = 2,
    }

    internal sealed class ThemePreset
    {
        public string Name;
        public string Origin;      // where the palette comes from, shown under the name
        public bool IsDark;
        public Dictionary<string, string> Roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Role keys. Strings, not an enum: presets are data and this file is read as data.
        public const string Background = "bg";
        public const string Foreground = "fg";
        public const string Comment = "comment";
        public const string Keyword = "keyword";
        public const string Control = "control";
        public const string String = "string";
        public const string Escape = "escape";
        public const string Number = "number";
        public const string Operator = "operator";
        public const string Punctuation = "punctuation";
        public const string Identifier = "identifier";
        public const string Preprocessor = "preprocessor";
        public const string Type = "type";
        public const string Interface = "interface";
        public const string Enum = "enum";
        public const string Namespace = "namespace";
        public const string Method = "method";
        public const string Property = "property";
        public const string Field = "field";
        public const string Constant = "constant";
        public const string Local = "local";
        public const string Parameter = "parameter";
        public const string Event = "event";
        public const string Label = "label";
        public const string Selection = "selection";
        public const string LineNumber = "lineNumber";
        public const string CurrentLineNumber = "lineNumberCurrent";
        public const string CollapsedText = "collapsedText";
        public const string Error = "error";
        public const string Warning = "warning";
        public const string Excluded = "excluded";

        /// <summary>
        /// Resolves a role, walking the fallback chain when the palette does not name it.
        /// A missing role must never resolve to "no colour" — that is how a preset ends up
        /// painting half the file in the previous theme's colours.
        /// </summary>
        public string Get(string role)
        {
            string value;
            if (Roles.TryGetValue(role, out value) && !string.IsNullOrEmpty(value))
                return value;

            switch (role)
            {
                case Control: return Get(Keyword);
                case Escape: return Get(String);
                case Interface:
                case Enum:
                case Namespace: return Get(Type);
                case Constant: return Get(Field);
                case Property:
                case Field: return Get(Foreground);
                case Event: return Get(Property);
                case Local:
                case Parameter:
                case Label:
                case Identifier:
                case Punctuation:
                case Operator: return Get(Foreground);
                case Preprocessor: return Get(Keyword);
                case Excluded: return Get(Comment);
                case LineNumber: return Get(Comment);
                // The current line's number is a separate display item. Left unnamed it keeps
                // whatever the previous theme gave it, which reads as one stubbornly
                // wrong-coloured digit in an otherwise repainted gutter.
                case CurrentLineNumber: return Get(Foreground);
                case CollapsedText: return Get(Foreground);
                // Neither of these may reach the default arm. The default answer is the text
                // colour, so a palette that never named a selection resolves its selection to
                // its own foreground — and the collapsed-region block is the one place that
                // paints text *on* the selection colour, so the pair comes out identical and
                // the region reads as blank. Derived from the background instead.
                case Background: return IsDark ? "#1E1E1E" : "#FFFFFF";
                case Selection: return Shade(Get(Background), IsDark ? 0.25 : -0.12);
                case Error: return "#FF5555";
                case Warning: return "#FFCC00";
                default: return Get(Foreground);
            }
        }

        /// <summary>The role each curated item takes its foreground from.</summary>
        private static readonly Dictionary<string, string> ForegroundOf =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Plain Text", Foreground },
                { "keyword", Keyword },
                { "keyword - control", Control },
                { "string", String },
                { "string - verbatim", String },
                { "string - escape character", Escape },
                { "number", Number },
                { "comment", Comment },
                { "operator", Operator },
                { "operator - overloaded", Operator },
                { "punctuation", Punctuation },
                { "identifier", Identifier },
                { "preprocessor keyword", Preprocessor },
                // The text after #region / #pragma. Its own classification, and left unnamed
                // it kept the previous theme's grey — the "#region Configuration Parameters"
                // label that never changed colour with the rest of the file.
                { "preprocessor text", Preprocessor },

                { "class name", Type },
                { "record class name", Type },
                { "struct name", Type },
                { "record struct name", Type },
                { "delegate name", Type },
                { "type parameter name", Type },
                { "interface name", Interface },
                { "enum name", Enum },
                { "namespace name", Namespace },

                { "method name", Method },
                { "extension method name", Method },
                { "property name", Property },
                { "field name", Field },
                { "constant name", Constant },
                { "local name", Local },
                { "parameter name", Parameter },
                { "event name", Event },
                { "label name", Label },

                { "Line Number", LineNumber },
                { "Selected Line Number", CurrentLineNumber },
                { "excluded code", Excluded },

                // The "..." box on a collapsed region. It is in the surface list, so a
                // whole-editor paint gives it the editor background — and nothing gave it a
                // foreground, so the hint text kept the previous theme's colour and ended up
                // invisible against the block it sits in. Both channels, or neither.
                { "Collapsible Text (Collapsed)", CollapsedText },

                // The name VS 2026 actually paints that box with. The legacy entry above is kept
                // for VS 2022, where it is still the live one; on 18.x it resolves to nothing.
                { "outlining.chevron.collapsed", CollapsedText },

                // The same indicator on a region that is open. It sits on the page rather than in
                // a block, so it takes the muted colour a guide line wants and no background.
                { "outlining.chevron.expanded", Comment },

                { "Syntax Error", Error },
                { "Compiler Error", Error },
                { "Other Error", Error },
                { "Warning", Warning },
            };

        /// <summary>
        /// The few items whose *background* is the point. Everything else keeps an inherited
        /// background, so the row still reads correctly if the user later switches VS themes.
        /// </summary>
        private static readonly Dictionary<string, string> BackgroundOf =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Plain Text", Background },
                { "Selected Text", Selection },
                { "Inactive Selected Text", Selection },

                // The collapsed region takes the page background, not the selection colour. VS
                // draws the indicator with its own outline, so the block is already legible
                // without a fill — and a fill spans the whole line, which reads as a highlighted
                // row rather than as a collapsed one. These entries are also what keeps the
                // whole-editor pass from writing them twice — see ToRecords.
                { "Collapsible Text (Collapsed)", Background },
                { "outlining.chevron.collapsed", Background },

                // Drawn on a code line, so it belongs to the text area rather than to the margins
                // around it. Left to the whole-editor pass it took the *editor* page colour and
                // stopped matching the code behind it the moment the two slots differed.
                { "outlining.chevron.expanded", Background },
            };

        /// <summary>
        /// Surfaces that are lines and glyphs rather than areas: they are stroked in their
        /// foreground on top of whatever the row already has, and Visual Studio ships them with
        /// no background at all — a dump of an untouched editor reads
        /// <c>fg=#5B5B5B bg=&lt;unset&gt; 'outlining.verticalrule'</c>.
        ///
        /// Giving one a background is not a no-op. On a row with text the run paints over it and
        /// nothing shows; on an empty row there is nothing to cover it, so the rule fills its
        /// column and the guide line reads heavier than on the rows above and below. That it was
        /// the *editor* page colour is what tied the effect to changing the text-area palette.
        /// </summary>
        private static readonly HashSet<string> NeverFilled =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "outlining.verticalrule",
                "outlining.square",
            };

        /// <summary>
        /// Every name the collapsed-region box is drawn under, across the VS versions this
        /// extension runs on. They are written together because only the running VS knows which
        /// one is live: on 18.x the composition lists 'outlining.chevron.collapsed' and no
        /// definition at all for the legacy name, on 17.x it is the other way round. A write to
        /// the name that is not live costs one trace line; missing the live one costs the region
        /// name, which is the bug this list exists to close.
        /// </summary>
        private static readonly string[] CollapsedBlock =
        {
            "outlining.chevron.collapsed",
            "Collapsible Text (Collapsed)",
        };

        /// <summary>
        /// Expands the palette into one record per affected display item.
        ///
        /// <paramref name="mode"/> decides how far the background travels:
        /// <list type="bullet">
        /// <item><b>None</b> — syntax colours only, the VS theme keeps every surface. The safe
        /// default for mixing a published palette into a theme you already like.</item>
        /// <item><b>TextArea</b> — the code area takes the preset's background; the gutter,
        /// breakpoint bar and overview margin stay on the VS theme, so the two will not
        /// necessarily agree.</item>
        /// <item><b>WholeEditor</b> — every surface in <paramref name="surfaces"/> takes it too,
        /// which is what makes the editor read as one colour instead of a band around the
        /// code.</item>
        /// </list>
        /// </summary>
        public List<Snapshot.Record> ToRecords(Guid category, BackgroundMode mode = BackgroundMode.TextArea,
                                               IEnumerable<string> surfaces = null)
        {
            var byItem = new Dictionary<string, Snapshot.Record>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in ForegroundOf)
            {
                byItem[pair.Key] = new Snapshot.Record
                {
                    Category = category,
                    Item = pair.Key,
                    Foreground = Get(pair.Value),
                    Background = null,
                    Bold = false
                };
            }

            if (mode == BackgroundMode.None)
            {
                // Every background is handed back to the theme rather than merely left alone:
                // a previous preset may have pinned one, and "foreground only" has to mean the
                // editor's own background shows through, not whatever was set last time.
                foreach (var record in byItem.Values)
                    record.Background = null;
                return new List<Snapshot.Record>(byItem.Values);
            }

            foreach (var pair in BackgroundOf)
            {
                Snapshot.Record record;
                if (!byItem.TryGetValue(pair.Key, out record))
                {
                    record = new Snapshot.Record { Category = category, Item = pair.Key };
                    byItem[pair.Key] = record;
                }
                record.Background = Get(pair.Value);
            }

            if (mode == BackgroundMode.TextArea && surfaces != null)
            {
                // The code area is the view background, not the Plain Text run background.
                // Writing only the latter tints the text and leaves the editor dark.
                foreach (var surface in surfaces.Where(
                    s => string.Equals(s, "TextView Background", StringComparison.OrdinalIgnoreCase)))
                {
                    byItem[surface] = new Snapshot.Record
                    {
                        Category = category,
                        Item = surface,
                        Background = Get(Background)
                    };
                }
            }

            // The outlining rule is drawn from its foreground, and this overload never wrote it.
            // The multi-palette path already does (see the "reads heavier on some rows" note
            // there); leaving it out here is why changing only the text area left the guide line
            // on whatever the previous theme set. Nested regions draw coincident rules, so a
            // stale colour stacks and the line reads thicker — most visibly on blank rows, which
            // have no Plain Text run background painted over the stroke to thin it out. Writing
            // the channel on every apply is what keeps it uniform.
            byItem["outlining.verticalrule"] = new Snapshot.Record
            {
                Category = category,
                Item = "outlining.verticalrule",
                Foreground = Get(Comment)
            };

            if (mode == BackgroundMode.WholeEditor && surfaces != null)
            {
                // Every margin gets the same background as the text area. The foreground is
                // left alone: a margin's glyphs and line numbers are already coloured by their
                // own roles, and overwriting those would blank the gutter.
                foreach (var surface in surfaces)
                {
                    // A surface that BackgroundOf named already has the background the palette
                    // meant for it. Overwriting that with the editor background flattens the
                    // collapsed-region block into the page and leaves its hint text floating.
                    if (BackgroundOf.ContainsKey(surface) || NeverFilled.Contains(surface))
                        continue;

                    Snapshot.Record record;
                    if (!byItem.TryGetValue(surface, out record))
                    {
                        record = new Snapshot.Record { Category = category, Item = surface };
                        byItem[surface] = record;
                    }
                    record.Background = Get(Background);
                }
            }

            // Selected Text keeps the theme's foreground: forcing one is how a selection ends
            // up unreadable over a highlight it was never designed against.
            Snapshot.Record selected;
            if (byItem.TryGetValue("Selected Text", out selected))
                selected.Foreground = null;
            if (byItem.TryGetValue("Inactive Selected Text", out selected))
                selected.Foreground = null;

            return new List<Snapshot.Record>(byItem.Values);
        }

        /// <summary>
        /// Builds one record set from three independently chosen palettes.
        ///
        /// Each slot owns a disjoint territory, defined per <em>channel</em> rather than per
        /// item — which is the only split that lets three palettes coexist without one wiping
        /// another's work:
        ///
        ///   * <b>Foreground</b> owns the foreground channel of every syntax item. It never
        ///     writes a background, so choosing it cannot change the colour of the page.
        ///   * <b>TextArea</b> owns the background channel of the code area and the things
        ///     drawn directly on it — the selection, the collapsed-region block.
        ///   * <b>Editor</b> owns the background channel of every surface around the code.
        ///
        /// <c>Plain Text</c> is the case that proves the rule: its foreground belongs to the
        /// first slot and its background to the second, and before this they could not be
        /// chosen separately.
        ///
        /// A null palette is "None", and it is not the same as leaving the slot out. None means
        /// the user is handing that territory back to Visual Studio, so its channels are
        /// written as inherited — otherwise switching a slot to None would leave the palette
        /// that was there before still painted, with no way to remove it.
        /// </summary>
        public static List<Snapshot.Record> Compose(
            Guid category,
            ThemePreset foreground,
            ThemePreset textArea,
            ThemePreset editor,
            IEnumerable<string> surfaces)
        {
            var byItem = new Dictionary<string, Snapshot.Record>(StringComparer.OrdinalIgnoreCase);

            Func<string, Snapshot.Record> at = name =>
            {
                Snapshot.Record record;
                if (!byItem.TryGetValue(name, out record))
                {
                    record = new Snapshot.Record { Category = category, Item = name };
                    byItem[name] = record;
                }
                return record;
            };

            // ---- foreground territory ----------------------------------------------
            foreach (var pair in ForegroundOf)
            {
                at(pair.Key).Foreground = foreground != null ? foreground.Get(pair.Value) : null;
                at(pair.Key).Bold = false;
            }

            // ---- text-area territory ------------------------------------------------
            foreach (var pair in BackgroundOf)
                at(pair.Key).Background = textArea != null ? textArea.Get(pair.Value) : null;

            // The collapsed-region box is the one surface whose inherited background is not
            // supplied by the active theme. It is a legacy Fonts and Colors item, and what it
            // inherits is white — the trace catches the whole failure in one line:
            //
            //   Flush write 'Collapsible Text (Collapsed)' fg=#DCDCDC bg=#FFFFFF bgInherit=True
            //
            // Light grey on white, in a dark editor, which is why setting a Foreground palette
            // appeared to erase the region text. So whichever slot is driving the text also has
            // to supply a background for it; leaving that channel to inherit is not an option
            // here the way it is everywhere else.
            //
            // The two channels also come from two independently chosen palettes — the text from
            // the Foreground slot, the block from whichever slot owns backgrounds — and nothing
            // else in this method makes them differ. Changing only the Foreground slot is what
            // pairs them arbitrarily, and a pair with no contrast is the collapsed region reading
            // as blank. So the text is checked against the block it will actually sit in.
            var box = textArea ?? editor ?? foreground;
            string blockBackground = null;
            if (box != null)
            {
                blockBackground = box.Get(Background);
                string blockText = Readable(
                    (foreground ?? box).Get(CollapsedText), blockBackground, box.Get(Foreground));

                foreach (var name in CollapsedBlock)
                {
                    at(name).Background = blockBackground;
                    at(name).Foreground = blockText;
                }
            }

            // The code area is the view background, not the Plain Text run background. Writing
            // only the latter tints the text and leaves the editor itself untouched.
            at("TextView Background").Background =
                textArea != null ? textArea.Get(Background) : null;

            // Selected Text keeps whatever foreground the syntax slot gave it: forcing one is
            // how a selection ends up unreadable over a highlight it was never designed against.
            at("Selected Text").Foreground = null;
            at("Inactive Selected Text").Foreground = null;

            // ---- editor territory ----------------------------------------------------
            if (surfaces != null)
            {
                string page = editor != null ? editor.Get(Background) : null;

                // The scroll bar's boundary is not a border definition — there is no such key
                // among the surfaces this VS exposes. It is the colour step between the page and
                // the overview margin, which is why painting both with one flat background made
                // it vanish and why it reappears the moment the Editor slot is None. The margin
                // therefore gets a shade of the page rather than the page itself.
                string margin = Shade(page, editor != null && editor.IsDark ? 0.10 : -0.06);

                foreach (var surface in surfaces)
                {
                    // Already spoken for by the text-area slot. Overwriting here is what used
                    // to flatten the collapsed-region block into the page.
                    if (BackgroundOf.ContainsKey(surface)
                        || string.Equals(surface, "TextView Background", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // The collapse hint carries text, so both of its channels are decided
                    // together below rather than here, where only the background is known.
                    if (string.Equals(surface, "outlining.collapsehintadornment",
                                      StringComparison.OrdinalIgnoreCase))
                        continue;

                    // A stroke, not an area. Its background stays unwritten so it sits on
                    // whatever the row already has.
                    if (NeverFilled.Contains(surface))
                        continue;

                    bool isOverview = surface.StartsWith("OverviewMargin", StringComparison.OrdinalIgnoreCase);

                    at(surface).Background = isOverview ? margin : page;
                }

                // The collapse hint is a box drawn *on* the page, not part of it. Giving it the
                // page background made it a light box outlined against a light page with
                // near-light text — the trace caught it writing #D7DDE8 on #F6F7FA — so it takes
                // the same step away from the page that the margin does. With no Editor palette
                // it belongs to the slot that owns the collapsed block, not to nothing: it is
                // the same box, and half-painting it is what left its text floating.
                // The page colour flat, like the collapsed line. It was given a step away from the
                // page on the reasoning that a popup has to separate itself from what it floats
                // over; in practice that reads as a differently-coloured panel in the middle of
                // the file, and the adornment already has its own border.
                string hintBackground = blockBackground ?? page;
                at("outlining.collapsehintadornment").Background = hintBackground;

                // The outlining rules are drawn from their *foreground*, which nothing here ever
                // wrote. Left alone they keep whatever the last theme to touch them left behind,
                // and because nested regions draw coincident rules the leftovers stack — that is
                // the guide line that reads heavier on some rows than others. Writing the channel
                // on every apply is what makes it uniform again; a null palette writes it as
                // inherited, which now restores Visual Studio's own rule instead of deleting it.
                string rule = editor != null ? editor.Get(Comment) : null;
                at("outlining.verticalrule").Foreground = rule;

                // The hint carries readable text, so it takes the palette's foreground rather
                // than the muted rule colour a guide line wants — and, like the block, only if
                // that foreground can be seen against the background just written for it.
                var hintPalette = editor ?? foreground ?? box;
                at("outlining.collapsehintadornment").Foreground =
                    hintBackground == null || hintPalette == null
                        ? null
                        : Readable(hintPalette.Get(Foreground), hintBackground,
                                   hintPalette.Get(Comment));
            }

            return new List<Snapshot.Record>(byItem.Values);
        }

        /// <summary>
        /// The floor a piece of text has to clear against the thing it is drawn on. 3:1 is the
        /// large-text threshold, not the body-text one: the collapsed-region hint is a short,
        /// non-essential label and holding it to 4.5 would reject palettes that read fine.
        /// </summary>
        private const double MinContrast = 3.0;

        /// <summary>
        /// <paramref name="text"/> if it can be read on <paramref name="background"/>,
        /// <paramref name="alternate"/> if that can, otherwise plain white or black.
        ///
        /// This exists because the collapsed-region block is the one item whose two channels are
        /// filled from two different palettes, chosen independently by the user. Every other item
        /// takes both channels from one palette, where the author already guaranteed the pair
        /// works; here nobody has.
        /// </summary>
        private static string Readable(string text, string background, string alternate)
        {
            if (Contrast(text, background) >= MinContrast)
                return text;
            if (Contrast(alternate, background) >= MinContrast)
                return alternate;

            uint rgb;
            if (background == null || !ColorMath.TryParseHex(background, out rgb))
                return text;

            // Nothing in either palette works on this block, so stop asking the palettes.
            var c = ColorMath.ToWpf(rgb);
            double luma = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return luma < 0.5 ? "#FFFFFF" : "#000000";
        }

        /// <summary>
        /// Contrast between two hex colours. An unparseable or absent colour answers "fine":
        /// a channel we cannot measure is one we have no business overriding.
        /// </summary>
        private static double Contrast(string a, string b)
        {
            uint x, y;
            if (a == null || b == null
                || !ColorMath.TryParseHex(a, out x) || !ColorMath.TryParseHex(b, out y))
                return double.MaxValue;

            return ColorMath.ContrastRatio(ColorMath.ToWpf(x), ColorMath.ToWpf(y));
        }

        /// <summary>
        /// Moves a hex colour toward white (positive) or black (negative) by a fraction.
        /// Null in, null out, so a "None" slot stays None rather than becoming grey.
        /// </summary>
        private static string Shade(string hex, double amount)
        {
            uint rgb;
            if (hex == null || !ColorMath.TryParseHex(hex, out rgb))
                return hex;

            var c = ColorMath.ToWpf(rgb);
            return ColorMath.ToHex(ColorMath.ToColorRef(System.Windows.Media.Color.FromRgb(
                Channel(c.R, amount), Channel(c.G, amount), Channel(c.B, amount))));
        }

        private static byte Channel(byte value, double amount)
        {
            double shifted = amount >= 0
                ? value + (255 - value) * amount
                : value * (1 + amount);
            return (byte)(shifted < 0 ? 0 : (shifted > 255 ? 255 : shifted));
        }

        /// <summary>The swatches shown on the preset card, in reading order.</summary>
        public IEnumerable<string> PreviewRoles
        {
            get
            {
                return new[] { Keyword, Control, Type, Method, String, Number, Comment, Operator };
            }
        }
    }

    /// <summary>
    /// The shipped palettes. Values are the published hex codes of each theme, mapped onto
    /// roles — not eyeballed approximations.
    /// </summary>
    internal static class ThemePresets
    {
        private static ThemePreset[] _all;

        public static ThemePreset[] All
        {
            get { return _all ?? (_all = Build()); }
        }

        private static ThemePreset[] Build()
        {
            return new[]
            {
                new ThemePreset
                {
                    Name = "Visual Studio Dark+",
                    Origin = "Microsoft — the VS/VS Code dark default",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#1E1E1E" }, { ThemePreset.Foreground, "#DCDCDC" },
                        { ThemePreset.Comment, "#6A9955" }, { ThemePreset.Keyword, "#569CD6" },
                        { ThemePreset.Control, "#C586C0" }, { ThemePreset.String, "#D69D85" },
                        { ThemePreset.Escape, "#FFD68F" }, { ThemePreset.Number, "#B5CEA8" },
                        { ThemePreset.Type, "#4EC9B0" }, { ThemePreset.Interface, "#B8D7A3" },
                        { ThemePreset.Enum, "#B8D7A3" }, { ThemePreset.Method, "#DCDCAA" },
                        { ThemePreset.Property, "#DCDCDC" }, { ThemePreset.Field, "#DCDCDC" },
                        { ThemePreset.Local, "#9CDCFE" }, { ThemePreset.Parameter, "#9CDCFE" },
                        { ThemePreset.Operator, "#B4B4B4" }, { ThemePreset.Punctuation, "#DCDCDC" },
                        { ThemePreset.Preprocessor, "#9B9B9B" }, { ThemePreset.Namespace, "#DCDCDC" },
                        { ThemePreset.Selection, "#264F78" }, { ThemePreset.LineNumber, "#2B91AF" },
                        { ThemePreset.Error, "#F44747" }, { ThemePreset.Warning, "#FFD700" },
                    }
                },
                new ThemePreset
                {
                    Name = "Dracula",
                    Origin = "draculatheme.com",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#282A36" }, { ThemePreset.Foreground, "#F8F8F2" },
                        { ThemePreset.Comment, "#6272A4" }, { ThemePreset.Keyword, "#FF79C6" },
                        { ThemePreset.String, "#F1FA8C" }, { ThemePreset.Number, "#BD93F9" },
                        { ThemePreset.Type, "#8BE9FD" }, { ThemePreset.Method, "#50FA7B" },
                        { ThemePreset.Parameter, "#FFB86C" }, { ThemePreset.Operator, "#FF79C6" },
                        { ThemePreset.Selection, "#44475A" }, { ThemePreset.LineNumber, "#6272A4" },
                        { ThemePreset.Error, "#FF5555" }, { ThemePreset.Warning, "#F1FA8C" },
                    }
                },
                new ThemePreset
                {
                    Name = "One Dark",
                    Origin = "Atom / One Dark Pro",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#282C34" }, { ThemePreset.Foreground, "#ABB2BF" },
                        { ThemePreset.Comment, "#5C6370" }, { ThemePreset.Keyword, "#C678DD" },
                        { ThemePreset.String, "#98C379" }, { ThemePreset.Number, "#D19A66" },
                        { ThemePreset.Type, "#E5C07B" }, { ThemePreset.Method, "#61AFEF" },
                        { ThemePreset.Property, "#E06C75" }, { ThemePreset.Field, "#E06C75" },
                        { ThemePreset.Operator, "#56B6C2" }, { ThemePreset.Selection, "#3E4451" },
                        { ThemePreset.LineNumber, "#4B5263" }, { ThemePreset.Error, "#E06C75" },
                        { ThemePreset.Warning, "#E5C07B" },
                    }
                },
                new ThemePreset
                {
                    Name = "Nord",
                    Origin = "nordtheme.com",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#2E3440" }, { ThemePreset.Foreground, "#D8DEE9" },
                        { ThemePreset.Comment, "#616E88" }, { ThemePreset.Keyword, "#81A1C1" },
                        { ThemePreset.String, "#A3BE8C" }, { ThemePreset.Number, "#B48EAD" },
                        { ThemePreset.Type, "#8FBCBB" }, { ThemePreset.Method, "#88C0D0" },
                        { ThemePreset.Punctuation, "#ECEFF4" }, { ThemePreset.Operator, "#81A1C1" },
                        { ThemePreset.Selection, "#434C5E" }, { ThemePreset.LineNumber, "#4C566A" },
                        { ThemePreset.Error, "#BF616A" }, { ThemePreset.Warning, "#EBCB8B" },
                    }
                },
                new ThemePreset
                {
                    Name = "Gruvbox Dark",
                    Origin = "morhetz/gruvbox",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#282828" }, { ThemePreset.Foreground, "#EBDBB2" },
                        { ThemePreset.Comment, "#928374" }, { ThemePreset.Keyword, "#FB4934" },
                        { ThemePreset.String, "#B8BB26" }, { ThemePreset.Number, "#D3869B" },
                        { ThemePreset.Type, "#FABD2F" }, { ThemePreset.Method, "#B8BB26" },
                        { ThemePreset.Property, "#83A598" }, { ThemePreset.Field, "#83A598" },
                        { ThemePreset.Operator, "#8EC07C" }, { ThemePreset.Selection, "#504945" },
                        { ThemePreset.LineNumber, "#7C6F64" }, { ThemePreset.Error, "#FB4934" },
                        { ThemePreset.Warning, "#FABD2F" },
                    }
                },
                new ThemePreset
                {
                    Name = "Monokai",
                    Origin = "Sublime Text",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#272822" }, { ThemePreset.Foreground, "#F8F8F2" },
                        { ThemePreset.Comment, "#75715E" }, { ThemePreset.Keyword, "#F92672" },
                        { ThemePreset.String, "#E6DB74" }, { ThemePreset.Number, "#AE81FF" },
                        { ThemePreset.Type, "#66D9EF" }, { ThemePreset.Method, "#A6E22E" },
                        { ThemePreset.Parameter, "#FD971F" }, { ThemePreset.Operator, "#F92672" },
                        { ThemePreset.Selection, "#49483E" }, { ThemePreset.LineNumber, "#90908A" },
                        { ThemePreset.Error, "#F92672" }, { ThemePreset.Warning, "#E6DB74" },
                    }
                },
                new ThemePreset
                {
                    Name = "Solarized Dark",
                    Origin = "Ethan Schoonover",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#002B36" }, { ThemePreset.Foreground, "#839496" },
                        { ThemePreset.Comment, "#586E75" }, { ThemePreset.Keyword, "#859900" },
                        { ThemePreset.String, "#2AA198" }, { ThemePreset.Number, "#D33682" },
                        { ThemePreset.Type, "#B58900" }, { ThemePreset.Method, "#268BD2" },
                        { ThemePreset.Property, "#268BD2" }, { ThemePreset.Field, "#268BD2" },
                        { ThemePreset.Operator, "#859900" }, { ThemePreset.Selection, "#073642" },
                        { ThemePreset.LineNumber, "#586E75" }, { ThemePreset.Error, "#DC322F" },
                        { ThemePreset.Warning, "#B58900" },
                    }
                },
                new ThemePreset
                {
                    Name = "Solarized Light",
                    Origin = "Ethan Schoonover",
                    IsDark = false,
                    Roles =
                    {
                        { ThemePreset.Background, "#FDF6E3" }, { ThemePreset.Foreground, "#657B83" },
                        { ThemePreset.Comment, "#93A1A1" }, { ThemePreset.Keyword, "#859900" },
                        { ThemePreset.String, "#2AA198" }, { ThemePreset.Number, "#D33682" },
                        { ThemePreset.Type, "#B58900" }, { ThemePreset.Method, "#268BD2" },
                        { ThemePreset.Property, "#268BD2" }, { ThemePreset.Field, "#268BD2" },
                        { ThemePreset.Operator, "#859900" }, { ThemePreset.Selection, "#EEE8D5" },
                        { ThemePreset.LineNumber, "#93A1A1" }, { ThemePreset.Error, "#DC322F" },
                        { ThemePreset.Warning, "#B58900" },
                    }
                },
                new ThemePreset
                {
                    Name = "Catppuccin Mocha",
                    Origin = "catppuccin.com",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#1E1E2E" }, { ThemePreset.Foreground, "#CDD6F4" },
                        { ThemePreset.Comment, "#6C7086" }, { ThemePreset.Keyword, "#CBA6F7" },
                        { ThemePreset.String, "#A6E3A1" }, { ThemePreset.Number, "#FAB387" },
                        { ThemePreset.Type, "#F9E2AF" }, { ThemePreset.Method, "#89B4FA" },
                        { ThemePreset.Property, "#B4BEFE" }, { ThemePreset.Field, "#BAC2DE" },
                        { ThemePreset.Parameter, "#EBA0AC" }, { ThemePreset.Operator, "#89DCEB" },
                        { ThemePreset.Punctuation, "#9399B2" }, { ThemePreset.Selection, "#313244" },
                        { ThemePreset.LineNumber, "#6C7086" }, { ThemePreset.Error, "#F38BA8" },
                        { ThemePreset.Warning, "#F9E2AF" },
                    }
                },
                new ThemePreset
                {
                    Name = "Tokyo Night",
                    Origin = "enkia/tokyo-night",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#1A1B26" }, { ThemePreset.Foreground, "#A9B1D6" },
                        { ThemePreset.Comment, "#565F89" }, { ThemePreset.Keyword, "#BB9AF7" },
                        { ThemePreset.String, "#9ECE6A" }, { ThemePreset.Number, "#FF9E64" },
                        { ThemePreset.Type, "#2AC3DE" }, { ThemePreset.Method, "#7AA2F7" },
                        { ThemePreset.Property, "#7DCFFF" }, { ThemePreset.Field, "#7DCFFF" },
                        { ThemePreset.Parameter, "#E0AF68" }, { ThemePreset.Operator, "#89DDFF" },
                        { ThemePreset.Selection, "#283457" }, { ThemePreset.LineNumber, "#3B4261" },
                        { ThemePreset.Error, "#F7768E" }, { ThemePreset.Warning, "#E0AF68" },
                    }
                },
                new ThemePreset
                {
                    Name = "Night Owl",
                    Origin = "Sarah Drasner",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#011627" }, { ThemePreset.Foreground, "#D6DEEB" },
                        { ThemePreset.Comment, "#637777" }, { ThemePreset.Keyword, "#C792EA" },
                        { ThemePreset.String, "#ECC48D" }, { ThemePreset.Number, "#F78C6C" },
                        { ThemePreset.Type, "#FFCB8B" }, { ThemePreset.Method, "#82AAFF" },
                        { ThemePreset.Property, "#80CBC4" }, { ThemePreset.Field, "#80CBC4" },
                        { ThemePreset.Operator, "#C792EA" }, { ThemePreset.Selection, "#1D3B53" },
                        { ThemePreset.LineNumber, "#4B6479" }, { ThemePreset.Error, "#EF5350" },
                        { ThemePreset.Warning, "#FFCB8B" },
                    }
                },
                new ThemePreset
                {
                    Name = "GitHub Dark",
                    Origin = "primer/github-vscode-theme",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#0D1117" }, { ThemePreset.Foreground, "#C9D1D9" },
                        { ThemePreset.Comment, "#8B949E" }, { ThemePreset.Keyword, "#FF7B72" },
                        { ThemePreset.String, "#A5D6FF" }, { ThemePreset.Number, "#79C0FF" },
                        { ThemePreset.Type, "#FFA657" }, { ThemePreset.Method, "#D2A8FF" },
                        { ThemePreset.Property, "#79C0FF" }, { ThemePreset.Field, "#79C0FF" },
                        { ThemePreset.Operator, "#FF7B72" }, { ThemePreset.Selection, "#163356" },
                        { ThemePreset.LineNumber, "#6E7681" }, { ThemePreset.Error, "#FF7B72" },
                        { ThemePreset.Warning, "#D29922" },
                    }
                },
                new ThemePreset
                {
                    Name = "GitHub Light",
                    Origin = "primer/github-vscode-theme",
                    IsDark = false,
                    Roles =
                    {
                        { ThemePreset.Background, "#FFFFFF" }, { ThemePreset.Foreground, "#24292F" },
                        { ThemePreset.Comment, "#6E7781" }, { ThemePreset.Keyword, "#CF222E" },
                        { ThemePreset.String, "#0A3069" }, { ThemePreset.Number, "#0550AE" },
                        { ThemePreset.Type, "#953800" }, { ThemePreset.Method, "#8250DF" },
                        { ThemePreset.Property, "#0550AE" }, { ThemePreset.Field, "#0550AE" },
                        { ThemePreset.Operator, "#CF222E" }, { ThemePreset.Selection, "#B6D7FF" },
                        { ThemePreset.LineNumber, "#8C959F" }, { ThemePreset.Error, "#CF222E" },
                        { ThemePreset.Warning, "#9A6700" },
                    }
                },
                new ThemePreset
                {
                    Name = "Visual Studio Light",
                    Origin = "Microsoft — the VS light default",
                    IsDark = false,
                    Roles =
                    {
                        { ThemePreset.Background, "#FFFFFF" }, { ThemePreset.Foreground, "#000000" },
                        { ThemePreset.Comment, "#008000" }, { ThemePreset.Keyword, "#0000FF" },
                        { ThemePreset.Control, "#8F08C4" }, { ThemePreset.String, "#A31515" },
                        { ThemePreset.Number, "#000000" }, { ThemePreset.Type, "#2B91AF" },
                        { ThemePreset.Method, "#74531F" }, { ThemePreset.Local, "#1F377F" },
                        { ThemePreset.Parameter, "#808080" }, { ThemePreset.Operator, "#000000" },
                        { ThemePreset.Preprocessor, "#808080" }, { ThemePreset.Selection, "#ADD6FF" },
                        { ThemePreset.LineNumber, "#2B91AF" }, { ThemePreset.Error, "#E51400" },
                        { ThemePreset.Warning, "#FF8C00" },
                    }
                },
                new ThemePreset
                {
                    Name = "Darcula",
                    Origin = "JetBrains IntelliJ / Rider",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#2B2B2B" }, { ThemePreset.Foreground, "#A9B7C6" },
                        { ThemePreset.Comment, "#808080" }, { ThemePreset.Keyword, "#CC7832" },
                        { ThemePreset.String, "#6A8759" }, { ThemePreset.Number, "#6897BB" },
                        { ThemePreset.Type, "#A9B7C6" }, { ThemePreset.Method, "#FFC66D" },
                        { ThemePreset.Property, "#9876AA" }, { ThemePreset.Field, "#9876AA" },
                        { ThemePreset.Operator, "#A9B7C6" }, { ThemePreset.Preprocessor, "#BBB529" },
                        { ThemePreset.Selection, "#214283" }, { ThemePreset.LineNumber, "#606366" },
                        { ThemePreset.Error, "#BC3F3C" }, { ThemePreset.Warning, "#BBB529" },
                    }
                },
                new ThemePreset
                {
                    Name = "Ayu Dark",
                    Origin = "dempfi/ayu",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#0B0E14" }, { ThemePreset.Foreground, "#BFBDB6" },
                        { ThemePreset.Comment, "#646B73" }, { ThemePreset.Keyword, "#FF8F40" },
                        { ThemePreset.String, "#AAD94C" }, { ThemePreset.Number, "#D2A6FF" },
                        { ThemePreset.Type, "#59C2FF" }, { ThemePreset.Method, "#FFB454" },
                        { ThemePreset.Operator, "#F29668" }, { ThemePreset.Selection, "#1B3A5B" },
                        { ThemePreset.LineNumber, "#6C7380" }, { ThemePreset.Error, "#D95757" },
                        { ThemePreset.Warning, "#FFB454" },
                    }
                },
                new ThemePreset
                {
                    Name = "Ayu Mirage",
                    Origin = "dempfi/ayu",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#1F2430" }, { ThemePreset.Foreground, "#CCCAC2" },
                        { ThemePreset.Comment, "#707A8C" }, { ThemePreset.Keyword, "#FFA759" },
                        { ThemePreset.String, "#D5FF80" }, { ThemePreset.Number, "#DFBFFF" },
                        { ThemePreset.Type, "#73D0FF" }, { ThemePreset.Method, "#FFD173" },
                        { ThemePreset.Operator, "#F29E74" }, { ThemePreset.Selection, "#33415E" },
                        { ThemePreset.LineNumber, "#707A8C" }, { ThemePreset.Error, "#FF6666" },
                        { ThemePreset.Warning, "#FFD173" },
                    }
                },
                new ThemePreset
                {
                    Name = "Material Palenight",
                    Origin = "material-theme",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#292D3E" }, { ThemePreset.Foreground, "#A6ACCD" },
                        { ThemePreset.Comment, "#676E95" }, { ThemePreset.Keyword, "#C792EA" },
                        { ThemePreset.String, "#C3E88D" }, { ThemePreset.Number, "#F78C6C" },
                        { ThemePreset.Type, "#FFCB6B" }, { ThemePreset.Method, "#82AAFF" },
                        { ThemePreset.Property, "#F07178" }, { ThemePreset.Field, "#F07178" },
                        { ThemePreset.Operator, "#89DDFF" }, { ThemePreset.Selection, "#3E4451" },
                        { ThemePreset.LineNumber, "#4B526D" }, { ThemePreset.Error, "#FF5370" },
                        { ThemePreset.Warning, "#FFCB6B" },
                    }
                },
                new ThemePreset
                {
                    Name = "Material Darker",
                    Origin = "material-theme",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#212121" }, { ThemePreset.Foreground, "#EEFFFF" },
                        { ThemePreset.Comment, "#616161" }, { ThemePreset.Keyword, "#C792EA" },
                        { ThemePreset.String, "#C3E88D" }, { ThemePreset.Number, "#F78C6C" },
                        { ThemePreset.Type, "#FFCB6B" }, { ThemePreset.Method, "#82AAFF" },
                        { ThemePreset.Property, "#F07178" }, { ThemePreset.Field, "#F07178" },
                        { ThemePreset.Operator, "#89DDFF" }, { ThemePreset.Selection, "#424242" },
                        { ThemePreset.LineNumber, "#616161" }, { ThemePreset.Error, "#FF5370" },
                        { ThemePreset.Warning, "#FFCB6B" },
                    }
                },
                new ThemePreset
                {
                    Name = "SynthWave '84",
                    Origin = "robb0wen/synthwave-vscode",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#262335" }, { ThemePreset.Foreground, "#FFFFFF" },
                        { ThemePreset.Comment, "#848BBD" }, { ThemePreset.Keyword, "#FEDE5D" },
                        { ThemePreset.Control, "#FF7EDB" }, { ThemePreset.String, "#FF8B39" },
                        { ThemePreset.Number, "#F97E72" }, { ThemePreset.Type, "#FE4450" },
                        { ThemePreset.Method, "#36F9F6" }, { ThemePreset.Property, "#FF7EDB" },
                        { ThemePreset.Field, "#FF7EDB" }, { ThemePreset.Operator, "#FEDE5D" },
                        { ThemePreset.Selection, "#463465" }, { ThemePreset.LineNumber, "#495495" },
                        { ThemePreset.Error, "#FE4450" }, { ThemePreset.Warning, "#FEDE5D" },
                    }
                },
                new ThemePreset
                {
                    Name = "Cobalt2",
                    Origin = "Wes Bos",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#193549" }, { ThemePreset.Foreground, "#FFFFFF" },
                        { ThemePreset.Comment, "#0088FF" }, { ThemePreset.Keyword, "#FF9D00" },
                        { ThemePreset.String, "#3AD900" }, { ThemePreset.Number, "#FF628C" },
                        { ThemePreset.Type, "#80FFBB" }, { ThemePreset.Method, "#FFC600" },
                        { ThemePreset.Property, "#9EFFFF" }, { ThemePreset.Field, "#9EFFFF" },
                        { ThemePreset.Operator, "#FF9D00" }, { ThemePreset.Selection, "#0050A4" },
                        { ThemePreset.LineNumber, "#35577B" }, { ThemePreset.Error, "#FF628C" },
                        { ThemePreset.Warning, "#FFC600" },
                    }
                },
                new ThemePreset
                {
                    Name = "Shades of Purple",
                    Origin = "ahmadawais",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#2D2B55" }, { ThemePreset.Foreground, "#FFFFFF" },
                        { ThemePreset.Comment, "#B362FF" }, { ThemePreset.Keyword, "#FF9D00" },
                        { ThemePreset.String, "#A5FF90" }, { ThemePreset.Number, "#FF628C" },
                        { ThemePreset.Type, "#FB94FF" }, { ThemePreset.Method, "#FAD000" },
                        { ThemePreset.Property, "#9EFFFF" }, { ThemePreset.Field, "#9EFFFF" },
                        { ThemePreset.Operator, "#FF9D00" }, { ThemePreset.Selection, "#463C77" },
                        { ThemePreset.LineNumber, "#5E5A8A" }, { ThemePreset.Error, "#EC3A37" },
                        { ThemePreset.Warning, "#FAD000" },
                    }
                },
                new ThemePreset
                {
                    Name = "Andromeda",
                    Origin = "EliverLara/Andromeda",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#23262E" }, { ThemePreset.Foreground, "#D5CED9" },
                        { ThemePreset.Comment, "#A0A1A7" }, { ThemePreset.Keyword, "#C74DED" },
                        { ThemePreset.String, "#96E072" }, { ThemePreset.Number, "#F39C12" },
                        { ThemePreset.Type, "#FFE66D" }, { ThemePreset.Method, "#00E8C6" },
                        { ThemePreset.Operator, "#C74DED" }, { ThemePreset.Selection, "#3D4352" },
                        { ThemePreset.LineNumber, "#666B75" }, { ThemePreset.Error, "#EE5D43" },
                        { ThemePreset.Warning, "#FFE66D" },
                    }
                },
                new ThemePreset
                {
                    Name = "Panda",
                    Origin = "siamak/vscode-panda",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#292A2B" }, { ThemePreset.Foreground, "#E6E6E6" },
                        { ThemePreset.Comment, "#676B79" }, { ThemePreset.Keyword, "#FF75B5" },
                        { ThemePreset.String, "#19F9D8" }, { ThemePreset.Number, "#FFB86C" },
                        { ThemePreset.Type, "#FFCC95" }, { ThemePreset.Method, "#6FC1FF" },
                        { ThemePreset.Operator, "#FF75B5" }, { ThemePreset.Selection, "#403F3F" },
                        { ThemePreset.LineNumber, "#676B79" }, { ThemePreset.Error, "#FF2C6D" },
                        { ThemePreset.Warning, "#FFB86C" },
                    }
                },
                new ThemePreset
                {
                    Name = "Rosé Pine",
                    Origin = "rose-pine.github.io",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#191724" }, { ThemePreset.Foreground, "#E0DEF4" },
                        { ThemePreset.Comment, "#6E6A86" }, { ThemePreset.Keyword, "#31748F" },
                        { ThemePreset.String, "#F6C177" }, { ThemePreset.Number, "#EBBCBA" },
                        { ThemePreset.Type, "#9CCFD8" }, { ThemePreset.Method, "#EBBCBA" },
                        { ThemePreset.Property, "#C4A7E7" }, { ThemePreset.Field, "#C4A7E7" },
                        { ThemePreset.Operator, "#908CAA" }, { ThemePreset.Selection, "#26233A" },
                        { ThemePreset.LineNumber, "#6E6A86" }, { ThemePreset.Error, "#EB6F92" },
                        { ThemePreset.Warning, "#F6C177" },
                    }
                },
                new ThemePreset
                {
                    Name = "Rosé Pine Dawn",
                    Origin = "rose-pine.github.io",
                    IsDark = false,
                    Roles =
                    {
                        { ThemePreset.Background, "#FAF4ED" }, { ThemePreset.Foreground, "#575279" },
                        { ThemePreset.Comment, "#9893A5" }, { ThemePreset.Keyword, "#286983" },
                        { ThemePreset.String, "#EA9D34" }, { ThemePreset.Number, "#D7827E" },
                        { ThemePreset.Type, "#56949F" }, { ThemePreset.Method, "#D7827E" },
                        { ThemePreset.Property, "#907AA9" }, { ThemePreset.Field, "#907AA9" },
                        { ThemePreset.Operator, "#797593" }, { ThemePreset.Selection, "#F2E9E1" },
                        { ThemePreset.LineNumber, "#9893A5" }, { ThemePreset.Error, "#B4637A" },
                        { ThemePreset.Warning, "#EA9D34" },
                    }
                },
                new ThemePreset
                {
                    Name = "Everforest Dark",
                    Origin = "sainnhe/everforest",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#2D353B" }, { ThemePreset.Foreground, "#D3C6AA" },
                        { ThemePreset.Comment, "#859289" }, { ThemePreset.Keyword, "#E67E80" },
                        { ThemePreset.String, "#A7C080" }, { ThemePreset.Number, "#D699B6" },
                        { ThemePreset.Type, "#DBBC7F" }, { ThemePreset.Method, "#A7C080" },
                        { ThemePreset.Property, "#83C092" }, { ThemePreset.Field, "#83C092" },
                        { ThemePreset.Operator, "#E69875" }, { ThemePreset.Selection, "#425047" },
                        { ThemePreset.LineNumber, "#7A8478" }, { ThemePreset.Error, "#E67E80" },
                        { ThemePreset.Warning, "#DBBC7F" },
                    }
                },
                new ThemePreset
                {
                    Name = "Kanagawa",
                    Origin = "rebelot/kanagawa",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#1F1F28" }, { ThemePreset.Foreground, "#DCD7BA" },
                        { ThemePreset.Comment, "#727169" }, { ThemePreset.Keyword, "#957FB8" },
                        { ThemePreset.String, "#98BB6C" }, { ThemePreset.Number, "#D27E99" },
                        { ThemePreset.Type, "#7AA89F" }, { ThemePreset.Method, "#7E9CD8" },
                        { ThemePreset.Property, "#E6C384" }, { ThemePreset.Field, "#E6C384" },
                        { ThemePreset.Operator, "#C0A36E" }, { ThemePreset.Selection, "#2D4F67" },
                        { ThemePreset.LineNumber, "#54546D" }, { ThemePreset.Error, "#E82424" },
                        { ThemePreset.Warning, "#FF9E3B" },
                    }
                },
                new ThemePreset
                {
                    Name = "Oceanic Next",
                    Origin = "voronianski",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#1B2B34" }, { ThemePreset.Foreground, "#CDD3DE" },
                        { ThemePreset.Comment, "#65737E" }, { ThemePreset.Keyword, "#C594C5" },
                        { ThemePreset.String, "#99C794" }, { ThemePreset.Number, "#F99157" },
                        { ThemePreset.Type, "#FAC863" }, { ThemePreset.Method, "#6699CC" },
                        { ThemePreset.Property, "#5FB3B3" }, { ThemePreset.Field, "#5FB3B3" },
                        { ThemePreset.Operator, "#C594C5" }, { ThemePreset.Selection, "#4F5B66" },
                        { ThemePreset.LineNumber, "#65737E" }, { ThemePreset.Error, "#EC5F67" },
                        { ThemePreset.Warning, "#FAC863" },
                    }
                },
                new ThemePreset
                {
                    Name = "Winter is Coming",
                    Origin = "johnpapa — dark blue",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#011627" }, { ThemePreset.Foreground, "#D6DEEB" },
                        { ThemePreset.Comment, "#5E7887" }, { ThemePreset.Keyword, "#C792EA" },
                        { ThemePreset.String, "#ADDB67" }, { ThemePreset.Number, "#F78C6C" },
                        { ThemePreset.Type, "#FFCB8B" }, { ThemePreset.Method, "#82AAFF" },
                        { ThemePreset.Property, "#7FDBCA" }, { ThemePreset.Field, "#7FDBCA" },
                        { ThemePreset.Operator, "#7FDBCA" }, { ThemePreset.Selection, "#0E3A53" },
                        { ThemePreset.LineNumber, "#4B6479" }, { ThemePreset.Error, "#EF5350" },
                        { ThemePreset.Warning, "#FFCB8B" },
                    }
                },
                new ThemePreset
                {
                    Name = "Zenburn",
                    Origin = "jnurmine/Zenburn",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#3F3F3F" }, { ThemePreset.Foreground, "#DCDCCC" },
                        { ThemePreset.Comment, "#7F9F7F" }, { ThemePreset.Keyword, "#F0DFAF" },
                        { ThemePreset.String, "#CC9393" }, { ThemePreset.Number, "#8CD0D3" },
                        { ThemePreset.Type, "#DFDFBF" }, { ThemePreset.Method, "#93E0E3" },
                        { ThemePreset.Operator, "#F0EFD0" }, { ThemePreset.Selection, "#5F5F5F" },
                        { ThemePreset.LineNumber, "#9FAFAF" }, { ThemePreset.Error, "#E37170" },
                        { ThemePreset.Warning, "#F0DFAF" },
                    }
                },
                new ThemePreset
                {
                    Name = "Tomorrow Night Blue",
                    Origin = "chriskempson/tomorrow-theme",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#002451" }, { ThemePreset.Foreground, "#FFFFFF" },
                        { ThemePreset.Comment, "#7285B7" }, { ThemePreset.Keyword, "#EBBBFF" },
                        { ThemePreset.String, "#D1F1A9" }, { ThemePreset.Number, "#FFC58F" },
                        { ThemePreset.Type, "#FFEEAD" }, { ThemePreset.Method, "#BBDAFF" },
                        { ThemePreset.Operator, "#99FFFF" }, { ThemePreset.Selection, "#003F8E" },
                        { ThemePreset.LineNumber, "#7285B7" }, { ThemePreset.Error, "#FF9DA4" },
                        { ThemePreset.Warning, "#FFC58F" },
                    }
                },
                new ThemePreset
                {
                    Name = "Gruvbox Light",
                    Origin = "morhetz/gruvbox",
                    IsDark = false,
                    Roles =
                    {
                        { ThemePreset.Background, "#FBF1C7" }, { ThemePreset.Foreground, "#3C3836" },
                        { ThemePreset.Comment, "#928374" }, { ThemePreset.Keyword, "#9D0006" },
                        { ThemePreset.String, "#79740E" }, { ThemePreset.Number, "#8F3F71" },
                        { ThemePreset.Type, "#B57614" }, { ThemePreset.Method, "#79740E" },
                        { ThemePreset.Property, "#076678" }, { ThemePreset.Field, "#076678" },
                        { ThemePreset.Operator, "#427B58" }, { ThemePreset.Selection, "#EBDBB2" },
                        { ThemePreset.LineNumber, "#A89984" }, { ThemePreset.Error, "#9D0006" },
                        { ThemePreset.Warning, "#B57614" },
                    }
                },
                new ThemePreset
                {
                    Name = "One Light",
                    Origin = "Atom / One Light",
                    IsDark = false,
                    Roles =
                    {
                        { ThemePreset.Background, "#FAFAFA" }, { ThemePreset.Foreground, "#383A42" },
                        { ThemePreset.Comment, "#A0A1A7" }, { ThemePreset.Keyword, "#A626A4" },
                        { ThemePreset.String, "#50A14F" }, { ThemePreset.Number, "#986801" },
                        { ThemePreset.Type, "#C18401" }, { ThemePreset.Method, "#4078F2" },
                        { ThemePreset.Property, "#E45649" }, { ThemePreset.Field, "#E45649" },
                        { ThemePreset.Operator, "#0184BC" }, { ThemePreset.Selection, "#E5E5E6" },
                        { ThemePreset.LineNumber, "#9D9D9F" }, { ThemePreset.Error, "#E45649" },
                        { ThemePreset.Warning, "#C18401" },
                    }
                },
                new ThemePreset
                {
                    Name = "Catppuccin Latte",
                    Origin = "catppuccin.com",
                    IsDark = false,
                    Roles =
                    {
                        { ThemePreset.Background, "#EFF1F5" }, { ThemePreset.Foreground, "#4C4F69" },
                        { ThemePreset.Comment, "#9CA0B0" }, { ThemePreset.Keyword, "#8839EF" },
                        { ThemePreset.String, "#40A02B" }, { ThemePreset.Number, "#FE640B" },
                        { ThemePreset.Type, "#DF8E1D" }, { ThemePreset.Method, "#1E66F5" },
                        { ThemePreset.Property, "#7287FD" }, { ThemePreset.Field, "#5C5F77" },
                        { ThemePreset.Parameter, "#E64553" }, { ThemePreset.Operator, "#04A5E5" },
                        { ThemePreset.Selection, "#CCD0DA" }, { ThemePreset.LineNumber, "#9CA0B0" },
                        { ThemePreset.Error, "#D20F39" }, { ThemePreset.Warning, "#DF8E1D" },
                    }
                },
                new ThemePreset
                {
                    Name = "Tokyo Night Storm",
                    Origin = "enkia/tokyo-night",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#24283B" }, { ThemePreset.Foreground, "#A9B1D6" },
                        { ThemePreset.Comment, "#565F89" }, { ThemePreset.Keyword, "#BB9AF7" },
                        { ThemePreset.String, "#9ECE6A" }, { ThemePreset.Number, "#FF9E64" },
                        { ThemePreset.Type, "#2AC3DE" }, { ThemePreset.Method, "#7AA2F7" },
                        { ThemePreset.Property, "#7DCFFF" }, { ThemePreset.Field, "#7DCFFF" },
                        { ThemePreset.Parameter, "#E0AF68" }, { ThemePreset.Operator, "#89DDFF" },
                        { ThemePreset.Selection, "#364A82" }, { ThemePreset.LineNumber, "#3B4261" },
                        { ThemePreset.Error, "#F7768E" }, { ThemePreset.Warning, "#E0AF68" },
                    }
                },
                new ThemePreset
                {
                    Name = "Monokai Pro",
                    Origin = "monokai.pro",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#2D2A2E" }, { ThemePreset.Foreground, "#FCFCFA" },
                        { ThemePreset.Comment, "#727072" }, { ThemePreset.Keyword, "#FF6188" },
                        { ThemePreset.String, "#FFD866" }, { ThemePreset.Number, "#AB9DF2" },
                        { ThemePreset.Type, "#78DCE8" }, { ThemePreset.Method, "#A9DC76" },
                        { ThemePreset.Parameter, "#FC9867" }, { ThemePreset.Operator, "#FF6188" },
                        { ThemePreset.Selection, "#403E41" }, { ThemePreset.LineNumber, "#5B595C" },
                        { ThemePreset.Error, "#FF6188" }, { ThemePreset.Warning, "#FFD866" },
                    }
                },
                new ThemePreset
                {
                    Name = "Horizon",
                    Origin = "jolaleye/horizon-theme-vscode",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#1C1E26" }, { ThemePreset.Foreground, "#D5D8DA" },
                        { ThemePreset.Comment, "#6C6F93" }, { ThemePreset.Keyword, "#B877DB" },
                        { ThemePreset.String, "#FAB795" }, { ThemePreset.Number, "#F09383" },
                        { ThemePreset.Type, "#FAC29A" }, { ThemePreset.Method, "#25B0BC" },
                        { ThemePreset.Property, "#E95678" }, { ThemePreset.Field, "#E95678" },
                        { ThemePreset.Operator, "#B877DB" }, { ThemePreset.Selection, "#2E303E" },
                        { ThemePreset.LineNumber, "#6C6F93" }, { ThemePreset.Error, "#E95678" },
                        { ThemePreset.Warning, "#FAC29A" },
                    }
                },
                new ThemePreset
                {
                    Name = "Nightfox",
                    Origin = "EdenEast/nightfox.nvim",
                    IsDark = true,
                    Roles =
                    {
                        { ThemePreset.Background, "#192330" }, { ThemePreset.Foreground, "#CDCECF" },
                        { ThemePreset.Comment, "#738091" }, { ThemePreset.Keyword, "#9D79D6" },
                        { ThemePreset.String, "#8EBAA4" }, { ThemePreset.Number, "#F4A261" },
                        { ThemePreset.Type, "#DBC074" }, { ThemePreset.Method, "#719CD6" },
                        { ThemePreset.Property, "#63CDCF" }, { ThemePreset.Field, "#63CDCF" },
                        { ThemePreset.Operator, "#719CD6" }, { ThemePreset.Selection, "#2B3B51" },
                        { ThemePreset.LineNumber, "#575860" }, { ThemePreset.Error, "#C94F6D" },
                        { ThemePreset.Warning, "#DBC074" },
                    }
                },
                new ThemePreset
                {
                    Name = "Nord Light",
                    Origin = "Nord — Snow Storm base",
                    IsDark = false,
                    Roles =
                    {
                        { ThemePreset.Background, "#ECEFF4" }, { ThemePreset.Foreground, "#2E3440" },
                        { ThemePreset.Comment, "#7B88A1" }, { ThemePreset.Keyword, "#5E81AC" },
                        { ThemePreset.String, "#617D48" }, { ThemePreset.Number, "#8A5F82" },
                        { ThemePreset.Type, "#3B7C7F" }, { ThemePreset.Method, "#4A7391" },
                        { ThemePreset.Operator, "#5E81AC" }, { ThemePreset.Selection, "#D8DEE9" },
                        { ThemePreset.LineNumber, "#9BA5B5" }, { ThemePreset.Error, "#BF616A" },
                        { ThemePreset.Warning, "#B48D3B" },
                    }
                },
            };
        }
    }
}
