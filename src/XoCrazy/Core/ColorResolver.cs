using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace XoCrazy.Core
{
    /// <summary>
    /// Turns whatever the storage handed us into a real RGB value.
    ///
    /// This is the fix for the "it just says Default" problem: a stored color is not
    /// necessarily an RGB triple. It can be an index into the editor's palette, a Windows
    /// system color, a VS theme color, or the sentinel "automatic". The built-in dialog
    /// shows the word "Default" for those and stops. We resolve them and show the swatch.
    /// </summary>
    internal sealed class ColorResolver
    {
        private readonly IVsFontAndColorUtilities _utilities;

        public ColorResolver(IVsFontAndColorUtilities utilities)
        {
            _utilities = utilities;
        }

        /// <summary>
        /// Resolves one encoded channel.
        /// <paramref name="inherited"/> reports whether the value came from the theme rather
        /// than from an explicit user choice — the UI badges that instead of hiding it.
        /// </summary>
        public void Resolve(Guid category, uint encoded, bool isForeground, out uint rgb, out bool inherited)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            inherited = false;
            rgb = isForeground ? 0x00000000u : 0x00FFFFFFu;

            int type;
            if (ErrorHandler.Failed(_utilities.GetColorType(encoded, out type)))
                return;

            switch (type)
            {
                case ColorType.Raw:
                    rgb = encoded & 0x00FFFFFFu;
                    return;

                case ColorType.Automatic:
                case ColorType.Invalid:
                    inherited = true;
                    rgb = ResolveAutomatic(category, encoded, isForeground);
                    return;

                case ColorType.ColorIndex:
                {
                    inherited = true;
                    var idx = new COLORINDEX[1];
                    uint resolved;
                    if (ErrorHandler.Succeeded(_utilities.GetEncodedIndex(encoded, idx))
                        && ErrorHandler.Succeeded(_utilities.GetRGBOfIndex(idx[0], out resolved)))
                    {
                        rgb = resolved & 0x00FFFFFFu;
                    }
                    return;
                }

                case ColorType.SysColor:
                case ColorType.VSColor:
                case ColorType.TrackForeground:
                case ColorType.TrackBackground:
                    inherited = true;
                    rgb = ResolveAutomatic(category, encoded, isForeground);
                    return;

                default:
                    inherited = true;
                    return;
            }
        }

        /// <summary>
        /// Asks the shell to substitute a concrete color, seeding the substitution with the
        /// editor's own plain-text color so "automatic" resolves the way the editor paints it.
        /// </summary>
        private uint ResolveAutomatic(Guid category, uint encoded, bool isForeground)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            uint fallback = PlainTextFallback(isForeground);

            uint result;
            if (ErrorHandler.Succeeded(_utilities.GetRGBOfEncodedColor(encoded, fallback, ref category, out result)))
            {
                // The shell hands back an encoded value again for a few pathological cases;
                // masking is safe because a resolved color is always raw.
                return result & 0x00FFFFFFu;
            }
            return fallback;
        }

        private uint PlainTextFallback(bool isForeground)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var index = isForeground ? COLORINDEX.CI_SYSPLAINTEXT_FG : COLORINDEX.CI_SYSPLAINTEXT_BK;
            uint rgb;
            if (ErrorHandler.Succeeded(_utilities.GetRGBOfIndex(index, out rgb)))
                return rgb & 0x00FFFFFFu;
            return isForeground ? 0x00000000u : 0x00FFFFFFu;
        }

        /// <summary>The editor background, used for the contrast readout.</summary>
        public uint EditorBackground()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return PlainTextFallback(isForeground: false);
        }

        /// <summary>
        /// The editor's plain-text foreground. This is what an item with no explicit
        /// foreground actually paints as — assuming black there renders every inherited row
        /// as black text on a dark theme.
        /// </summary>
        public uint EditorForeground()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return PlainTextFallback(isForeground: true);
        }
    }
}
