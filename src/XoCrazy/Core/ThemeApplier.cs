using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;

namespace XoCrazy.Core
{
    /// <summary>
    /// Puts the saved theme back after a restart, and keeps it on screen afterwards.
    ///
    /// Three things reset the editor's format maps, and each one needs a re-apply:
    ///
    ///   * <b>Restart.</b> The maps are built from Fonts and Colors at process start. Anything
    ///     XoCrazy pushed last session is gone.
    ///   * <b>Late MEF load.</b> At package init the Roslyn classification types
    ///     (<c>class name</c>, <c>method name</c>, …) do not exist yet — they register when the
    ///     first C# file opens. Applying once at startup silently misses most of the theme,
    ///     which looks exactly like the theme not being saved. Hence the retry, driven by
    ///     document creation rather than a fixed delay.
    ///   * <b>VS theme switch.</b> The shell rebuilds every format map from the new theme's
    ///     defaults and drops all overrides. That is the shell's behaviour, not something that
    ///     can be prevented — the answer is to notice and re-assert.
    /// </summary>
    internal sealed class ThemeApplier : IDisposable
    {
        private static ThemeApplier _instance;

        private readonly IServiceProvider _services;
        private readonly DispatcherTimer _retry;
        private ITextDocumentFactoryService _documents;
        private int _attempts;
        private bool _disposed;

        private ThemeApplier(IServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _services = services;

            // Background priority: re-applying a theme must never contend with typing.
            _retry = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _retry.Tick += (s, e) => Apply("retry");
        }

        /// <summary>Starts the one applier for this VS session. Safe to call twice.</summary>
        public static void Start(IServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_instance != null)
                return;

            _instance = new ThemeApplier(services);
            _instance.Hook();
            _instance.Apply("startup");
        }

        /// <summary>
        /// Called after the tool window writes new values, so a later re-assert (theme switch,
        /// new document) pushes the current theme and not the one loaded at startup.
        /// </summary>
        public static void Reassert(string reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_instance != null)
                _instance.Apply(reason);
        }

        private void Hook()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var components = _services.GetService(typeof(SComponentModel)) as IComponentModel;
                if (components != null)
                {
                    _documents = components.GetService<ITextDocumentFactoryService>();
                    if (_documents != null)
                        _documents.TextDocumentCreated += OnTextDocumentCreated;
                }
            }
            catch (Exception ex)
            {
                Diag.Log("Applier: could not hook document creation: " + ex.Message);
            }

            try
            {
                VSColorTheme.ThemeChanged += OnThemeChanged;
            }
            catch (Exception ex)
            {
                Diag.Log("Applier: could not hook theme change: " + ex.Message);
            }
        }

        private void OnTextDocumentCreated(object sender, TextDocumentEventArgs e)
        {
            // The first document is what drags the language's classification types into the
            // composition, so this is the moment the missing half of the theme becomes
            // applicable. Queued rather than run inline: the document is still being created,
            // and this event does not promise the UI thread.
            ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Apply("document opened");
            }).FileAndForget("xocrazy/apply-on-document");
        }

        private void OnThemeChanged(ThemeChangedEventArgs e)
        {
            // The shell repopulates the format maps asynchronously after raising this. Applying
            // immediately would be overwritten by the theme that just loaded.
            _attempts = 0;
            _retry.Start();
            Diag.Log("Applier: VS theme changed; re-asserting saved colours.");
        }

        private void Apply(string reason)
        {
            if (_disposed)
                return;

            ThreadHelper.ThrowIfNotOnUIThread();

            if (ThemeStore.Count == 0)
            {
                _retry.Stop();
                return;
            }

            var bridge = EditorFormatBridge.Create(_services);
            if (bridge == null)
            {
                Diag.Log("Applier(" + reason + "): no editor format bridge yet.");
                ScheduleRetry();
                return;
            }

            // The surface set decides which map each stored item goes back to; without it a
            // restored theme would repaint the syntax and leave the editor background behind.
            var surfaces = new HashSet<string>(SurfaceCatalog.Discover(_services, bridge),
                                               StringComparer.OrdinalIgnoreCase);

            var batch = ThemeStore.All().Select(record =>
            {
                var model = ToViewModel(record);
                model.IsSurface = surfaces.Contains(model.StorageName);
                return model;
            }).ToList();
            int failed = bridge.Apply(batch);

            Diag.Log("Applier(" + reason + "): " + (batch.Count - failed) + "/" + batch.Count
                     + " item(s) restored.");

            if (failed > 0)
                ScheduleRetry();
            else
                _retry.Stop();
        }

        /// <summary>
        /// Keeps trying while items are still unresolvable, then gives up. Items that never
        /// resolve are ones no loaded language contributes — retrying forever would burn a
        /// timer tick every three seconds for the life of the process.
        /// </summary>
        private void ScheduleRetry()
        {
            if (_attempts++ >= 20)
            {
                _retry.Stop();
                Diag.Log("Applier: giving up on the remaining items for this session.");
                return;
            }
            _retry.Start();
        }

        private static ItemViewModel ToViewModel(Snapshot.Record record)
        {
            var known = ClassificationCatalog.Find(record.Item);
            var model = new ItemViewModel(
                record.Category,
                record.Item,
                known != null ? known.DisplayName : record.Item,
                known != null ? known.Group : "Saved",
                known != null ? known.Hint : record.Item,
                () => 0u);
            model.SetColors(Snapshot.ToColors(record, null));
            return model;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _retry.Stop();

            if (_documents != null)
                _documents.TextDocumentCreated -= OnTextDocumentCreated;
            try { VSColorTheme.ThemeChanged -= OnThemeChanged; } catch { }
        }

        public static void Shutdown()
        {
            if (_instance == null) return;
            _instance.Dispose();
            _instance = null;
        }
    }
}
