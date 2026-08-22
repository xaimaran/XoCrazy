using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ThemeForge.Core
{
    /// <summary>Storage open flags (__FCSTORAGEFLAGS).</summary>
    internal static class StorageFlags
    {
        public const uint LoadDefaults = 0x00000001;
        public const uint NoAutoColors = 0x00000002;
        public const uint PropagateChanges = 0x00000004;
        public const uint ReadOnly = 0x00000008;
    }

    /// <summary>Encoded color kinds (__VSCOLORTYPE).</summary>
    internal static class ColorType
    {
        public const int Invalid = 0;
        public const int Raw = 1;
        public const int ColorIndex = 2;
        public const int SysColor = 3;
        public const int VSColor = 4;
        public const int Automatic = 5;
        public const int TrackBackground = 6;
        public const int TrackForeground = 7;
    }

    /// <summary>Font style bits (__FCFONTFLAGS). Storage only persists bold.</summary>
    internal static class FontFlags
    {
        public const uint Default = 0;
        public const uint Bold = 1;
    }

    /// <summary>
    /// One colorable item as ThemeForge understands it: resolved RGB for display
    /// plus the flags needed to tell "the user set this" from "the theme decided this".
    /// </summary>
    internal sealed class ItemColors
    {
        public uint ForegroundRgb;      // 0x00BBGGRR, always a real paintable color
        public uint BackgroundRgb;
        public bool ForegroundInherited; // true when storage said CT_AUTOMATIC / CT_INVALID
        public bool BackgroundInherited;
        public bool Bold;

        public ItemColors Clone()
        {
            return new ItemColors
            {
                ForegroundRgb = ForegroundRgb,
                BackgroundRgb = BackgroundRgb,
                ForegroundInherited = ForegroundInherited,
                BackgroundInherited = BackgroundInherited,
                Bold = Bold
            };
        }

        public bool SameAs(ItemColors other)
        {
            return other != null
                && other.ForegroundRgb == ForegroundRgb
                && other.BackgroundRgb == BackgroundRgb
                && other.ForegroundInherited == ForegroundInherited
                && other.BackgroundInherited == BackgroundInherited
                && other.Bold == Bold;
        }
    }

    /// <summary>
    /// Thin, honest wrapper over the three shell services that actually own editor colors.
    /// Everything here is main-thread only — these are STA COM interfaces.
    /// </summary>
    internal sealed class FontColorStore
    {
        private readonly IVsFontAndColorStorage _storage;
        private readonly IVsFontAndColorUtilities _utilities;
        private readonly IVsFontAndColorCacheManager _cache;
        private readonly ColorResolver _resolver;

        public FontColorStore(IVsFontAndColorStorage storage, IVsFontAndColorCacheManager cache)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _storage = storage;
            _cache = cache;
            // The storage object also implements the utilities interface; that is how the
            // shell's own Fonts & Colors page resolves automatic colors.
            _utilities = (IVsFontAndColorUtilities)storage;
            _resolver = new ColorResolver(_utilities);
        }

        /// <summary>Exposed so callers can resolve theme colors without reopening a category.</summary>
        public ColorResolver Resolver { get { return _resolver; } }

        public static FontColorStore Create(IServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var storage = services.GetService(typeof(SVsFontAndColorStorage)) as IVsFontAndColorStorage;
            var cache = services.GetService(typeof(SVsFontAndColorCacheManager)) as IVsFontAndColorCacheManager;
            if (storage == null || cache == null)
                return null;
            return new FontColorStore(storage, cache);
        }

        /// <summary>
        /// Reads one item, resolving automatic/indexed/system colors down to real RGB so the
        /// UI never has to show the word "Default" where a swatch belongs.
        /// </summary>
        /// <summary>Why the last <see cref="Read"/> returned null. Null when it succeeded.</summary>
        public string LastReadFailure { get; private set; }

        public ItemColors Read(Guid category, string itemName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            LastReadFailure = null;

            int openHr = _storage.OpenCategory(ref category, StorageFlags.LoadDefaults | StorageFlags.ReadOnly);
            if (ErrorHandler.Failed(openHr))
            {
                LastReadFailure = "OpenCategory failed, hr=0x" + openHr.ToString("X8");
                return null;
            }
            try
            {
                var info = new ColorableItemInfo[1];
                int itemHr = _storage.GetItem(itemName, info);
                if (ErrorHandler.Failed(itemHr))
                {
                    LastReadFailure = "GetItem failed, hr=0x" + itemHr.ToString("X8");
                    return null;
                }

                uint fg, bg;
                bool fgInherited, bgInherited;
                _resolver.Resolve(category, info[0].crForeground, isForeground: true, rgb: out fg, inherited: out fgInherited);
                _resolver.Resolve(category, info[0].crBackground, isForeground: false, rgb: out bg, inherited: out bgInherited);

                return new ItemColors
                {
                    ForegroundRgb = fg,
                    BackgroundRgb = bg,
                    ForegroundInherited = fgInherited,
                    BackgroundInherited = bgInherited,
                    Bold = (info[0].dwFontFlags & FontFlags.Bold) != 0
                };
            }
            finally
            {
                _storage.CloseCategory();
            }
        }

        /// <summary>
        /// Writes one item. <paramref name="colors"/> flags decide whether a channel is
        /// pinned to raw RGB or handed back to the theme as automatic.
        /// </summary>
        /// <summary>Why the last <see cref="Write"/> returned false. Null when it succeeded.</summary>
        public string LastWriteFailure { get; private set; }

        /// <summary>
        /// Resolves classification names to Fonts and Colors display item names. Optional:
        /// without it every MEF-supplied item fails with REGDB_E_KEYMISSING.
        /// </summary>
        public IServiceProvider NameMapServices { get; set; }

        public bool Write(Guid category, string itemName, ItemColors colors)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            LastWriteFailure = null;

            int openHr = _storage.OpenCategory(ref category, StorageFlags.LoadDefaults | StorageFlags.PropagateChanges);
            if (ErrorHandler.Failed(openHr))
            {
                LastWriteFailure = "OpenCategory hr=0x" + openHr.ToString("X8");
                return false;
            }
            try
            {
                // Read-modify-write: never blind-set, or unrelated font flags get stripped.
                var info = new ColorableItemInfo[1];
                int getHr = _storage.GetItem(itemName, info);

                if (ErrorHandler.Failed(getHr) && NameMapServices != null)
                {
                    // The classification name is not the display item name. Ask the
                    // composition for the real one and try again before giving up.
                    var mapped = FontColorNameMap.ToStorageName(NameMapServices, itemName);
                    if (!string.Equals(mapped, itemName, StringComparison.OrdinalIgnoreCase))
                    {
                        int mappedHr = _storage.GetItem(mapped, info);
                        if (ErrorHandler.Succeeded(mappedHr))
                        {
                            Diag.Log("  storage name '" + itemName + "' -> '" + mapped + "'");
                            itemName = mapped;
                            getHr = mappedHr;
                        }
                    }
                }

                if (ErrorHandler.Failed(getHr))
                {
                    LastWriteFailure = "GetItem hr=0x" + getHr.ToString("X8")
                        + (getHr == unchecked((int)0x80040153) ? " (no Fonts and Colors entry for this item)" : string.Empty);
                    return false;
                }

                uint autoColor;
                _utilities.EncodeAutomaticColor(out autoColor);

                info[0].crForeground = colors.ForegroundInherited ? autoColor : ToColorRef(colors.ForegroundRgb);
                info[0].bForegroundValid = 1;
                info[0].crBackground = colors.BackgroundInherited ? autoColor : ToColorRef(colors.BackgroundRgb);
                info[0].bBackgroundValid = 1;

                var flags = info[0].dwFontFlags;
                flags = colors.Bold ? (flags | FontFlags.Bold) : (flags & ~FontFlags.Bold);
                info[0].dwFontFlags = flags;
                info[0].bFontFlagsValid = 1;

                int setHr = _storage.SetItem(itemName, info);
                if (ErrorHandler.Failed(setHr))
                {
                    LastWriteFailure = "SetItem hr=0x" + setHr.ToString("X8");
                    return false;
                }
                return true;
            }
            finally
            {
                _storage.CloseCategory();
            }
        }

        /// <summary>
        /// Forces the editor to repaint with what was just written. This is the call the
        /// built-in Fonts &amp; Colors page only makes when you press OK — which is the entire
        /// reason it has no Apply button and ThemeForge does not need one.
        /// </summary>
        public void Refresh(Guid category)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _cache.ClearCache(ref category);
            _cache.RefreshCache(ref category);
        }

        /// <summary>True when the shell will actually persist writes for this category.</summary>
        public bool IsWritable(Guid category)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int cacheable;
            return ErrorHandler.Succeeded(_cache.CheckCacheable(ref category, out cacheable)) && cacheable != 0;
        }

        /// <summary>Strips any encoding bits so the value is an unambiguous raw COLORREF.</summary>
        private static uint ToColorRef(uint rgb)
        {
            return rgb & 0x00FFFFFFu;
        }
    }
}
