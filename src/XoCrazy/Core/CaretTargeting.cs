using System;
using System.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.TextManager.Interop;

namespace XoCrazy.Core
{
    /// <summary>
    /// Answers "what is the thing under my caret called?".
    ///
    /// This is the feature that replaces scrolling. Instead of guessing which of six keyword
    /// variants paints <c>if</c>, put the caret on it and ask the classifier that actually
    /// coloured it.
    /// </summary>
    internal static class CaretTargeting
    {
        /// <summary>
        /// Returns the storage name of the most specific classification at the caret,
        /// or null when there is no active code window or the span is unclassified.
        /// </summary>
        public static string ClassificationAtCaret(IServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textManager = services.GetService(typeof(SVsTextManager)) as IVsTextManager;
            var components = services.GetService(typeof(SComponentModel)) as IComponentModel;
            if (textManager == null || components == null)
                return null;

            // fMustHaveFocus must be 0. This command is also on the editor context menu, and
            // while that menu is up the focus is on the menu, not the view — asking for a
            // focused view there always fails, so every right-click invocation reported
            // "no token under the caret" no matter where the caret was.
            IVsTextView activeView;
            if (ErrorHandler.Failed(textManager.GetActiveView(0, null, out activeView)) || activeView == null)
            {
                Diag.Log("CaretTargeting: no active view.");
                return null;
            }

            var adapters = components.GetService<IVsEditorAdaptersFactoryService>();
            var wpfView = adapters != null ? adapters.GetWpfTextView(activeView) : null;
            if (wpfView == null)
                return null;

            var caret = wpfView.Caret.Position.BufferPosition;
            var snapshot = caret.Snapshot;

            // Look at the character the caret sits on; at end of line, look left instead.
            int start = caret.Position;
            if (start >= snapshot.Length || (start > 0 && IsLineBreakAt(snapshot, start)))
                start = Math.Max(0, start - 1);
            if (start >= snapshot.Length)
                return null;

            var aggregator = components.GetService<IClassifierAggregatorService>();
            if (aggregator == null)
                return null;

            var classifier = aggregator.GetClassifier(wpfView.TextBuffer);
            var span = new SnapshotSpan(snapshot, start, 1);
            var spans = classifier.GetClassificationSpans(span);
            if (spans == null || spans.Count == 0)
                return null;

            // Multiple classifiers can overlap the same character (a keyword inside a
            // preprocessor directive, for instance). The narrowest span is the one that
            // actually won the paint.
            // Log every overlapping classification, not just the winner. When the painted
            // colour does not match the item XoCrazy is editing, the answer is almost
            // always that the name being edited is not the name that painted the token.
            foreach (var s in spans)
            {
                Diag.Log("CaretTargeting: candidate '" + s.ClassificationType.Classification
                         + "' len=" + s.Span.Length
                         + " bases=[" + string.Join(",", s.ClassificationType.BaseTypes.Select(b => b.Classification)) + "]");
            }

            var best = spans
                .OrderBy(s => s.Span.Length)
                .ThenByDescending(s => s.ClassificationType.BaseTypes.Count())
                .First();

            var storageName = ClassificationCatalog.ToStorageName(best.ClassificationType.Classification);

            // What the format map says this classification is painted with, right now. If this
            // does not match what is on screen, the view is not reading the map we are writing.
            var formatMaps = components.GetService<IClassificationFormatMapService>();
            if (formatMaps != null)
            {
                var map = formatMaps.GetClassificationFormatMap("text");
                var props = map.GetTextProperties(best.ClassificationType);
                var brush = props.ForegroundBrushEmpty ? null : props.ForegroundBrush as System.Windows.Media.SolidColorBrush;
                Diag.Log("CaretTargeting: '" + best.ClassificationType.Classification + "' -> '" + storageName
                         + "'; format map paints it "
                         + (brush != null ? ColorMath.ToHex(ColorMath.ToColorRef(brush.Color)) : "<inherited>"));
            }

            return storageName;
        }

        private static bool IsLineBreakAt(ITextSnapshot snapshot, int position)
        {
            if (position >= snapshot.Length) return true;
            char c = snapshot[position];
            return c == '\r' || c == '\n';
        }
    }
}
