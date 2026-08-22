using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using XoCrazy.Core;

namespace XoCrazy.UI
{
    /// <summary>
    /// Screen colour picker.
    ///
    /// The screen is captured to a bitmap *before* the overlay appears and every sample is
    /// taken from that bitmap. Reading the live desktop DC through our own window would
    /// sample the overlay too. All coordinates are physical pixels — going through WPF's
    /// device-independent units breaks the moment a second monitor runs a different DPI.
    /// </summary>
    internal static class Eyedropper
    {
        public static bool TryPick(out uint colorRef)
        {
            colorRef = 0;

            int originX = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int originY = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (width <= 0 || height <= 0)
                return false;

            using (var capture = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(capture))
                    g.CopyFromScreen(originX, originY, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);

                var source = ToBitmapSource(capture);
                uint picked = 0;
                bool ok = false;

                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Cursor = Cursors.Cross,
                    Content = new System.Windows.Controls.Image { Source = source, Stretch = System.Windows.Media.Stretch.Fill }
                };

                window.SourceInitialized += (s, e) =>
                {
                    var handle = new WindowInteropHelper(window).Handle;
                    SetWindowPos(handle, HWND_TOPMOST, originX, originY, width, height, SWP_SHOWWINDOW);
                };

                window.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                        window.Close();
                };

                window.MouseLeftButtonDown += (s, e) =>
                {
                    POINT cursor;
                    if (!GetCursorPos(out cursor))
                    {
                        window.Close();
                        return;
                    }
                    int x = cursor.X - originX;
                    int y = cursor.Y - originY;
                    if (x >= 0 && y >= 0 && x < width && y < height)
                    {
                        var c = capture.GetPixel(x, y);
                        picked = (uint)(c.R | (c.G << 8) | (c.B << 16));
                        ok = true;
                    }
                    window.Close();
                };

                window.ShowDialog();
                colorRef = picked;
                return ok;
            }
        }

        private static BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
