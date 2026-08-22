using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using ThemeForge.Core;

namespace ThemeForge.UI
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool && (bool)value) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public partial class ThemeForgeControl : UserControl
    {
        private ThemeForgeSession _session;
        private CollectionViewSource _view;
        private ItemViewModel _selected;
        private bool _suppressPickerFeedback;

        public ThemeForgeControl()
        {
            InitializeComponent();
            Picker.ColorChanged += OnPickerColorChanged;
            Picker.ColorCommitted += OnPickerColorCommitted;

            // Transparent and Inherit are the same operation on the same channel: both mean
            // "write no brush". Wiring them to one handler keeps them from drifting apart.
            Picker.TransparentRequested += (s, e) => Inherit_Click(s, new RoutedEventArgs());
        }

        /// <summary>
        /// Called once the package has a service provider. Everything here touches STA COM,
        /// so it must run on the main thread.
        /// </summary>
        public void Initialize(IServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _session = new ThemeForgeSession(services);
            if (!_session.IsReady)
            {
                StatusLabel.Text = "Could not reach the Fonts & Colors services. Restart Visual Studio and try again.";
                IsEnabled = false;
                return;
            }

            _session.Applied += (s, e) =>
            {
                UpdateDirtyLabel();
                UpdateHistoryButtons();
                if (!string.IsNullOrEmpty(_session.LastApplyError))
                    StatusLabel.Text = _session.LastApplyError;
            };
            _session.LoadCurated();
            BuildView();

            // The classification display items (keyword, comment, class name, …) are supplied
            // by the editor's font-and-colour defaults provider, which enumerates MEF
            // classification types. They do not exist in the category until an editor view has
            // been created. This package auto-loads at startup, so the first load runs before
            // any file is open and GetItem returns REGDB_E_KEYMISSING for all of them — the
            // list was permanently stuck with the two base editor items.
            IsVisibleChanged += OnVisibleChanged;
        }

        /// <summary>
        /// Rebuilds the list when the window is shown. Cheap — a GetItem per curated row — and
        /// it is the only point where "an editor exists now" is observable without hooking the
        /// running document table.
        /// </summary>
        private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_session == null || !IsVisible)
                return;

            ThreadHelper.ThrowIfNotOnUIThread();
            ReloadItems();
        }

        private void BuildView()
        {
            _view = new CollectionViewSource { Source = _session.Items };
            _view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            _view.View.Filter = o =>
            {
                var item = o as ItemViewModel;
                return item != null && item.MatchesFilter(SearchBox.Text);
            };
            ItemList.ItemsSource = _view.View;
            UpdateDirtyLabel();
        }

        /// <summary>
        /// Selects the row for a storage name. This is what the caret-targeting command calls,
        /// and it is the reason nobody has to scroll a 400-row list.
        /// </summary>
        public bool SelectByStorageName(string storageName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_session == null || string.IsNullOrEmpty(storageName))
                return false;

            var match = _session.Items.FirstOrDefault(
                i => string.Equals(i.StorageName, storageName, StringComparison.OrdinalIgnoreCase));

            if (match == null && !ShowAllToggle.IsChecked.GetValueOrDefault())
            {
                // Not in the short list — widen once, then look again.
                ShowAllToggle.IsChecked = true;
                ReloadItems();
                match = _session.Items.FirstOrDefault(
                    i => string.Equals(i.StorageName, storageName, StringComparison.OrdinalIgnoreCase));
            }

            if (match == null)
            {
                StatusLabel.Text = "No colorable item is registered for '" + storageName + "'.";
                return false;
            }

            SearchBox.Text = string.Empty;
            ItemList.SelectedItem = match;
            ItemList.ScrollIntoView(match);
            StatusLabel.Text = "Targeted " + match.DisplayName + " (" + match.StorageName + ").";
            return true;
        }

        // ---- list plumbing -----------------------------------------------------------

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_view != null) _view.View.Refresh();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_session == null) return;

            ReloadItems();
            _session.DumpClassifications();
            StatusLabel.Text = _session.Items.Count + " items. "
                + (_session.Items.Count < 10
                    ? "Open a code file and press Refresh again — syntax items only register once the editor has loaded."
                    : "Ready.");
        }

        private void ShowAll_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ReloadItems();
        }

        private void ReloadItems()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_session == null) return;

            var previous = _selected != null ? _selected.StorageName : null;

            if (ShowAllToggle.IsChecked.GetValueOrDefault())
                _session.LoadAll();
            else
                _session.LoadCurated();

            BuildView();

            if (previous != null)
            {
                var restored = _session.Items.FirstOrDefault(
                    i => string.Equals(i.StorageName, previous, StringComparison.OrdinalIgnoreCase));
                if (restored != null) ItemList.SelectedItem = restored;
            }
        }

        private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selected = ItemList.SelectedItem as ItemViewModel;
            DetailPanel.IsEnabled = _selected != null;
            if (_selected == null)
            {
                DetailName.Text = string.Empty;
                DetailStorage.Text = string.Empty;
                return;
            }

            DetailName.Text = _selected.DisplayName;
            DetailStorage.Text = _selected.StorageName;
            BoldToggle.IsChecked = _selected.Bold;
            SyncPickerToSelection();
            UpdateContrastLabel();
        }

        private void Channel_Changed(object sender, RoutedEventArgs e)
        {
            SyncPickerToSelection();
        }

        private void SyncPickerToSelection()
        {
            if (_selected == null) return;
            _suppressPickerFeedback = true;
            try
            {
                bool isForeground = ForegroundTarget.IsChecked.GetValueOrDefault();

                Picker.SetColor(
                    isForeground
                        ? _selected.Colors.ForegroundRgb
                        : _selected.Colors.BackgroundRgb,
                    notify: false);

                // An inherited channel is an unpainted one, which is what transparent means
                // here. Set after SetColor, which clears the flag by design.
                Picker.SetTransparent(isForeground
                    ? _selected.Colors.ForegroundInherited
                    : _selected.Colors.BackgroundInherited);
            }
            finally
            {
                _suppressPickerFeedback = false;
            }

            UpdatePaintSwatch();
        }

        /// <summary>
        /// Keeps the "paint whole editor" glyph filled with the colour it would apply. The
        /// button takes its colour from the picker, so showing anything else there would be a
        /// lie about what pressing it does.
        /// </summary>
        private void UpdatePaintSwatch()
        {
            var brush = new System.Windows.Media.SolidColorBrush(ColorMath.ToWpf(Picker.Color));
            brush.Freeze();
            PaintSwatch.Foreground = brush;
        }

        // ---- live edit ---------------------------------------------------------------

        private void OnPickerColorChanged(object sender, uint colorRef)
        {
            if (_selected == null || _suppressPickerFeedback)
            {
                Diag.Log("PickerColorChanged ignored; selected=" + (_selected != null)
                         + " suppressed=" + _suppressPickerFeedback);
                return;
            }

            Diag.Log("PickerColorChanged '" + _selected.StorageName + "' -> " + ColorMath.ToHex(colorRef)
                     + " channel=" + (ForegroundTarget.IsChecked.GetValueOrDefault() ? "fg" : "bg"));

            if (ForegroundTarget.IsChecked.GetValueOrDefault())
                _selected.SetForeground(colorRef);
            else
                _selected.SetBackground(colorRef);

            UpdateContrastLabel();
            UpdatePaintSwatch();
        }

        private void OnPickerColorCommitted(object sender, uint colorRef)
        {
            if (_session == null) return;
            _session.FlushNow();     // land the final value the instant the drag ends

            // Mouse-up ends the gesture: everything the drag applied is one undo step, and the
            // next colour starts a new one. Without this, a whole editing session collapses
            // into a single Ctrl+Z.
            _session.CloseHistoryGroup();
            UpdateDirtyLabel();
            UpdateHistoryButtons();
        }

        private void Bold_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            _selected.Bold = BoldToggle.IsChecked.GetValueOrDefault();
            _session.FlushNow();
            _session.CloseHistoryGroup();
            UpdateDirtyLabel();
            UpdateHistoryButtons();
        }

        private void Inherit_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;

            if (ForegroundTarget.IsChecked.GetValueOrDefault())
                _selected.ClearForeground();
            else
                _selected.ClearBackground();

            _session.FlushNow();
            _session.CloseHistoryGroup();
            SyncPickerToSelection();
            UpdateContrastLabel();
            UpdateDirtyLabel();
            UpdateHistoryButtons();
        }

        private void RevertOne_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            _session.Revert(_selected);
            BoldToggle.IsChecked = _selected.Bold;
            SyncPickerToSelection();
            UpdateContrastLabel();
            UpdateDirtyLabel();
        }

        private void RevertAll_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;
            _session.RevertAll();
            if (_selected != null) BoldToggle.IsChecked = _selected.Bold;
            SyncPickerToSelection();
            UpdateContrastLabel();
            UpdateDirtyLabel();
            StatusLabel.Text = "Reverted to the state this window opened with.";
        }

        // ---- undo / redo -------------------------------------------------------------

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_session == null) return;

            string label = _session.UndoLabel;
            StatusLabel.Text = _session.Undo()
                ? "Undid " + label + "."
                : "Nothing left to undo.";
            AfterHistoryMove();
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_session == null) return;

            string label = _session.RedoLabel;
            StatusLabel.Text = _session.Redo()
                ? "Redid " + label + "."
                : "Nothing left to redo.";
            AfterHistoryMove();
        }

        private void UndoCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Undo_Click(sender, e);
        }

        private void RedoCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Redo_Click(sender, e);
        }

        private void UndoCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _session != null && _session.CanUndo;
        }

        private void RedoCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _session != null && _session.CanRedo;
        }

        /// <summary>
        /// The picker, the bold box and the contrast readout all mirror the selected row, and
        /// undo moves that row underneath them. Re-syncing here is what stops the UI from
        /// showing the colour you just stepped away from.
        /// </summary>
        private void AfterHistoryMove()
        {
            if (_selected != null)
                BoldToggle.IsChecked = _selected.Bold;
            SyncPickerToSelection();
            UpdateContrastLabel();
            UpdateDirtyLabel();
        }

        private void UpdateHistoryButtons()
        {
            if (_session == null) return;
            UndoButton.IsEnabled = _session.CanUndo;
            RedoButton.IsEnabled = _session.CanRedo;
            UndoButton.ToolTip = _session.CanUndo ? "Undo " + _session.UndoLabel + " (Ctrl+Z)" : "Nothing to undo";
            RedoButton.ToolTip = _session.CanRedo ? "Redo " + _session.RedoLabel + " (Ctrl+Y)" : "Nothing to redo";
        }

        // ---- presets -----------------------------------------------------------------

        private void Presets_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_session == null) return;

            // Captured before anything is previewed: this is both the cancel target and the
            // "before" of the single undo step the preset produces.
            var baseline = _session.CaptureAll();

            var dialog = new PresetPicker();
            dialog.PreviewRequested += (fg, text, editor) =>
            {
                // Live, on the real code, and deliberately not saved — the picker is allowed
                // to repaint the editor, not to change what is on disk.
                _session.PreviewSelection(fg, text, editor);
                AfterHistoryMove();
            };

            // GetWindow returns null when the tool window is hosted in the shell's own HWND
            // rather than a WPF Window, which is the normal case for a docked pane. Setting
            // Owner to null throws, so only set it when there is one.
            var owner = Window.GetWindow(this);
            if (owner != null)
                dialog.Owner = owner;

            // ShowDialog's result is not the authority here. Closing the window with Esc or the
            // title-bar X returns false without running any handler, and a preview raised
            // before that point is already on screen — which is how cancelling used to leave
            // the previewed palette painted. Committed is set only by Apply, so anything else
            // is a cancel and every cancel restores.
            dialog.ShowDialog();

            if (!dialog.Committed)
            {
                // Undo the previews, and there may have been several.
                _session.PreviewStates(baseline, "preset picker cancelled");
                AfterHistoryMove();
                StatusLabel.Text = "Cancelled. Colours put back.";
                return;
            }

            var chosenForeground = dialog.ChosenFor(Core.PresetSlot.Foreground);
            var chosenTextArea = dialog.ChosenFor(Core.PresetSlot.TextArea);
            var chosenEditor = dialog.ChosenFor(Core.PresetSlot.Editor);

            _session.ApplySelection(chosenForeground, chosenTextArea, chosenEditor, baseline);
            AfterHistoryMove();

            StatusLabel.Text = "Applied — syntax: " + Describe(chosenForeground)
                + ", text area: " + Describe(chosenTextArea)
                + ", editor: " + Describe(chosenEditor)
                + ". Saved and re-applied at the next Visual Studio start. Ctrl+Z steps back.";
        }

        private static string Describe(Core.ThemePreset preset)
        {
            return preset != null ? preset.Name : "None";
        }

        /// <summary>
        /// Paints the picker's current colour across every editor surface.
        ///
        /// Takes the colour from the picker rather than from the selected row's background:
        /// the row you happen to have selected is usually a syntax item whose background is
        /// inherited, and "apply the inherited value" would paint nothing.
        /// </summary>
        private void PaintEditor_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_session == null) return;

            int count = _session.PaintEditorBackground(Picker.Color);
            AfterHistoryMove();
            StatusLabel.Text = count == 0
                ? "No paintable editor surfaces were found. Open a code file and press Refresh list."
                : "Painted " + count + " editor surface(s) " + ColorMath.ToHex(Picker.Color)
                  + ". Ctrl+Z undoes the lot.";
        }

        /// <summary>
        /// Opens the overflow menu under the button. Placement is explicit: a ContextMenu left
        /// to itself opens at the mouse, which for a toolbar glyph puts it wherever the pointer
        /// happened to land rather than anchored to the control it belongs to.
        /// </summary>
        private void Overflow_Click(object sender, RoutedEventArgs e)
        {
            if (OverflowMenu == null) return;

            // PlacementTarget has to be set here, explicitly. Declaring the menu inside
            // <Button.ContextMenu> gives it an inheritance context — which is what fixed the
            // resource lookups — but it does not set PlacementTarget. WPF fills that in only
            // when the menu is opened through the right-click path; opening it by assigning
            // IsOpen leaves it null, and a null target means the popup is positioned against
            // the screen origin. That is the menu appearing at the far top-left.
            OverflowMenu.PlacementTarget = OverflowButton;
            OverflowMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

            // The surface, resolved rather than referenced. A DynamicResource that fails to
            // resolve leaves Background null and the popup renders see-through, which is what
            // produced a menu that was light over the search box and dark over the list. Asking
            // VSColorTheme for the colour cannot half-fail: either a brush or a stated default.
            OverflowMenu.Background = ThemedBrush(
                Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowBackgroundColorKey,
                System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x26));
            OverflowMenu.Foreground = ThemedBrush(
                Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowTextColorKey,
                System.Windows.Media.Color.FromRgb(0xF1, 0xF1, 0xF1));

            OverflowMenu.IsOpen = true;
        }

        /// <summary>
        /// A themed colour as a real, frozen brush. <paramref name="fallback"/> is used when the
        /// shell has no answer, so the caller always gets something opaque to paint with.
        /// </summary>
        private static System.Windows.Media.Brush ThemedBrush(
            Microsoft.VisualStudio.Shell.ThemeResourceKey key, System.Windows.Media.Color fallback)
        {
            var color = fallback;
            try
            {
                var themed = Microsoft.VisualStudio.PlatformUI.VSColorTheme.GetThemedColor(key);
                // A fully transparent answer is the shell saying "no opinion". Painting with it
                // reproduces the see-through popup this method exists to prevent.
                if (themed.A != 0)
                    color = System.Windows.Media.Color.FromRgb(themed.R, themed.G, themed.B);
            }
            catch (Exception ex)
            {
                Diag.Log("ThemedBrush(" + key.Name + ") failed: " + ex.Message);
            }

            var brush = new System.Windows.Media.SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// The way out. Everything ThemeForge did goes back to the VS theme — the editor, the
        /// Fonts and Colors page, and the saved theme that would otherwise re-apply at the next
        /// start. Confirmed, because it is not undoable: the history is cleared with it.
        /// </summary>
        private void ResetToVsDefaults_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_session == null) return;

            var answer = MessageBox.Show(
                "Hand every colour back to the active Visual Studio theme?\n\n"
                + "This clears the editor, the Fonts & Colors page and the saved theme, so the "
                + "editor looks exactly as it did before this extension ever ran.\n\n"
                + "It cannot be undone — Ctrl+Z will not bring the palette back. Export it first "
                + "if you want to keep it.",
                "XoCrazy",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.OK)
                return;

            int count = _session.ResetToVisualStudioDefaults();

            ReloadItems();
            SyncPickerToSelection();
            UpdateContrastLabel();
            UpdateDirtyLabel();
            UpdateHistoryButtons();

            StatusLabel.Text = count == 0
                ? "Nothing to reset — no overrides were in place."
                : "Reset " + count + " item(s) to the Visual Studio theme. A restart is not needed; "
                  + "some adornments only repaint on the next file you open.";
        }

        private void Forget_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;

            var answer = MessageBox.Show(
                "Delete the saved theme?\n\nColours stay as they are now. On the next "
                + "Visual Studio start nothing is re-applied, so the VS theme's own colours come back.",
                "XoCrazy",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.OK)
                return;

            _session.ForgetSaved();
            StatusLabel.Text = "Saved theme deleted. Current colours are untouched until the next restart.";
        }

        // ---- import / export ---------------------------------------------------------

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export colours",
                Filter = "XoCrazy palette (*.json)|*.json",
                FileName = "my-colors" + Snapshot.FileExtension
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                Snapshot.Save(dialog.FileName, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName), _session.Items);
                StatusLabel.Text = "Exported " + _session.Items.Count + " items.";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Export failed: " + ex.Message;
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import colours",
                Filter = "XoCrazy palette (*.json)|*.json"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var records = Snapshot.Load(dialog.FileName);
                _session.Apply(records);
                SyncPickerToSelection();
                UpdateContrastLabel();
                UpdateDirtyLabel();
                StatusLabel.Text = "Applied " + records.Count + " items. 'Revert all' still undoes this.";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Import failed: " + ex.Message;
            }
        }

        // ---- status ------------------------------------------------------------------

        private void UpdateDirtyLabel()
        {
            if (_session == null) return;
            int dirty = _session.DirtyCount;
            DirtyLabel.Text = dirty == 0 ? string.Empty : dirty + " changed";
        }

        private void UpdateContrastLabel()
        {
            if (_selected == null)
            {
                ContrastLabel.Text = string.Empty;
                return;
            }

            double ratio = _selected.Contrast;
            ContrastLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Contrast against the background behind it: {0:0.00}:1 ({1}). Code is small text, so 4.5:1 is the bar.",
                ratio, ColorMath.ContrastVerdict(ratio));
        }

        /// <summary>
        /// Tears the session down. Called from the tool window pane's Dispose — deliberately
        /// NOT from WPF's Unloaded.
        ///
        /// Unloaded fires whenever the element is re-parented: docking, undocking, auto-hide,
        /// switching to another tab in the same dock well. A tool window does that constantly.
        /// Disposing there detached every item's Changed handler, so nothing was ever queued
        /// again — the picker still moved, the samples still updated, and not one edit reached
        /// the editor for the rest of the session.
        /// </summary>
        public void Shutdown()
        {
            if (_session == null) return;
            Diag.Log("Session shutdown.");
            _session.Dispose();
            _session = null;
        }
    }
}
