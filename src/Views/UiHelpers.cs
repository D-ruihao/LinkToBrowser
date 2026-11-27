using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
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

            var (scaleX, scaleY) = GetDpiScale(window);
            var widthDip = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var heightDip = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

            var workLeftDip = workArea.Left / scaleX;
            var workTopDip = workArea.Top / scaleY;
            var workRightDip = workArea.Right / scaleX;
            var workBottomDip = workArea.Bottom / scaleY;

            var targetLeft = (cursor.X + 12) / scaleX;
            var targetTop = (cursor.Y + 12) / scaleY;

            if (targetLeft + widthDip > workRightDip)
            {
                targetLeft = Math.Max(workLeftDip, workRightDip - widthDip - 8);
            }

            if (targetTop + heightDip > workBottomDip)
            {
                targetTop = Math.Max(workTopDip, workBottomDip - heightDip - 8);
            }

            window.Left = Math.Max(workLeftDip + 8, targetLeft);
            window.Top = Math.Max(workTopDip + 8, targetTop);
        }

        public static void CenterOnCursorScreen(Window window)
        {
            var cursor = Cursor.Position;
            var workArea = Screen.FromPoint(cursor).WorkingArea;
            var (scaleX, scaleY) = GetDpiScale(window);

            var widthDip = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var heightDip = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

            window.Left = workArea.Left / scaleX + Math.Max(0, (workArea.Width / scaleX - widthDip) / 2.0);
            window.Top = workArea.Top / scaleY + Math.Max(0, (workArea.Height / scaleY - heightDip) / 2.0);
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
