using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Shell;

namespace XoCrazy.Core
{
    /// <summary>
    /// Everything that paints a background in the editor window: the text area, the gutter, the
    /// breakpoint bar, the outlining strip, the overview margin down the right-hand side.
    ///
    /// Setting <c>Plain Text</c>'s background only repaints the text area, because each margin
    /// is its own format definition with its own background and none of them inherit from the
    /// view. That is why a preset background used to leave a differently-coloured band around
    /// the code instead of a themed editor.
    ///
    /// The list is discovered, not hardcoded. The margin definitions a given Visual Studio
    /// exposes depend on which extensions are loaded, so the names come from the MEF
    /// composition and are then filtered to the ones the format map will actually accept a
    /// write for. A hand-written table would be wrong on the first machine with a different
    /// extension set.
    /// </summary>
    internal static class SurfaceCatalog
    {
        /// <summary>
        /// Definitions that carry an editor background but are not named "...Margin".
        /// Kept explicit because pattern-matching cannot infer them.
        /// </summary>
        private static readonly string[] KnownSurfaces =
        {
            // The one that actually owns the dark area behind the code. Setting "Plain Text"
            // alone paints the *text runs* — which is what produced light bands behind the code
            // and left the view itself untouched. The view reads its background brush from
            // this key.
            "TextView Background",
            "Plain Text",
            "Indicator Margin",         // the breakpoint bar
            "Line Number",
            "Visible Whitespace",
            "outlining.verticalrule",
            "outlining.collapsehintadornment",

            // The box a collapsed region is actually drawn as on VS 2026. The composition walk
            // reports it as 'outlining.chevron.collapsed' -> "Collapsed Text Indicator
            // (Collapsed)", while 'Collapsible Text (Collapsed)' appears in no NameMap build at
            // all — it survives only as a 'Collapsible Text (Collapsed) {LegacyMarker}'
            // classification that paints nothing and reads black in a dump. Writing only the
            // legacy name is why the region name kept the old theme's colour while everything
            // around it was repainted, and why restarting left it black with nothing able to
            // move it: no code path in this extension had ever named the item that paints.
            // Its name contains no "margin", so the discovery filter below never finds it either.
            "outlining.chevron.collapsed",
            "outlining.chevron.expanded",
            "Collapsible Text (Collapsed)",
        };

        /// <summary>
        /// Definitions that must keep their own background even when painting the whole
        /// editor: a selection or a highlight that takes the editor background stops being
        /// visible at all.
        /// </summary>
        private static readonly HashSet<string> NeverPaint =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Selected Text",
                "Inactive Selected Text",
                "brace matching",
                "Current Line",
                "Current Line (Inactive)",
                "MarkerFormatDefinition/HighlightedReference",
                "MarkerFormatDefinition/HighlightedWrittenReference",
                "MarkerFormatDefinition/HighlightedDefinition",
                "Syntax Error",
                "Compiler Error",
                "Other Error",
                "Warning",
            };

        private static List<string> _cached;

        /// <summary>
        /// The surfaces to paint, in no particular order. Cached: the discovery walk
        /// constructs every format definition in the composition.
        /// </summary>
        public static IReadOnlyList<string> Discover(IServiceProvider services, EditorFormatBridge bridge)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cached != null)
                return _cached;

            // The composition's list is used to *discover* margins, not to veto the built-ins.
            //
            // FontColorNameMap only records a definition when it exposes a non-empty
            // DisplayName, and the core editor surfaces do not: "Plain Text",
            // "TextView Background", "Indicator Margin" and "Collapsible Text (Collapsed)"
            // have never appeared in a single NameMap build in the trace, while the very same
            // session logs "bridge 'Indicator Margin' -> format map OK". Having a display name
            // and being a format-map key are different questions, and gating on the first one
            // silently dropped the breakpoint bar and the text-area background from every
            // whole-editor paint — which is the light band left standing beside the code.
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var name in FontColorNameMap.AllFormatNames(services))
                    existing.Add(name);
            }
            catch (Exception ex)
            {
                Diag.Log("SurfaceCatalog: composition walk failed: " + ex.Message);
            }

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Unconditional. These are core-editor definitions; a name the running VS really
            // does not have costs one "KEY NOT IN MAP" line in the trace and nothing else,
            // whereas dropping one costs a repainted editor with an unpainted band in it.
            foreach (var name in KnownSurfaces)
            {
                candidates.Add(name);
                if (!existing.Contains(name))
                    Diag.Log("SurfaceCatalog: '" + name + "' has no Fonts and Colors display name; "
                             + "kept anyway — the editor format map is what paints it.");
            }

            foreach (var name in existing)
            {
                // Margins are the surfaces that surround the text and the ones that give the
                // mismatched band away. Everything else with a background is a marker or a
                // highlight, and painting those flat would erase them.
                if (name.IndexOf("margin", StringComparison.OrdinalIgnoreCase) >= 0)
                    candidates.Add(name);
            }

            candidates.ExceptWith(NeverPaint);

            _cached = candidates.ToList();
            Diag.Log("SurfaceCatalog: " + _cached.Count + " paintable surface(s): "
                     + string.Join(", ", _cached.OrderBy(n => n).ToArray()));
            return _cached;
        }
    }
}
