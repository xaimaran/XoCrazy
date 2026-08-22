using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace XoCrazy.Core
{
    /// <summary>
    /// Coalesces writes so dragging a colour picker does not melt the editor.
    ///
    /// <c>RefreshCache</c> makes the editor re-classify and repaint every visible view. Firing
    /// that per mouse-move sample locks the UI thread. A short trailing debounce is enough to
    /// still feel instantaneous while collapsing a drag into a handful of applies.
    /// </summary>
    internal sealed class LiveApplyQueue : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, ItemViewModel> _pending =
            new Dictionary<string, ItemViewModel>(StringComparer.Ordinal);
        private readonly Action<IReadOnlyCollection<ItemViewModel>> _flush;

        public LiveApplyQueue(Action<IReadOnlyCollection<ItemViewModel>> flush, int debounceMs = 50)
        {
            _flush = flush;
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(debounceMs)
            };
            _timer.Tick += OnTick;
        }

        public void Queue(ItemViewModel item)
        {
            _pending[item.Category.ToString("N") + "|" + item.StorageName] = item;
            _timer.Stop();   // restart: trailing edge, so a drag applies once it settles
            _timer.Start();
        }

        /// <summary>Applies anything queued right now — used on mouse-up and on close.</summary>
        public void FlushNow()
        {
            _timer.Stop();
            Drain();
        }

        private void OnTick(object sender, EventArgs e)
        {
            _timer.Stop();
            Drain();
        }

        private void Drain()
        {
            if (_pending.Count == 0) return;
            var batch = new List<ItemViewModel>(_pending.Values);
            _pending.Clear();
            _flush(batch);
        }

        public void Dispose()
        {
            _timer.Tick -= OnTick;
            _timer.Stop();
        }
    }
}
