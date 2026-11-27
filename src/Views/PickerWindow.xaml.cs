using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using Wpf.Ui.Controls;

namespace LinkToBrowser.Views
{
    public partial class PickerWindow : FluentWindow
    {
        private sealed class PickerItem
        {
            public BrowserEntry Entry { get; set; }
            public ImageSource Icon { get; set; }
            public string HotkeyText { get; set; }
            public Visibility BadgeVisibility => string.IsNullOrEmpty(HotkeyText) ? Visibility.Collapsed : Visibility.Visible;
            public Visibility FallbackIconVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;
            public string Name => Entry.Name;
            public string Subtitle => string.IsNullOrWhiteSpace(Entry.ArgumentsTemplate)
                ? Entry.ExecutablePath
                : $"{Entry.ExecutablePath}  {Entry.ArgumentsTemplate}";
        }

        private readonly string _url;
        private readonly AppConfig _cfg;
        private readonly LogService _logService;
        private readonly string _selfExe;
        private readonly Action _onOpenSettings;
        private readonly Action<string, string, bool> _onLaunchFailed;
        private readonly List<BrowserEntry> _allVisible;
        private List<PickerItem> _filtered = new List<PickerItem>();

        public PickerWindow(
            string url,
            string warning,
            AppConfig cfg,
            ConfigService configService,
            LogService logService,
            string selfExe,
            Action onOpenSettings,
            Action<string, string, bool> onLaunchFailed)
        {
            _url = url;
            _cfg = cfg ?? new AppConfig();
            _logService = logService;
            _selfExe = selfExe;
            _onOpenSettings = onOpenSettings;
            _onLaunchFailed = onLaunchFailed;

            InitializeComponent();
            UiHelpers.ApplyTheme(this, _cfg.Theme);

            HostText.Text = UrlService.Host(url);
            UrlPreviewText.Text = url;
            UrlPreviewText.ToolTip = url;

            if (!string.IsNullOrWhiteSpace(warning))
            {
                WarningBar.Message = warning;
                WarningBar.IsOpen = true;
            }

            _allVisible = BrowserCatalog.GetAll(_cfg, _selfExe, includeHidden: false);
            RefreshList(string.Empty);

            Loaded += (_, _) =>
            {
                UiHelpers.PlaceNearCursor(this);
                Activate();
                SearchBox.Focus();
            };
        }

        public static bool ShouldLaunchDigitHotkey(Key key, ModifierKeys modifiers, string filterText, out int digit)
        {
            digit = GetDigit(key);
            return digit >= 1 &&
                   digit <= 9 &&
                   modifiers == ModifierKeys.None &&
                   string.IsNullOrEmpty(filterText);
        }

        private void RefreshList(string query)
        {
            var q = (query ?? string.Empty).Trim();
            var matched = string.IsNullOrEmpty(q)
                ? _allVisible
                : _allVisible.Where(b =>
                    (b.Name ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (b.ExecutablePath ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (b.ArgumentsTemplate ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            _filtered = matched.Select((b, idx) => new PickerItem
            {
                Entry = b,
                Icon = UiHelpers.GetExeIcon(b.ExecutablePath),
                HotkeyText = idx < 9 ? (idx + 1).ToString() : string.Empty
            }).ToList();

            BrowserList.ItemsSource = _filtered;
            var hasItems = _filtered.Count > 0;
            BrowserList.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            EmptyStatePanel.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            if (hasItems)
            {
                BrowserList.SelectedIndex = 0;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshList(SearchBox.Text);
        }

        private void BrowserList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not ListBoxItem)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is ListBoxItem item && item.DataContext is PickerItem pickerItem)
            {
                LaunchItem(pickerItem.Entry);
            }
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
                if (!SearchBox.IsKeyboardFocused || SearchBox.SelectionLength == 0)
                {
                    e.Handled = true;
                    CopyUrl();
                    return;
                }
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.F)
            {
                e.Handled = true;
                SearchBox.Focus();
                return;
            }

            if (e.Key == Key.Down && _filtered.Count > 0)
            {
                e.Handled = true;
                BrowserList.SelectedIndex = Math.Min(_filtered.Count - 1, Math.Max(0, BrowserList.SelectedIndex + 1));
                BrowserList.ScrollIntoView(BrowserList.SelectedItem);
                return;
            }

            if (e.Key == Key.Up && _filtered.Count > 0)
            {
                e.Handled = true;
                BrowserList.SelectedIndex = Math.Max(0, BrowserList.SelectedIndex - 1);
                BrowserList.ScrollIntoView(BrowserList.SelectedItem);
                return;
            }

            if (e.Key == Key.Enter && _filtered.Count > 0)
            {
                e.Handled = true;
                var idx = BrowserList.SelectedIndex >= 0 ? BrowserList.SelectedIndex : 0;
                LaunchItem(_filtered[idx].Entry);
                return;
            }

            if (ShouldLaunchDigitHotkey(e.Key, Keyboard.Modifiers, SearchBox.Text, out var digit) &&
                digit <= _filtered.Count)
            {
                e.Handled = true;
                LaunchItem(_filtered[digit - 1].Entry);
            }
        }

        private static int GetDigit(Key key)
        {
            if (key >= Key.D1 && key <= Key.D9) return key - Key.D0;
            if (key >= Key.NumPad1 && key <= Key.NumPad9) return key - Key.NumPad0;
            return -1;
        }

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            CopyUrl();
        }

        private void CopyUrl()
        {
            var ok = ClipboardService.CopyWithRetry(_url);
            _logService.LogLink(_url, ok, ok ? null : "clipboard copy failed from picker");
            CopyBtn.Content = ok ? "已复制" : "失败";
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            _onOpenSettings?.Invoke();
            Close();
        }

        private void LaunchItem(BrowserEntry entry)
        {
            var launcher = new BrowserLauncher(_selfExe);
            if (launcher.TryLaunch(entry.ExecutablePath, entry.ArgumentsTemplate, _url, out var err))
            {
                _logService.LogLink(_url, true);
                Close();
                return;
            }

            var copyOk = ClipboardService.CopyWithRetry(_url);
            _logService.LogLink(_url, copyOk, $"picker launch failed: {err}");
            _onLaunchFailed?.Invoke(_url, err, copyOk);
            Close();
        }
    }
}
