using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ThemeForge.Core;

namespace ThemeForge.UI
{
    /// <summary>One swatch on a preset card.</summary>
    public sealed class SwatchViewModel
    {
        public Brush Brush { get; set; }
        public string Label { get; set; }
    }

    /// <summary>
    /// A preset, in the shape the card template binds to.
    ///
    /// The card renders real code in the preset's colours rather than showing a strip of
    /// swatches alone: a palette that looks pleasant as eight squares can still be unreadable
    /// as an <c>if</c> statement, and that is the only question worth answering before you
    /// commit to a theme.
    ///
    /// The first card in every list is the None card, which has no palette behind it. It is a
    /// <see cref="PresetViewModel"/> with a null <see cref="Preset"/> rather than a separate
    /// type, so the list stays one homogeneous collection and "nothing selected" stops being a
    /// state the dialog has to handle at all.
    /// </summary>
    public sealed class PresetViewModel
    {
        internal ThemePreset Preset { get; private set; }

        private readonly ObservableCollection<string> _badges = new ObservableCollection<string>();

        internal PresetViewModel(ThemePreset preset)
        {
            Preset = preset;

            if (preset == null)
            {
                // Painted in the shell's own colours, because "None" means exactly that: this
                // territory keeps whatever Visual Studio gives it.
                var neutral = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
                neutral.Freeze();
                BackgroundBrush = Brushes.Transparent;
                ForegroundBrush = neutral;
                Swatches = new List<SwatchViewModel>();
                return;
            }

            BackgroundBrush = Of(ThemePreset.Background);
            ForegroundBrush = Of(ThemePreset.Foreground);
            KeywordBrush = Of(ThemePreset.Keyword);
            ControlBrush = Of(ThemePreset.Control);
            StringBrush = Of(ThemePreset.String);
            NumberBrush = Of(ThemePreset.Number);
            CommentBrush = Of(ThemePreset.Comment);
            TypeBrush = Of(ThemePreset.Type);
            InterfaceBrush = Of(ThemePreset.Interface);
            MethodBrush = Of(ThemePreset.Method);
            PropertyBrush = Of(ThemePreset.Property);
            FieldBrush = Of(ThemePreset.Field);
            OperatorBrush = Of(ThemePreset.Operator);
            LineNumberBrush = Of(ThemePreset.LineNumber);
            SelectionBrush = Of(ThemePreset.Selection);

            Swatches = preset.PreviewRoles
                .Select(role => new SwatchViewModel { Brush = Of(role), Label = role + "  " + preset.Get(role) })
                .ToList();
        }

        public bool IsNone { get { return Preset == null; } }

        public string Name { get { return Preset != null ? Preset.Name : "None"; } }
        public string Origin { get { return Preset != null ? Preset.Origin : "Leave this to Visual Studio"; } }

        public Visibility SampleVisibility { get { return Preset != null ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility NoneVisibility { get { return Preset != null ? Visibility.Collapsed : Visibility.Visible; } }

        public string NoneDescription
        {
            get
            {
                return "Nothing is written for this part of the editor, and anything a previous "
                     + "palette wrote here is handed back. The active Visual Studio theme decides it.";
            }
        }

        /// <summary>The slots this palette is currently assigned to. Drives the card badges.</summary>
        public ObservableCollection<string> Badges { get { return _badges; } }

        public Brush BackgroundBrush { get; private set; }
        public Brush ForegroundBrush { get; private set; }
        public Brush KeywordBrush { get; private set; }
        public Brush ControlBrush { get; private set; }
        public Brush StringBrush { get; private set; }
        public Brush NumberBrush { get; private set; }
        public Brush CommentBrush { get; private set; }
        public Brush TypeBrush { get; private set; }
        public Brush InterfaceBrush { get; private set; }
        public Brush MethodBrush { get; private set; }
        public Brush PropertyBrush { get; private set; }
        public Brush FieldBrush { get; private set; }
        public Brush OperatorBrush { get; private set; }
        public Brush LineNumberBrush { get; private set; }
        public Brush SelectionBrush { get; private set; }
        public List<SwatchViewModel> Swatches { get; private set; }

        private Brush Of(string role)
        {
            uint rgb;
            if (!ColorMath.TryParseHex(Preset.Get(role), out rgb))
                rgb = 0;
            var brush = new SolidColorBrush(ColorMath.ToWpf(rgb));
            brush.Freeze();
            return brush;
        }
    }

    public partial class PresetPicker : Window
    {
        private readonly List<PresetViewModel> _cards;
        private readonly Dictionary<PresetSlot, ThemePreset> _choice =
            new Dictionary<PresetSlot, ThemePreset>();

        private PresetSlot _slot;
        private bool _suppressSelection;

        /// <summary>True once Apply has been pressed. Cancel leaves it false.</summary>
        internal bool Committed { get; private set; }

        internal ThemePreset ChosenFor(PresetSlot slot)
        {
            ThemePreset preset;
            return _choice.TryGetValue(slot, out preset) ? preset : null;
        }

        /// <summary>
        /// Raised whenever any slot's choice changes. The host paints the combination on the
        /// live editor — a card can only show you the colours, not what they do to the file you
        /// are actually reading.
        /// </summary>
        internal event Action<ThemePreset, ThemePreset, ThemePreset> PreviewRequested;

        public PresetPicker()
        {
            InitializeComponent();

            // None first, deliberately: it is the answer for anyone who wants a syntax palette
            // without letting it near their background, and burying it under forty cards made
            // that look impossible.
            _cards = new List<PresetViewModel> { new PresetViewModel(null) };
            _cards.AddRange(ThemePresets.All.Select(p => new PresetViewModel(p)));
            PresetList.ItemsSource = _cards;

            foreach (PresetSlot slot in Enum.GetValues(typeof(PresetSlot)))
                _choice[slot] = PresetSelection.PresetFor(slot);

            _slot = PresetSelection.ActiveSlot;

            HintLabel.Text = ThemePresets.All.Length + " palettes.";

            // No preview is raised here, and none is raised from Loaded either. Opening the
            // dialog used to select the first card and immediately paint it onto the editor,
            // so merely looking at your options overwrote your theme with Visual Studio Dark+.
            // The first preview now waits for the user to actually choose something.
            RefreshSlotBoxes();
            SelectCardForActiveSlot();
        }

        // ---- slots -------------------------------------------------------------------

        private void Slot_Click(object sender, RoutedEventArgs e)
        {
            var box = sender as ToggleButton;
            if (box == null) return;

            _slot = box == SlotTextArea ? PresetSlot.TextArea
                  : box == SlotEditor ? PresetSlot.Editor
                  : PresetSlot.Foreground;

            RefreshSlotBoxes();
            SelectCardForActiveSlot();
        }

        /// <summary>
        /// Puts the three boxes in sync with <see cref="_slot"/> and the current choices.
        /// Mutual exclusion lives here rather than in a RadioButton GroupName because a box
        /// that is already checked must stay checked when clicked again — a radio would let the
        /// user uncheck it and leave no slot active at all.
        /// </summary>
        private void RefreshSlotBoxes()
        {
            SlotForeground.IsChecked = _slot == PresetSlot.Foreground;
            SlotTextArea.IsChecked = _slot == PresetSlot.TextArea;
            SlotEditor.IsChecked = _slot == PresetSlot.Editor;

            SlotForegroundValue.Text = NameOf(PresetSlot.Foreground);
            SlotTextAreaValue.Text = NameOf(PresetSlot.TextArea);
            SlotEditorValue.Text = NameOf(PresetSlot.Editor);

            switch (_slot)
            {
                case PresetSlot.TextArea:
                    SlotHint.Text = "Choosing a palette here sets the background behind your code, "
                                  + "the selection colour, and the collapsed-region block. Syntax colours are untouched.";
                    break;
                case PresetSlot.Editor:
                    SlotHint.Text = "Choosing a palette here sets the surfaces around your code — gutter, "
                                  + "breakpoint bar, outlining strip, overview margin. Syntax colours and the code background are untouched.";
                    break;
                default:
                    SlotHint.Text = "Choosing a palette here sets syntax colours only. It will not change "
                                  + "the colour of the page.";
                    break;
            }

            RefreshBadges();
        }

        private string NameOf(PresetSlot slot)
        {
            var preset = ChosenFor(slot);
            return preset != null ? preset.Name : "None";
        }

        /// <summary>Rebuilds the per-card badges from the three current choices.</summary>
        private void RefreshBadges()
        {
            foreach (var card in _cards)
                card.Badges.Clear();

            AddBadge(PresetSlot.Foreground, "Foreground");
            AddBadge(PresetSlot.TextArea, "Text area");
            AddBadge(PresetSlot.Editor, "Editor");
        }

        private void AddBadge(PresetSlot slot, string label)
        {
            var preset = ChosenFor(slot);
            var card = _cards.FirstOrDefault(c => c.Preset == preset);
            if (card != null)
                card.Badges.Add(label);
        }

        /// <summary>
        /// Moves the list to whatever the active slot already holds, without treating the move
        /// as a user choice — that is what <see cref="_suppressSelection"/> guards.
        /// </summary>
        private void SelectCardForActiveSlot()
        {
            var preset = ChosenFor(_slot);
            var card = _cards.FirstOrDefault(c => c.Preset == preset) ?? _cards[0];

            _suppressSelection = true;
            try
            {
                PresetList.SelectedItem = card;
                PresetList.ScrollIntoView(card);
            }
            finally
            {
                _suppressSelection = false;
            }
        }

        // ---- list --------------------------------------------------------------------

        private void PresetList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressSelection)
                return;

            var selected = PresetList.SelectedItem as PresetViewModel;
            if (selected == null)
                return;

            _choice[_slot] = selected.Preset;

            RefreshSlotBoxes();
            RaisePreview();
        }

        private void RaisePreview()
        {
            if (PreviewRequested == null)
                return;

            PreviewRequested(ChosenFor(PresetSlot.Foreground),
                             ChosenFor(PresetSlot.TextArea),
                             ChosenFor(PresetSlot.Editor));
        }

        // ---- commit ------------------------------------------------------------------

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            foreach (var pair in _choice)
                PresetSelection.Assign(pair.Key, pair.Value != null ? pair.Value.Name : null);

            PresetSelection.ActiveSlot = _slot;
            PresetSelection.Save();

            Committed = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Nothing is written here on purpose. The host holds the baseline it captured
            // before the dialog opened and restores it when Committed is false, which is the
            // only way a cancel can also undo the live previews.
            Committed = false;
        }
    }
}
