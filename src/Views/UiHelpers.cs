using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinkToBrowser.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace LinkToBrowser.Views
{
    public static class UiHelpers
    {
        private static readonly ConcurrentDictionary<string, ImageSource> IconCache =
            new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource GetExeIcon(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return null;
            }

            if (IconCache.TryGetValue(exePath, out var cached))
            {
                return cached;
            }

            ImageSource result = null;
            try
            {
                if (File.Exists(exePath))
                {
                    using var icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null)
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze();
                        result = bmp;
                    }
                }
            }
            catch
            {
            }

            IconCache[exePath] = result;
            return result;
        }

        public static void ApplyTheme(Window window, ThemeMode mode)
        {
            try
            {
                switch (mode)
                {
                    case ThemeMode.Dark:
                        SystemThemeWatcher.UnWatch(window);
                        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
                        break;
                    case ThemeMode.Light:
                        SystemThemeWatcher.UnWatch(window);
                        ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
                        break;
                    default:
                        ApplicationThemeManager.ApplySystemTheme(true);
                        SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, true);
                        break;
                }
            }
            catch
            {
            }
        }

        public static void PlaceNearCursor(Window window)
        {
            var cursor = Cursor.Position;
            var screen = Screen.FromPoint(cursor);
            var workArea = screen.WorkingArea;

            var (scaleX, scaleY) = GetMonitorDpiScale(cursor, window);
            var widthDip = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var heightDip = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

            var (leftPx, topPx) = ComputePlacementNearCursorPx(cursor, workArea, scaleX, scaleY, widthDip, heightDip);
            MoveWindowPhysicalOrDip(window, leftPx, topPx, scaleX, scaleY);
        }

        public static void CenterOnCursorScreen(Window window)
        {
            var cursor = Cursor.Position;
            var workArea = Screen.FromPoint(cursor).WorkingArea;
            var (scaleX, scaleY) = GetMonitorDpiScale(cursor, window);

            var widthDip = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var heightDip = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

            var (leftPx, topPx) = ComputeCenteredOnScreenPx(workArea, scaleX, scaleY, widthDip, heightDip);
            MoveWindowPhysicalOrDip(window, leftPx, topPx, scaleX, scaleY);
        }

        public static (int LeftPx, int TopPx) ComputePlacementNearCursorPx(
            System.Drawing.Point cursorPx,
            Rectangle workAreaPx,
            double scaleX,
            double scaleY,
            double widthDip,
            double heightDip)
        {
            var sx = scaleX > 0 ? scaleX : 1.0;
            var sy = scaleY > 0 ? scaleY : 1.0;
            var marginX = (int)Math.Round(8 * sx);
            var marginY = (int)Math.Round(8 * sy);
            var offsetX = (int)Math.Round(12 * sx);
            var offsetY = (int)Math.Round(12 * sy);
            var widthPx = (int)Math.Round(Math.Max(0, widthDip) * sx);
            var heightPx = (int)Math.Round(Math.Max(0, heightDip) * sy);

            var targetLeft = cursorPx.X + offsetX;
            var targetTop = cursorPx.Y + offsetY;

            if (targetLeft + widthPx > workAreaPx.Right)
            {
                targetLeft = Math.Max(workAreaPx.Left, workAreaPx.Right - widthPx - marginX);
            }

            if (targetTop + heightPx > workAreaPx.Bottom)
            {
                targetTop = Math.Max(workAreaPx.Top, workAreaPx.Bottom - heightPx - marginY);
            }

            var minLeft = workAreaPx.Left + marginX;
            var minTop = workAreaPx.Top + marginY;
            return (Math.Max(minLeft, targetLeft), Math.Max(minTop, targetTop));
        }

        public static (int LeftPx, int TopPx) ComputeCenteredOnScreenPx(
            Rectangle workAreaPx,
            double scaleX,
            double scaleY,
            double widthDip,
            double heightDip)
        {
            var sx = scaleX > 0 ? scaleX : 1.0;
            var sy = scaleY > 0 ? scaleY : 1.0;
            var widthPx = (int)Math.Round(Math.Max(0, widthDip) * sx);
            var heightPx = (int)Math.Round(Math.Max(0, heightDip) * sy);

            var left = workAreaPx.Left + Math.Max(0, (workAreaPx.Width - widthPx) / 2);
            var top = workAreaPx.Top + Math.Max(0, (workAreaPx.Height - heightPx) / 2);
            return (left, top);
        }

        private static void MoveWindowPhysicalOrDip(Window window, int leftPx, int topPx, double scaleX, double scaleY)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero &&
                SetWindowPos(hwnd, IntPtr.Zero, leftPx, topPx, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate))
            {
                return;
            }

            window.Left = leftPx / (scaleX > 0 ? scaleX : 1.0);
            window.Top = topPx / (scaleY > 0 ? scaleY : 1.0);
        }

        private static (double ScaleX, double ScaleY) GetMonitorDpiScale(System.Drawing.Point pointPx, Visual fallbackVisual)
        {
            try
            {
                var pt = new NativePoint { X = pointPx.X, Y = pointPx.Y };
                var hMonitor = MonitorFromPoint(pt, MonitorDefaultToNearest);
                if (hMonitor != IntPtr.Zero &&
                    GetDpiForMonitor(hMonitor, MdtEffectiveDpi, out var dpiX, out var dpiY) == 0 &&
                    dpiX > 0 && dpiY > 0)
                {
                    return (dpiX / 96.0, dpiY / 96.0);
                }
            }
            catch
            {
            }

            return GetDpiScale(fallbackVisual);
        }

        private static (double ScaleX, double ScaleY) GetDpiScale(Visual visual)
        {
            try
            {
                var source = PresentationSource.FromVisual(visual);
                if (source?.CompositionTarget != null)
                {
                    var m = source.CompositionTarget.TransformToDevice;
                    if (m.M11 > 0 && m.M22 > 0)
                    {
                        return (m.M11, m.M22);
                    }
                }
            }
            catch
            {
            }

            return (1.0, 1.0);
        }

        private const uint MonitorDefaultToNearest = 2;
        private const int MdtEffectiveDpi = 0;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    }

    public sealed class SingleInstanceSettingsHost : IDisposable
    {
        private const string MutexName = @"Local\LinkToBrowser.Settings.Mutex";
        private const string WakeEventName = @"Local\LinkToBrowser.Settings.Wake";

        private Mutex _mutex;
        private EventWaitHandle _wakeEvent;
        private volatile bool _disposed;

        public bool TryAcquirePrimary(Action onWakeRequested)
        {
            _mutex = new Mutex(true, MutexName, out var createdNew);
            if (!createdNew)
            {
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(WakeEventName);
                    existing.Set();
                }
                catch
                {
                }

                _mutex.Dispose();
                _mutex = null;
                return false;
            }

            _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
            var listener = new Thread(() =>
            {
                while (!_disposed)
                {
                    try
                    {
                        if (_wakeEvent.WaitOne(500) && !_disposed)
                        {
                            onWakeRequested?.Invoke();
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "LinkToBrowserSettingsWakeListener"
            };
            listener.Start();
            return true;
        }

        public void Dispose()
        {
            _disposed = true;
            try { _wakeEvent?.Set(); } catch { }
            try { _wakeEvent?.Dispose(); } catch { }
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
            _wakeEvent = null;
            _mutex = null;
        }
    }
}
