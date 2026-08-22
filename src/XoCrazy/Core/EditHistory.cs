using System;
using System.Collections.Generic;
using System.Linq;

namespace XoCrazy.Core
{
    /// <summary>
    /// One undoable step: what every touched item looked like before, and after.
    ///
    /// Stored as full colour states rather than deltas. A delta would have to describe
    /// "foreground changed, background handed back to the theme, bold cleared" as three
    /// separate facts, and inheritance is exactly where that goes wrong — an inherited channel
    /// has no colour to diff against.
    /// </summary>
    internal sealed class EditStep
    {
        public string Label;
        public Dictionary<string, ItemColors> Before = new Dictionary<string, ItemColors>(StringComparer.Ordinal);
        public Dictionary<string, ItemColors> After = new Dictionary<string, ItemColors>(StringComparer.Ordinal);

        /// <summary>True while further edits to the same items fold into this step.</summary>
        public bool Open;

        public bool SameItemsAs(Dictionary<string, ItemColors> other)
        {
            return other.Count == After.Count && other.Keys.All(After.ContainsKey);
        }
    }

    /// <summary>
    /// Undo/redo for colour edits.
    ///
    /// Revert and undo are not the same operation and conflating them is why backing out of one
    /// bad colour used to throw away the whole session: <b>Revert all</b> jumps to the state the
    /// window opened in, undo walks back one step at a time.
    ///
    /// Steps are grouped by interaction, not by apply. Dragging the picker fires an apply every
    /// 50 ms — recording each one would mean fifty presses of Ctrl+Z to get back across a single
    /// drag. The group stays open until the caller says the interaction ended (mouse-up, a
    /// checkbox click, a preset), so one gesture is one step.
    /// </summary>
    internal sealed class EditHistory
    {
        private readonly List<EditStep> _steps = new List<EditStep>();
        private int _depth;          // how many steps are currently applied
        private const int Limit = 200;

        public bool CanUndo { get { return _depth > 0; } }
        public bool CanRedo { get { return _depth < _steps.Count; } }

        public string UndoLabel { get { return CanUndo ? _steps[_depth - 1].Label : null; } }
        public string RedoLabel { get { return CanRedo ? _steps[_depth].Label : null; } }

        /// <summary>
        /// Records a change. Folds into the open step when it touches the same items, so a
        /// drag is one step; otherwise starts a new one and drops any redo tail.
        /// </summary>
        public void Push(string label, Dictionary<string, ItemColors> before, Dictionary<string, ItemColors> after)
        {
            if (after.Count == 0)
                return;

            var top = _depth > 0 ? _steps[_depth - 1] : null;
            if (top != null && top.Open && _depth == _steps.Count && top.SameItemsAs(after))
            {
                // Same gesture continuing: keep the original "before", take the newer "after".
                top.After = after;
                top.Label = label ?? top.Label;
                return;
            }

            // A new step invalidates anything that was undone — the timeline forked.
            if (_depth < _steps.Count)
                _steps.RemoveRange(_depth, _steps.Count - _depth);

            _steps.Add(new EditStep { Label = label, Before = before, After = after, Open = true });

            if (_steps.Count > Limit)
                _steps.RemoveAt(0);

            _depth = _steps.Count;
        }

        /// <summary>Ends the current gesture. The next <see cref="Push"/> starts a new step.</summary>
        public void CloseGroup()
        {
            if (_depth > 0)
                _steps[_depth - 1].Open = false;
        }

        /// <summary>The step to reverse, or null. Call <see cref="CloseGroup"/> semantics apply.</summary>
        public EditStep Undo()
        {
            if (!CanUndo) return null;
            CloseGroup();
            return _steps[--_depth];
        }

        public EditStep Redo()
        {
            if (!CanRedo) return null;
            var step = _steps[_depth++];
            step.Open = false;
            return step;
        }

        public void Clear()
        {
            _steps.Clear();
            _depth = 0;
        }
    }
}
