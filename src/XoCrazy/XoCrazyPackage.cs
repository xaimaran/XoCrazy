using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using XoCrazy.Core;
using XoCrazy.UI;
using Task = System.Threading.Tasks.Task;

namespace XoCrazy
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.1.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(XoCrazyToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids.SolutionExplorer)]
    // Force the package to load at startup regardless of whether any command is invoked.
    // Without this the shell only loads on first command invocation — and if no command is
    // reachable, the package never runs and cannot report why.
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuids.PackageString)]
    public sealed class XoCrazyPackage : AsyncPackage
    {
        private static readonly string DiagnosticLog =
            Path.Combine(Path.GetTempPath(), "xocrazy-load.log");

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(DiagnosticLog,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never be the reason the package fails to load.
            }
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Log("InitializeAsync entered; assembly=" + typeof(XoCrazyPackage).Assembly.Location);
            try
            {
                await base.InitializeAsync(cancellationToken, progress);
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
                if (commandService == null)
                {
                    Log("FAILED: IMenuCommandService unavailable — no command can be registered.");
                    return;
                }

                commandService.AddCommand(new MenuCommand(
                    (s, e) => OpenToolWindow(),
                    new CommandID(PackageGuids.CommandSet, PackageGuids.CmdIdOpenToolWindow)));

                commandService.AddCommand(new MenuCommand(
                    (s, e) => OpenToolWindow(),
                    new CommandID(PackageGuids.CommandSet, PackageGuids.CmdIdOpenToolWindowTools)));

                commandService.AddCommand(new MenuCommand(
                    (s, e) => TargetCaret(),
                    new CommandID(PackageGuids.CommandSet, PackageGuids.CmdIdTargetCaret)));

                Log("OK: 3 commands registered on command set " + PackageGuids.CommandSet.ToString("B"));

                // Re-apply the saved theme. This is what makes edits survive a restart: the
                // editor's format maps are rebuilt from Fonts and Colors at process start, and
                // nothing the tool window pushed last session is in them.
                ThemeApplier.Start(this);
            }
            catch (Exception ex)
            {
                Log("EXCEPTION: " + ex);
                throw;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ThemeApplier.Shutdown();
            base.Dispose(disposing);
        }

        private XoCrazyToolWindow OpenToolWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var window = FindToolWindow(typeof(XoCrazyToolWindow), 0, create: true) as XoCrazyToolWindow;
            if (window == null || window.Frame == null)
                return null;

            var frame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
            return window;
        }

        /// <summary>
        /// Reads the classification under the caret and points the window at it.
        ///
        /// The order matters: resolve the classification from the code window *first*, because
        /// showing the tool window moves focus and the "active view" stops being the editor.
        /// </summary>
        private void TargetCaret()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var storageName = CaretTargeting.ClassificationAtCaret(ServiceProvider.GlobalProvider);

            var window = OpenToolWindow();
            if (window == null)
                return;

            // No classification under the caret is not an error worth a modal for. The command
            // is "open XoCrazy, on this colour if there is one" — the window is already up by
            // this point, which is the whole of what the user asked for. A dialog here made the
            // common case (caret on whitespace) feel like a failure and cost a click to dismiss.
            if (storageName == null)
            {
                Diag.Log("TargetCaret: no classification under the caret; opened the window only.");
                return;
            }

            window.Target(storageName);
        }
    }
}
