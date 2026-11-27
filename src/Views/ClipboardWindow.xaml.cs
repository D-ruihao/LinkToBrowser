using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using Wpf.Ui.Controls;

namespace LinkToBrowser.Views
{
    public sealed class ClipboardAutoCloseTracker
    {
        private bool _isHovered;
        private bool _isUrlFocused;

        public ClipboardAutoCloseTracker(bool copyOk, int autoCloseSeconds, string warning)
        {
            IsEnabled = copyOk && autoCloseSeconds > 0 && string.IsNullOrWhiteSpace(warning);
            RemainingSeconds = IsEnabled ? autoCloseSeconds : 0;
        }

        public bool IsEnabled { get; private set; }
        public int RemainingSeconds { get; private set; }
        public bool IsPaused => _isHovered || _isUrlFocused;

        public void SetHovered(bool hovered) => _isHovered = hovered;
        public void SetUrlFocused(bool focused) => _isUrlFocused = focused;
        public void Disable()
        {
            IsEnabled = false;
            RemainingSeconds = 0;
        }

        public bool Tick()
        {
            if (!IsEnabled || IsPaused || RemainingSeconds <= 0)
            {
                return false;
            }

            RemainingSeconds--;
            return RemainingSeconds <= 0;
        }
    }

    public partial class ClipboardWindow : FluentWindow
    {
        private readonly string _url;
        private readonly LogService _logService;
        private readonly Action<string> _onOpenPicker;
        private readonly Action _onOpenSettings;
        private readonly ClipboardAutoCloseTracker _tracker;
        private readonly DispatcherTimer _timer;

        public ClipboardWindow(
            DispatchResult dispatchResult,
            AppConfig cfg,
            LogService logService,
            Action<string> onOpenPicker,
            Action onOpenSettings)
        {
            _url = dispatchResult.Url;
            _logService = logService;
            _onOpenPicker = onOpenPicker;
            _onOpenSettings = onOpenSettings;

            InitializeComponent();
            UiHelpers.ApplyTheme(this, (cfg ?? new AppConfig()).Theme);

            UrlBox.Text = _url;
            ApplyStatus(dispatchResult.CopyOk, dispatchResult.Warning);

            _tracker = new ClipboardAutoCloseTracker(
                dispatchResult.CopyOk,
                cfg?.ClipboardAutoCloseSeconds ?? 0,
                dispatchResult.Warning);

            if (_tracker.IsEnabled)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += Timer_Tick;
                UpdateCloseButtonText();
                _timer.Start();
            }

            Loaded += (_, _) =>
            {
                UiHelpers.CenterOnCursorScreen(this);
                Activate();
                UrlBox.SelectAll();
                CloseBtn.Focus();
            };

            Closed += (_, _) => _timer?.Stop();
        }

        private void ApplyStatus(bool copyOk, string warning)
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                StatusBar.Severity = copyOk ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
                StatusBar.Title = copyOk ? "已复制到剪贴板（兜底提示）" : "操作失败";
                StatusBar.Message = warning;
                return;
            }

            if (copyOk)
            {
                StatusBar.Severity = InfoBarSeverity.Success;
                StatusBar.Title = "已复制到剪贴板";
                StatusBar.Message = "切换到目标浏览器按 Ctrl+V 即可粘贴打开。";
            }
            else
            {
                StatusBar.Severity = InfoBarSeverity.Error;
                StatusBar.Title = "剪贴板被占用，复制失败";
                StatusBar.Message = "URL 已写入 links.log，请从下方文本框手动复制。";
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_tracker.Tick())
            {
                _timer.Stop();
                Close();
                return;
            }

            UpdateCloseButtonText();
        }

        private void UpdateCloseButtonText()
        {
            if (!_tracker.IsEnabled)
            {
                CloseBtn.Content = "关闭 (Esc)";
                return;
            }

            CloseBtn.Content = _tracker.IsPaused
                ? "关闭 (Esc) · 已暂停"
                : $"关闭 (Esc) · {_tracker.RemainingSeconds}s";
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            _tracker.SetHovered(true);
            UpdateCloseButtonText();
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            _tracker.SetHovered(false);
            UpdateCloseButtonText();
        }

        private void UrlBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _tracker.SetUrlFocused(true);
            UpdateCloseButtonText();
        }

        private void UrlBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _tracker.SetUrlFocused(false);
            UpdateCloseButtonText();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.C)
            {
                if (!UrlBox.IsKeyboardFocused || UrlBox.SelectionLength == 0)
                {
                    e.Handled = true;
                    CopyAgain();
                }
            }
        }

        private void CopyAgain_Click(object sender, RoutedEventArgs e) => CopyAgain();

        private void CopyAgain()
        {
            var ok = ClipboardService.CopyWithRetry(_url);
            _logService.LogLink(_url, ok);
            if (!ok)
            {
                _timer?.Stop();
                _tracker.Disable();
                UpdateCloseButtonText();
            }
            ApplyStatus(ok, ok ? string.Empty : "仍然失败，请手动选中文本框复制。");
        }

        private void OpenInBrowser_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            _onOpenPicker?.Invoke(_url);
            Close();
        }

        private void OpenLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logService.OpenDirectory();
            }
            catch
            {
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            _onOpenSettings?.Invoke();
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
