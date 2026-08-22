using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace ThemeForge.UI
{
    /// <summary>
    /// The tool window host. Non-modal by construction, which is what makes "see it while you
    /// pick it" possible at all — the built-in Fonts &amp; Colors page is a modal options page,
    /// so it physically cannot show you the editor while you are choosing.
    /// </summary>
    [Guid(PackageGuids.ToolWindowString)]
    public sealed class ThemeForgeToolWindow : ToolWindowPane
    {
        private readonly ThemeForgeControl _control;

        public ThemeForgeToolWindow() : base(null)
        {
            Caption = "XoCrazy";

            // A XAML parse failure here reaches the user as "Exception has been thrown by the
            // target of an invocation" and nothing else — the shell swallows the inner
            // exception that actually names the offending line. Logging it is the difference
            // between a five-minute fix and a bisect.
            try
            {
                _control = new ThemeForgeControl();
                Content = _control;
            }
            catch (Exception ex)
            {
                Core.Diag.Log("Tool window construction FAILED: " + ex);
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    Core.Diag.Log("  inner: " + inner.GetType().Name + ": " + inner.Message);
                throw;
            }
        }

        protected override void Initialize()
        {
            base.Initialize();
            ThreadHelper.ThrowIfNotOnUIThread();
            _control.Initialize(ServiceProvider.GlobalProvider);
        }

        /// <summary>
        /// The pane's lifetime is the session's lifetime. This is the only correct teardown
        /// point — the control's WPF Unloaded fires on every re-dock and tab switch.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && _control != null)
                _control.Shutdown();
            base.Dispose(disposing);
        }

        /// <summary>Points the window at one item, used by the caret-targeting command.</summary>
        public bool Target(string storageName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _control.SelectByStorageName(storageName);
        }
    }
}
