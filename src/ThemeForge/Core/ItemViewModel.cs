using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace ThemeForge.Core
{
    /// <summary>One row in the list. Owns the display state; the session owns persistence.</summary>
    internal sealed class ItemViewModel : INotifyPropertyChanged
    {
        private readonly Func<uint> _editorBackground;

        public ItemViewModel(Guid category, string storageName, string displayName, string group, string hint, Func<uint> editorBackground)
        {
            Category = category;
            StorageName = storageName;
            DisplayName = displayName;
            Group = group;
            Hint = string.IsNullOrEmpty(hint) ? displayName : hint;
            _editorBackground = editorBackground;
            Colors = new ItemColors();
        }

        public Guid Category { get; private set; }
        public string StorageName { get; private set; }
        public string DisplayName { get; private set; }
        public string Group { get; private set; }
        public string Hint { get; private set; }

        /// <summary>The values as last read from, or written to, the shell.</summary>
        public ItemColors Colors { get; private set; }

        /// <summary>Values at the moment the tool window opened — the revert target.</summary>
        public ItemColors Original { get; set; }

        /// <summary>
        /// True for the editor's surfaces — the view background, the margins, the gutter.
        ///
        /// It decides which map the write goes to. Several surfaces share a name with a
        /// classification type (<c>Line Number</c> is both), and the classification map paints
        /// text runs, not the surface: writing there tinted the numbers' own background and
        /// left the margin the colour it was.
        /// </summary>
        public bool IsSurface { get; set; }

        public void SetColors(ItemColors colors)
        {
            Colors = colors ?? new ItemColors();
            RaiseAll();
        }

        // ---- derived display state ---------------------------------------------------

        public Brush ForegroundBrush
        {
            get { return Frozen(ColorMath.ToWpf(Colors.ForegroundRgb)); }
        }

        public Brush BackgroundBrush
        {
            get { return Frozen(ColorMath.ToWpf(Colors.BackgroundRgb)); }
        }

        /// <summary>Background actually painted behind the sample: transparent means editor bg.</summary>
        public Brush PreviewBackgroundBrush
        {
            get
            {
                var rgb = Colors.BackgroundInherited ? _editorBackground() : Colors.BackgroundRgb;
                return Frozen(ColorMath.ToWpf(rgb));
            }
        }

        public FontWeight PreviewWeight
        {
            get { return Colors.Bold ? FontWeights.Bold : FontWeights.Normal; }
        }

        public string ForegroundHex { get { return ColorMath.ToHex(Colors.ForegroundRgb); } }
        public string BackgroundHex { get { return ColorMath.ToHex(Colors.BackgroundRgb); } }

        public bool ForegroundInherited { get { return Colors.ForegroundInherited; } }
        public bool BackgroundInherited { get { return Colors.BackgroundInherited; } }

        public bool Bold
        {
            get { return Colors.Bold; }
            set
            {
                if (Colors.Bold == value) return;
                Colors.Bold = value;
                RaiseAll();
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
        }

        /// <summary>True once this row differs from what was on disk when the window opened.</summary>
        public bool IsDirty
        {
            get { return Original != null && !Original.SameAs(Colors); }
        }

        public double Contrast
        {
            get
            {
                var bg = Colors.BackgroundInherited ? _editorBackground() : Colors.BackgroundRgb;
                return ColorMath.ContrastRatio(ColorMath.ToWpf(Colors.ForegroundRgb), ColorMath.ToWpf(bg));
            }
        }

        public string ContrastLabel
        {
            get { return Contrast.ToString("0.0") + " " + ColorMath.ContrastVerdict(Contrast); }
        }

        public bool ContrastPoor { get { return Contrast < 4.5; } }

        // ---- mutation ----------------------------------------------------------------

        public void SetForeground(uint rgb)
        {
            Colors.ForegroundRgb = rgb;
            Colors.ForegroundInherited = false;
            RaiseAll();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void SetBackground(uint rgb)
        {
            Colors.BackgroundRgb = rgb;
            Colors.BackgroundInherited = false;
            RaiseAll();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void ClearForeground()
        {
            Colors.ForegroundInherited = true;
            RaiseAll();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void ClearBackground()
        {
            Colors.BackgroundInherited = true;
            RaiseAll();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public bool MatchesFilter(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || StorageName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || Group.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Raised when the user changed something and the shell should be told.</summary>
        public event EventHandler Changed;

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaiseAll()
        {
            var h = PropertyChanged;
            if (h == null) return;
            foreach (var name in new[]
            {
                "ForegroundBrush", "BackgroundBrush", "PreviewBackgroundBrush", "PreviewWeight",
                "ForegroundHex", "BackgroundHex", "ForegroundInherited", "BackgroundInherited",
                "Bold", "IsDirty", "Contrast", "ContrastLabel", "ContrastPoor"
            })
            {
                h(this, new PropertyChangedEventArgs(name));
            }
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
