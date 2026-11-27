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
        private readonly ConfigService _configService;
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
            _configService = configService;
            _logService = logService;
            _selfExe = selfExe;
            _onOpenSettings = onOpenSettings;
            _onLaunchFailed = onLaunchFailed;

            L.Apply(_cfg.Language, this);
            InitializeComponent();
            UiHelpers.ApplyTheme(this, _cfg.Theme);

            var host = UrlService.Host(url);
            HostText.Text = host;
            UrlPreviewText.Text = url;
            UrlPreviewText.ToolTip = url;

            if (!string.IsNullOrWhiteSpace(host) && _configService != null)
            {
                RememberDomainCheck.Content = L.Format("S_PickerRememberDomain", host);
                RememberDomainCheck.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrWhiteSpace(warning))
            {
                WarningBar.Message = warning;
                WarningBar.IsOpen = true;
            }

            _allVisible = BrowserCatalog.GetAllWithRunning(_cfg, _selfExe, includeHidden: false);
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
            CopyBtn.Content = L.Get(ok ? "S_PickerCopied" : "S_PickerFailed");
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            _onOpenSettings?.Invoke();
            Close();
        }

        public static RoutingRule SaveRememberDomainRule(AppConfig cfg, ConfigService configService, string url, BrowserEntry entry)
        {
            if (cfg == null || configService == null || entry == null || string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var host = UrlService.Host(url);
            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            cfg.Rules ??= new List<RoutingRule>();
            cfg.Rules.RemoveAll(r =>
                r != null &&
                r.MatchKind == RuleMatchKind.DomainSuffix &&
                string.IsNullOrWhiteSpace(r.SourceApps) &&
                string.Equals(r.Pattern?.Trim(), host, StringComparison.OrdinalIgnoreCase));

            var rule = new RoutingRule
            {
                Id = $"rule:{Guid.NewGuid():N}",
                Enabled = true,
                Name = host,
                MatchKind = RuleMatchKind.DomainSuffix,
                Pattern = host,
                SourceApps = string.Empty,
                Action = RuleActionKind.OpenTarget,
                TargetBrowser = new BrowserEntry
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    ExecutablePath = entry.ExecutablePath,
                    ArgumentsTemplate = entry.ArgumentsTemplate,
                    ProgId = entry.ProgId,
                    UserDataDir = entry.UserDataDir,
                    Kind = entry.Kind
                }
            };

            cfg.Rules.Insert(0, rule);
            cfg.EnableRoutingRules = true;
            cfg.SampleRulesSeeded = true;
            configService.Save(cfg);
            return rule;
        }

        private void TryRememberDomain(BrowserEntry entry)
        {
            if (RememberDomainCheck.IsChecked != true || _configService == null)
            {
                return;
            }

            try
            {
                var latestCfg = _configService.Load();
                SaveRememberDomainRule(latestCfg, _configService, _url, entry);
            }
            catch
            {
            }
        }

        private void LaunchItem(BrowserEntry entry)
        {
            if (_cfg.EnableCdpForIsolatedBrowsers &&
                !string.IsNullOrWhiteSpace(entry.UserDataDir) &&
                CdpClient.HasDevToolsActivePort(entry.UserDataDir))
            {
                if (CdpClient.TryOpenTab(entry.UserDataDir, _url, out var cdpErr))
                {
                    TryRememberDomain(entry);
                    var hwnd = RecentBrowserService.FindWindowByUserDataDir(entry.UserDataDir, _selfExe);
                    if (hwnd != IntPtr.Zero)
                    {
                        RecentBrowserService.ActivateWindow(hwnd);
                    }

                    _logService.LogLink(_url, true, $"action=Picker:CDP | target={entry.Name}");
                    Close();
                    return;
                }

                if (entry.Kind == BrowserKind.RunningWindow)
                {
                    var clipOk = ClipboardService.CopyWithRetry(_url);
                    _logService.LogLink(_url, clipOk, $"action=Picker:CDP | target={entry.Name} | warn={cdpErr}");
                    _onLaunchFailed?.Invoke(_url, cdpErr, clipOk);
                    Close();
                    return;
                }
            }

            if (entry.Kind == BrowserKind.RunningWindow)
            {
                var clipOk = ClipboardService.CopyWithRetry(_url);
                var msg = L.Format("S_WarnIsolatedNotRunning", entry.Name, "DevToolsActivePort unavailable");
                _logService.LogLink(_url, clipOk, $"action=Picker:Isolated | target={entry.Name} | warn={msg}");
                _onLaunchFailed?.Invoke(_url, msg, clipOk);
                Close();
                return;
            }

            var launcher = new BrowserLauncher(_selfExe);
            if (launcher.TryLaunch(entry.ExecutablePath, entry.ArgumentsTemplate, _url, out var err))
            {
                TryRememberDomain(entry);
                _logService.LogLink(_url, true, $"action=Picker:Process | target={entry.Name}");
                Close();
                return;
            }

            var copyOk = ClipboardService.CopyWithRetry(_url);
            _logService.LogLink(_url, copyOk, $"action=Picker:Process | target={entry.Name} | warn={err}");
            _onLaunchFailed?.Invoke(_url, err, copyOk);
            Close();
        }
    }
}
