using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace ThemeForge.Core
{
    /// <summary>
    /// COLORREF is 0x00BBGGRR; WPF is ARGB. Every conversion in the extension goes through
    /// here so the byte order is wrong in at most one place.
    /// </summary>
    internal static class ColorMath
    {
        public static Color ToWpf(uint colorRef)
        {
            byte r = (byte)(colorRef & 0xFF);
            byte g = (byte)((colorRef >> 8) & 0xFF);
            byte b = (byte)((colorRef >> 16) & 0xFF);
            return Color.FromRgb(r, g, b);
        }

        public static uint ToColorRef(Color c)
        {
            return (uint)(c.R | (c.G << 8) | (c.B << 16));
        }

        public static string ToHex(uint colorRef)
        {
            var c = ToWpf(colorRef);
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        public static bool TryParseHex(string text, out uint colorRef)
        {
            colorRef = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var s = text.Trim().TrimStart('#');
            if (s.Length == 3)
                s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            if (s.Length != 6)
                return false;

            uint value;
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return false;

            byte r = (byte)((value >> 16) & 0xFF);
            byte g = (byte)((value >> 8) & 0xFF);
            byte b = (byte)(value & 0xFF);
            colorRef = (uint)(r | (g << 8) | (b << 16));
            return true;
        }

        // ---- HSV ---------------------------------------------------------------------

        public static void ToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            v = max;
            s = max <= 0 ? 0 : delta / max;

            if (delta <= 0) { h = 0; return; }
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * (((b - r) / delta) + 2);
            else h = 60 * (((r - g) / delta) + 4);
            if (h < 0) h += 360;
        }

        public static Color FromHsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            s = Clamp01(s);
            v = Clamp01(v);

            double c = v * s;
            double x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
            double m = v - c;

            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        private static double Clamp01(double d) { return d < 0 ? 0 : (d > 1 ? 1 : d); }

        // ---- Contrast ----------------------------------------------------------------

        private static double Luminance(Color c)
        {
            Func<double, double> ch = v => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            return 0.2126 * ch(c.R / 255.0) + 0.7152 * ch(c.G / 255.0) + 0.0722 * ch(c.B / 255.0);
        }

        /// <summary>WCAG 2.1 contrast ratio, 1.0 to 21.0.</summary>
        public static double ContrastRatio(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>
        /// Code is small text, so 4.5 is the bar that matters. Below 3.0 is genuinely
        /// unreadable on a bad monitor, which is what the warning state is for.
        /// </summary>
        public static string ContrastVerdict(double ratio)
        {
            if (ratio >= 7.0) return "AAA";
            if (ratio >= 4.5) return "AA";
            if (ratio >= 3.0) return "AA large";
            return "fail";
        }

        // ---- Harmony -----------------------------------------------------------------

        /// <summary>
        /// Generates a coherent set from one seed. Syntax palettes fall apart when every
        /// token is picked independently; rotating hue around a fixed S/V keeps them related.
        /// </summary>
        public static List<Color> Harmony(Color seed, string kind)
        {
            double h, s, v;
            ToHsv(seed, out h, out s, out v);
            var result = new List<Color>();

            switch (kind)
            {
                case "Analogous":
                    for (int i = -2; i <= 2; i++)
                        result.Add(FromHsv(h + i * 24, s, v));
                    break;

                case "Complement":
                    result.Add(FromHsv(h, s, v));
                    result.Add(FromHsv(h, s * 0.6, Math.Min(1, v * 1.1)));
                    result.Add(FromHsv(h + 180, s, v));
                    result.Add(FromHsv(h + 180, s * 0.6, Math.Min(1, v * 1.1)));
                    result.Add(FromHsv(h + 180, s * 0.35, v));
                    break;

                case "Triad":
                    for (int i = 0; i < 3; i++)
                        result.Add(FromHsv(h + i * 120, s, v));
                    result.Add(FromHsv(h, s * 0.5, v));
                    result.Add(FromHsv(h + 120, s * 0.5, v));
                    break;

                default: // Monochrome
                    for (int i = 0; i < 5; i++)
                        result.Add(FromHsv(h, s, 0.30 + i * 0.17));
                    break;
            }
            return result;
        }
    }
}
