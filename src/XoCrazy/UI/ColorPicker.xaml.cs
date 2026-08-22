using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using XoCrazy.Core;

namespace XoCrazy.UI
{
    /// <summary>
    /// HSV picker with live output.
    ///
    /// <see cref="ColorChanged"/> fires continuously while dragging — that is what is wired to
    /// the live apply path. <see cref="ColorCommitted"/> fires on release and is what feeds the
    /// recent-colours strip, so a drag through fifty shades does not fill it with noise.
    /// </summary>
    public partial class ColorPicker : UserControl
    {
        public sealed class Swatch
        {
            public Brush Brush { get; set; }
            public uint Value { get; set; }
            public string Hex { get; set; }
        }

        private double _hue;
        private double _saturation;
        private double _value = 1.0;
        private bool _suppress;
        private bool _dragging;

        private readonly ObservableCollection<Swatch> _harmony = new ObservableCollection<Swatch>();
        private readonly ObservableCollection<Swatch> _recent = new ObservableCollection<Swatch>();

        public ColorPicker()
        {
            InitializeComponent();
            HarmonyStrip.ItemsSource = _harmony;
            RecentStrip.ItemsSource = _recent;
            TransparencyChecker.Fill = BuildCheckerboard();
            Loaded += (s, e) => Redraw();
        }

        /// <summary>
        /// True while the selected channel is transparent — that is, not painted at all.
        ///
        /// There is no transparent COLORREF: the editor's format maps carry a colour or they
        /// carry nothing, and "nothing" is what makes a format see-through. So transparency is
        /// not a value this control can hold, it is the absence of one, and the host has to be
        /// told rather than handed a number. Hence a separate event instead of a magic colour.
        /// </summary>
        public bool IsTransparent { get; private set; }

        /// <summary>Raised when the user asks for the channel to be cleared.</summary>
        public event EventHandler TransparentRequested;

        /// <summary>Puts the swatch into, or out of, its transparent state.</summary>
        public void SetTransparent(bool transparent)
        {
            IsTransparent = transparent;
            Redraw();
        }

        private void Transparent_Click(object sender, RoutedEventArgs e)
        {
            IsTransparent = true;
            Redraw();
            if (TransparentRequested != null)
                TransparentRequested(this, EventArgs.Empty);
        }

        /// <summary>The grey checkerboard that reads as "nothing is painted here".</summary>
        private static Brush BuildCheckerboard()
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC8, 0xC8, 0xC8)), null,
                new RectangleGeometry(new Rect(0, 0, 8, 8))));
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8C, 0x8C, 0x8C)), null,
                new RectangleGeometry(new Rect(0, 0, 4, 4))));
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8C, 0x8C, 0x8C)), null,
                new RectangleGeometry(new Rect(4, 4, 4, 4))));

            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 8, 8),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.Freeze();
            return brush;
        }

        /// <summary>Fires on every change, including mid-drag.</summary>
        public event EventHandler<uint> ColorChanged;

        /// <summary>Fires when the user settles on a colour.</summary>
        public event EventHandler<uint> ColorCommitted;

        private uint _current;

        /// <summary>The selected colour as a COLORREF.</summary>
        public uint Color
        {
            get { return _current; }
            set { SetColor(value, notify: false); }
        }

        public void SetColor(uint colorRef, bool notify)
        {
            // Any real colour ends the transparent state. Leaving the flag set would keep the
            // swatch showing a checkerboard over a channel that is now painted.
            IsTransparent = false;
            _current = colorRef;
            var c = ColorMath.ToWpf(colorRef);
            ColorMath.ToHsv(c, out _hue, out _saturation, out _value);

            _suppress = true;
            HueSlider.Value = _hue;
            _suppress = false;

            Redraw();
            if (notify && ColorChanged != null) ColorChanged(this, _current);
        }

        // ---- rendering ---------------------------------------------------------------

        private void Redraw()
        {
            var pure = ColorMath.FromHsv(_hue, 1, 1);
            HueLayer.Fill = new SolidColorBrush(pure);

            var current = ColorMath.FromHsv(_hue, _saturation, _value);
            _current = ColorMath.ToColorRef(current);

            // Transparent shows the checkerboard through; any other value covers it.
            CurrentSwatch.Fill = IsTransparent
                ? Brushes.Transparent
                : (Brush)new SolidColorBrush(current);

            if (!HexBox.IsFocused)
                HexBox.Text = ColorMath.ToHex(_current);

            if (SvField.ActualWidth > 0)
            {
                Canvas.SetLeft(SvThumb, _saturation * SvField.ActualWidth - SvThumb.Width / 2);
                Canvas.SetTop(SvThumb, (1 - _value) * SvField.ActualHeight - SvThumb.Height / 2);
                SvThumb.Fill = new SolidColorBrush(current);
            }

            HueMarker.Margin = new Thickness((_hue / 360.0) * Math.Max(0, ActualWidth - 3), 0, 0, 0);

            RebuildHarmony(current);
        }

        private void RebuildHarmony(Color seed)
        {
            var mode = HarmonyMode.SelectedItem as ComboBoxItem;
            var kind = mode != null ? Convert.ToString(mode.Content) : "Monochrome";

            _harmony.Clear();
            foreach (var c in ColorMath.Harmony(seed, kind))
                _harmony.Add(MakeSwatch(ColorMath.ToColorRef(c)));
        }

        private static Swatch MakeSwatch(uint value)
        {
            var brush = new SolidColorBrush(ColorMath.ToWpf(value));
            brush.Freeze();
            return new Swatch { Brush = brush, Value = value, Hex = ColorMath.ToHex(value) };
        }

        private void PushRecent(uint value)
        {
            for (int i = 0; i < _recent.Count; i++)
            {
                if (_recent[i].Value == value)
                {
                    _recent.Move(i, 0);
                    return;
                }
            }
            _recent.Insert(0, MakeSwatch(value));
            while (_recent.Count > 16)
                _recent.RemoveAt(_recent.Count - 1);
        }

        // ---- interaction -------------------------------------------------------------

        private void SvField_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            SvField.CaptureMouse();
            UpdateFromPoint(e.GetPosition(SvField));
        }

        private void SvField_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
                UpdateFromPoint(e.GetPosition(SvField));
        }

        private void SvField_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            SvField.ReleaseMouseCapture();
            Commit();
        }

        private void UpdateFromPoint(Point p)
        {
            if (SvField.ActualWidth <= 0 || SvField.ActualHeight <= 0) return;
            _saturation = Clamp01(p.X / SvField.ActualWidth);
            _value = Clamp01(1 - p.Y / SvField.ActualHeight);
            Redraw();
            if (ColorChanged != null) ColorChanged(this, _current);
        }

        private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppress) return;
            _hue = e.NewValue;
            Redraw();
            if (ColorChanged != null) ColorChanged(this, _current);
        }

        private void Commit_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Commit();
        }

        private void Commit()
        {
            PushRecent(_current);
            if (ColorCommitted != null) ColorCommitted(this, _current);
        }

        private void HexBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ApplyHex();
            e.Handled = true;
        }

        private void HexBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyHex();
        }

        private void ApplyHex()
        {
            uint parsed;
            if (ColorMath.TryParseHex(HexBox.Text, out parsed))
            {
                SetColor(parsed, notify: true);
                Commit();
            }
            else
            {
                HexBox.Text = ColorMath.ToHex(_current);
            }
        }

        private void HarmonyMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            RebuildHarmony(ColorMath.FromHsv(_hue, _saturation, _value));
        }

        private void Swatch_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as FrameworkElement;
            if (border == null || !(border.Tag is uint)) return;
            SetColor((uint)border.Tag, notify: true);
            Commit();
        }

        private void Eyedropper_Click(object sender, RoutedEventArgs e)
        {
            uint picked;
            if (!Eyedropper.TryPick(out picked))
                return;
            SetColor(picked, notify: true);
            Commit();
        }

        private static double Clamp01(double d) { return d < 0 ? 0 : (d > 1 ? 1 : d); }
    }
}
