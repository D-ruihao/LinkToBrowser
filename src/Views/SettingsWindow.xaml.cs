using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace LinkToBrowser.Views
{
    public partial class SettingsWindow : FluentWindow
    {
        public sealed class ProgIdChoice
        {
            public string ProgId { get; set; }
            public string Display { get; set; }
        }

        private sealed class ComboItem<T>
        {
            public T Value { get; set; }
            public string Label { get; set; }
            public override string ToString() => Label;
        }

        private sealed class BrowserRowVm
        {
            public BrowserEntry Entry { get; set; }
            public bool IsHidden { get; set; }
            public ImageSource Icon { get; set; }
            public Visibility FallbackIconVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;
            public string Name => Entry.Name;
            public string KindLabel => Entry.Kind switch
            {
                BrowserKind.Custom => L.Get("S_KindCustom"),
                BrowserKind.ChromiumProfile => L.Get("S_KindProfile"),
                _ => L.Get("S_KindSystem")
            };
            public string CommandPreview => string.IsNullOrWhiteSpace(Entry.ArgumentsTemplate)
                ? Entry.ExecutablePath
                : $"{Entry.ExecutablePath}  {Entry.ArgumentsTemplate}";
            public string VisibilityButtonText => IsHidden ? L.Get("S_StatusHidden") : L.Get("S_StatusVisible");
            public Visibility CustomActionVisibility => Entry.Kind == BrowserKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        }

        private readonly ConfigService _configService;
        private readonly LogService _logService;
        private readonly SystemRegistration _registration;
        private readonly string _selfExe;
        private readonly DispatcherTimer _savedBadgeTimer;

        private AppConfig _cfg;
        private List<BrowserEntry> _allBrowsers = new List<BrowserEntry>();
        private bool _isLoadingUi;
        private string _currentPage = "General";

        public SettingsWindow(
            ConfigService configService,
            LogService logService,
            SystemRegistration registration,
            string selfExe)
        {
            _configService = configService;
            _logService = logService;
            _registration = registration;
            _selfExe = selfExe;

            _cfg = _configService.Load();
            if (_registration.TrySnapshot(_cfg))
            {
                _configService.Save(_cfg);
            }

            L.Apply(_cfg.Language, this);
            InitializeComponent();

            var ver = typeof(SettingsWindow).Assembly.GetName().Version;
            if (ver != null)
            {
                AboutVersionText.Text = $"LinkToBrowser v{ver.Major}.{ver.Minor}.{ver.Build}";
            }

            _savedBadgeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            _savedBadgeTimer.Tick += (_, _) =>
            {
                SavedBadgeText.Opacity = 0;
                _savedBadgeTimer.Stop();
            };

            PopulateStaticCombos();
            UiHelpers.ApplyTheme(this, _cfg.Theme);
            RefreshAllState();
            Activated += (_, _) =>
            {
                RefreshRegistrationBanner();
                RefreshDiagnostics();
            };
        }

        private void PopulateStaticCombos()
        {
            var prevLoading = _isLoadingUi;
            _isLoadingUi = true;
            try
            {
                AutoCloseCombo.ItemsSource = new[]
                {
                    new ComboItem<int> { Value = 0, Label = L.Get("S_AutoClose0") },
                    new ComboItem<int> { Value = 3, Label = L.Format("S_AutoCloseN", 3) },
                    new ComboItem<int> { Value = 5, Label = L.Format("S_AutoCloseN", 5) },
                    new ComboItem<int> { Value = 8, Label = L.Format("S_AutoCloseN", 8) },
                    new ComboItem<int> { Value = 15, Label = L.Format("S_AutoCloseN", 15) }
                };

                ModifierCombo.ItemsSource = new[]
                {
                    new ComboItem<ModifierKey> { Value = ModifierKey.None, Label = L.Get("S_ModNone") },
                    new ComboItem<ModifierKey> { Value = ModifierKey.Alt, Label = L.Get("S_ModAlt") },
                    new ComboItem<ModifierKey> { Value = ModifierKey.Shift, Label = L.Get("S_ModShift") }
                };

                ThemeCombo.ItemsSource = new[]
                {
                    new ComboItem<ThemeMode> { Value = ThemeMode.System, Label = L.Get("S_ThemeSystem") },
                    new ComboItem<ThemeMode> { Value = ThemeMode.Dark, Label = L.Get("S_ThemeDark") },
                    new ComboItem<ThemeMode> { Value = ThemeMode.Light, Label = L.Get("S_ThemeLight") }
                };

                LanguageCombo.ItemsSource = new[]
                {
                    new ComboItem<LanguageMode> { Value = LanguageMode.ZhCn, Label = L.Get("S_LangZhCn") },
                    new ComboItem<LanguageMode> { Value = LanguageMode.EnUs, Label = L.Get("S_LangEnUs") },
                    new ComboItem<LanguageMode> { Value = LanguageMode.System, Label = L.Get("S_LangSystem") }
                };
            }
            finally
            {
                _isLoadingUi = prevLoading;
            }
        }

        private void RefreshAllState()
        {
            _isLoadingUi = true;
            try
            {
                _allBrowsers = BrowserCatalog.GetAll(_cfg, _selfExe, includeHidden: true);
                RefreshRegistrationBanner();
                BindGeneralControls();
                BindBrowserRows();
                RefreshDiagnostics();
            }
            finally
            {
                _isLoadingUi = false;
            }
        }

        private void RefreshRegistrationBanner()
        {
            var httpProg = _registration.GetUserChoice("http");
            var httpsProg = _registration.GetUserChoice("https");
            var regCmd = _registration.GetRegisteredCommand();
            var isReg = !string.IsNullOrEmpty(regCmd);
            var pathOk = _registration.PathMatchesSelf();
            var isDef = _registration.IsSystemDefault();

            RegPathSubtitle.Text = isReg
                ? L.Format("S_RegSubCurrent", regCmd)
                : L.Format("S_RegSubNotReg", _selfExe);

            var banner = EvaluateRegistrationBanner(isDef, isReg, pathOk, httpProg, httpsProg);
            RegStatusBar.Severity = banner.Severity;
            RegStatusBar.Title = banner.Title;
            RegStatusBar.Message = banner.Message;
            RegisterBtn.Content = banner.ButtonText;
            RegisterBtn.Appearance = banner.ButtonAppearance;
        }

        public static (InfoBarSeverity Severity, string Title, string Message, string ButtonText, ControlAppearance ButtonAppearance) EvaluateRegistrationBanner(
            bool isDefault,
            bool isRegistered,
            bool pathMatchesSelf,
            string httpProg,
            string httpsProg)
        {
            if (isDefault && isRegistered && pathMatchesSelf)
            {
                return (
                    InfoBarSeverity.Success,
                    L.Get("S_BannerOkTitle"),
                    L.Get("S_BannerOkMsg"),
                    L.Get("S_RegBtnUpdatePath"),
                    ControlAppearance.Secondary);
            }

            if (isDefault && isRegistered && !pathMatchesSelf)
            {
                return (
                    InfoBarSeverity.Warning,
                    L.Get("S_BannerMovedTitle"),
                    L.Get("S_BannerMovedMsg"),
                    L.Get("S_RegBtnUpdatePath"),
                    ControlAppearance.Primary);
            }

            if (isDefault && !isRegistered)
            {
                return (
                    InfoBarSeverity.Warning,
                    L.Get("S_BannerPendingTitle"),
                    L.Get("S_BannerPendingMsg"),
                    L.Get("S_RegBtnRegister"),
                    ControlAppearance.Secondary);
            }

            if (isRegistered)
            {
                return (
                    InfoBarSeverity.Informational,
                    L.Get("S_BannerRegOnlyTitle"),
                    L.Format("S_BannerRegOnlyMsg", httpProg, httpsProg),
                    L.Get("S_RegBtnUpdatePath"),
                    ControlAppearance.Secondary);
            }

            return (
                InfoBarSeverity.Warning,
                L.Get("S_BannerUnregTitle"),
                L.Get("S_BannerUnregMsg"),
                L.Get("S_RegBtnRegister"),
                ControlAppearance.Primary);
        }

        private void BindGeneralControls()
        {
            ModeNativeRadio.IsChecked = _cfg.GlobalMode == GlobalMode.NativePassthrough;
            ModeDefaultRadio.IsChecked = _cfg.GlobalMode == GlobalMode.DefaultBrowser;
            ModePickerRadio.IsChecked = _cfg.GlobalMode == GlobalMode.PickerPopup;
            ModeClipboardRadio.IsChecked = _cfg.GlobalMode == GlobalMode.ClipboardOnly;

            var (progChoices, selectedProgIndex) = BuildProgIdChoices(_cfg.OriginalDefaultProgId, _allBrowsers);
            OriginalProgIdCombo.ItemsSource = progChoices;
            OriginalProgIdCombo.SelectedIndex = selectedProgIndex;
            OriginalProgIdCombo.ToolTip = (OriginalProgIdCombo.SelectedItem as ProgIdChoice)?.Display;

            var defaultChoices = new List<BrowserEntry>(_allBrowsers);
            if (_cfg.DefaultBrowser != null &&
                !defaultChoices.Any(b => string.Equals(b.Id, _cfg.DefaultBrowser.Id, StringComparison.OrdinalIgnoreCase)))
            {
                defaultChoices.Insert(0, _cfg.DefaultBrowser);
            }

            DefaultBrowserCombo.ItemsSource = defaultChoices;
            if (_cfg.DefaultBrowser != null)
            {
                var matchedDefault = defaultChoices.FirstOrDefault(b =>
                    string.Equals(b.Id, _cfg.DefaultBrowser.Id, StringComparison.OrdinalIgnoreCase));
                DefaultBrowserCombo.SelectedItem = matchedDefault;
                DefaultBrowserCombo.ToolTip = matchedDefault?.Name;
            }
            else
            {
                DefaultBrowserCombo.SelectedIndex = -1;
                DefaultBrowserCombo.ToolTip = null;
            }

            ShowClipPopupToggle.IsChecked = _cfg.ShowClipboardPopup;
            ChromiumProfilesToggle.IsChecked = _cfg.ShowChromiumProfiles;
            StripTrackingToggle.IsChecked = _cfg.StripTrackingParams;

            SelectComboItem(AutoCloseCombo, _cfg.ClipboardAutoCloseSeconds, 0);
            SelectComboItem(ModifierCombo, _cfg.Modifier, ModifierKey.None);
            SelectComboItem(ThemeCombo, _cfg.Theme, ThemeMode.System);
            SelectComboItem(LanguageCombo, _cfg.Language, LanguageMode.ZhCn);
        }

        public static (List<ProgIdChoice> Choices, int SelectedIndex) BuildProgIdChoices(
            string currentOriginalProgId,
            IEnumerable<BrowserEntry> browsers)
        {
            var list = browsers?.ToList() ?? new List<BrowserEntry>();
            var choices = new List<ProgIdChoice>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasConfigured = !string.IsNullOrWhiteSpace(currentOriginalProgId) &&
                                !string.Equals(currentOriginalProgId, SystemRegistration.ProgId, StringComparison.OrdinalIgnoreCase);

            if (!hasConfigured)
            {
                choices.Add(new ProgIdChoice { ProgId = string.Empty, Display = L.Get("S_SelectOriginalBrowser") });
            }

            foreach (var b in list)
            {
                if (!string.IsNullOrWhiteSpace(b.ProgId) && seen.Add(b.ProgId))
                {
                    choices.Add(new ProgIdChoice
                    {
                        ProgId = b.ProgId,
                        Display = $"{b.Name} ({b.ProgId})"
                    });
                }
            }

            if (hasConfigured && seen.Add(currentOriginalProgId))
            {
                choices.Insert(0, new ProgIdChoice
                {
                    ProgId = currentOriginalProgId,
                    Display = currentOriginalProgId
                });
            }

            if (!hasConfigured)
            {
                return (choices, 0);
            }

            var idx = choices.FindIndex(p => string.Equals(p.ProgId, currentOriginalProgId, StringComparison.OrdinalIgnoreCase));
            return (choices, idx >= 0 ? idx : 0);
        }

        private static void SelectComboItem<T>(ComboBox combo, T target, T fallback)
        {
            if (combo.ItemsSource is not IEnumerable<ComboItem<T>> items)
            {
                return;
            }

            var list = items.ToList();
            combo.SelectedItem = list.FirstOrDefault(i => EqualityComparer<T>.Default.Equals(i.Value, target))
                                 ?? list.FirstOrDefault(i => EqualityComparer<T>.Default.Equals(i.Value, fallback));
        }

        private void BindBrowserRows()
        {
            var hidden = new HashSet<string>(_cfg.HiddenBrowserIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            BrowsersListControl.ItemsSource = _allBrowsers.Select(b => new BrowserRowVm
            {
                Entry = b,
                IsHidden = hidden.Contains(b.Id),
                Icon = UiHelpers.GetExeIcon(b.ExecutablePath)
            }).ToList();
        }

        private void RefreshDiagnostics()
        {
            var httpProg = _registration.GetUserChoice("http");
            var httpsProg = _registration.GetUserChoice("https");
            var cmd = _registration.GetRegisteredCommand();
            DiagAssocText.Text = L.Format(
                "S_DiagText",
                string.IsNullOrEmpty(httpProg) ? L.Get("S_Unset") : httpProg,
                string.IsNullOrEmpty(httpsProg) ? L.Get("S_Unset") : httpsProg,
                string.IsNullOrEmpty(cmd) ? L.Get("S_NotRegistered") : cmd);
            DiagConfigPathText.Text = _configService.ConfigPath;
            DiagLogDirText.Text = _logService.LogDir;
        }

        private void SaveAndFlash()
        {
            _configService.Save(_cfg);
            SavedBadgeText.Opacity = 1;
            _savedBadgeTimer.Stop();
            _savedBadgeTimer.Start();
        }

        private void SwitchPage(string page)
        {
            _currentPage = page;
            GeneralPage.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
            BrowsersPage.Visibility = page == "Browsers" ? Visibility.Visible : Visibility.Collapsed;
            AboutPage.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;

            NavGeneralBtn.Appearance = page == "General" ? ControlAppearance.Secondary : ControlAppearance.Transparent;
            NavBrowsersBtn.Appearance = page == "Browsers" ? ControlAppearance.Secondary : ControlAppearance.Transparent;
            NavAboutBtn.Appearance = page == "About" ? ControlAppearance.Secondary : ControlAppearance.Transparent;

            PageTitleText.Text = page switch
            {
                "Browsers" => L.Get("S_NavBrowsers"),
                "About" => L.Get("S_NavAbout"),
                _ => L.Get("S_NavGeneral")
            };
        }

        private void NavGeneral_Click(object sender, RoutedEventArgs e) => SwitchPage("General");
        private void NavBrowsers_Click(object sender, RoutedEventArgs e) => SwitchPage("Browsers");
        private void NavAbout_Click(object sender, RoutedEventArgs e)
        {
            RefreshDiagnostics();
            SwitchPage("About");
        }

        private void ModeNativeCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && IsInsideControl<ComboBox>(d)) return;
            ModeNativeRadio.IsChecked = true;
        }

        private void ModeDefaultCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && IsInsideControl<ComboBox>(d)) return;
            ModeDefaultRadio.IsChecked = true;
        }

        private void ModePickerCard_Click(object sender, MouseButtonEventArgs e) => ModePickerRadio.IsChecked = true;
        private void ModeClipboardCard_Click(object sender, MouseButtonEventArgs e) => ModeClipboardRadio.IsChecked = true;

        private static bool IsInsideControl<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T) return true;
                d = VisualTreeHelper.GetParent(d);
            }

            return false;
        }

        private void ModeRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi) return;

            if (ModeNativeRadio.IsChecked == true) _cfg.GlobalMode = GlobalMode.NativePassthrough;
            else if (ModeDefaultRadio.IsChecked == true) _cfg.GlobalMode = GlobalMode.DefaultBrowser;
            else if (ModePickerRadio.IsChecked == true) _cfg.GlobalMode = GlobalMode.PickerPopup;
            else if (ModeClipboardRadio.IsChecked == true) _cfg.GlobalMode = GlobalMode.ClipboardOnly;

            if (_cfg.GlobalMode == GlobalMode.DefaultBrowser && _cfg.DefaultBrowser == null &&
                DefaultBrowserCombo.ItemsSource is IList<BrowserEntry> defaultChoices && defaultChoices.Count > 0)
            {
                var preferred = defaultChoices.FirstOrDefault(b =>
                    !string.IsNullOrWhiteSpace(_cfg.OriginalDefaultProgId) &&
                    string.Equals(b.ProgId, _cfg.OriginalDefaultProgId, StringComparison.OrdinalIgnoreCase))
                    ?? defaultChoices[0];
                DefaultBrowserCombo.SelectedItem = preferred;
                if (_cfg.DefaultBrowser != null) return;
            }

            SaveAndFlash();
        }

        private void OriginalProgIdCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi || OriginalProgIdCombo.SelectedItem is not ProgIdChoice choice) return;
            OriginalProgIdCombo.ToolTip = choice.Display;
            if (!string.IsNullOrEmpty(choice.ProgId))
            {
                _cfg.OriginalDefaultProgId = choice.ProgId;
                SaveAndFlash();
            }
        }

        private void DefaultBrowserCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi || DefaultBrowserCombo.SelectedItem is not BrowserEntry entry) return;
            DefaultBrowserCombo.ToolTip = entry.Name;
            _cfg.DefaultBrowser = new BrowserEntry
            {
                Id = entry.Id,
                Name = entry.Name,
                ExecutablePath = entry.ExecutablePath,
                ArgumentsTemplate = entry.ArgumentsTemplate,
                ProgId = entry.ProgId,
                Kind = entry.Kind
            };
            SaveAndFlash();
        }

        private void SettingToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi) return;
            _cfg.ShowClipboardPopup = ShowClipPopupToggle.IsChecked == true;
            _cfg.StripTrackingParams = StripTrackingToggle.IsChecked == true;
            SaveAndFlash();
        }

        private void ChromiumProfilesToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi) return;
            _cfg.ShowChromiumProfiles = ChromiumProfilesToggle.IsChecked == true;
            SaveAndFlash();
            RefreshAllState();
        }

        private void AutoCloseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi || AutoCloseCombo.SelectedItem is not ComboItem<int> item) return;
            _cfg.ClipboardAutoCloseSeconds = item.Value;
            SaveAndFlash();
        }

        private void ModifierCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi || ModifierCombo.SelectedItem is not ComboItem<ModifierKey> item) return;
            _cfg.Modifier = item.Value;
            SaveAndFlash();
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi || ThemeCombo.SelectedItem is not ComboItem<ThemeMode> item) return;
            _cfg.Theme = item.Value;
            UiHelpers.ApplyTheme(this, _cfg.Theme);
            SaveAndFlash();
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi || LanguageCombo.SelectedItem is not ComboItem<LanguageMode> item) return;
            _cfg.Language = item.Value;
            L.Apply(_cfg.Language, this);
            PopulateStaticCombos();
            RefreshAllState();
            SwitchPage(_currentPage);
            SaveAndFlash();
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_registration.TrySnapshot(_cfg))
                {
                    _configService.Save(_cfg);
                }

                _registration.Register();
                RefreshAllState();
                SaveAndFlash();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.Format("S_MsgRegFailed", ex.Message), "LinkToBrowser", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenDefaultApps_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SystemRegistration.OpenDefaultAppsSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.Format("S_MsgOpenDefaultFailed", ex.Message), "LinkToBrowser", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RestoreSystemDefault_Click(object sender, RoutedEventArgs e)
        {
            var matched = _allBrowsers.FirstOrDefault(b =>
                string.Equals(b.ProgId, _cfg.OriginalDefaultProgId, StringComparison.OrdinalIgnoreCase));
            var targetLabel = matched != null
                ? $"{matched.Name} ({_cfg.OriginalDefaultProgId})"
                : _cfg.OriginalDefaultProgId;
            var hint = string.IsNullOrWhiteSpace(_cfg.OriginalDefaultProgId)
                ? L.Get("S_RestoreHintNoTarget")
                : L.Format("S_RestoreHintTarget", targetLabel);

            var res = MessageBox.Show(
                this,
                L.Format("S_RestoreConfirmBody", hint),
                L.Get("S_RestoreConfirmTitle"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Cancel)
            {
                return;
            }

            if (res == MessageBoxResult.Yes)
            {
                _registration.Unregister();
            }

            try
            {
                SystemRegistration.OpenDefaultAppsSettings();
            }
            catch
            {
            }

            RefreshAllState();
        }

        private void RescanBrowsers_Click(object sender, RoutedEventArgs e) => RefreshAllState();

        private void AddCustomBrowser_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CustomBrowserDialog(_cfg.Theme, _selfExe) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.ResultEntry != null)
            {
                _cfg.CustomBrowsers.Add(dlg.ResultEntry);
                SaveAndFlash();
                RefreshAllState();
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveBrowser(((FrameworkElement)sender).Tag as BrowserRowVm, -1);
        private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveBrowser(((FrameworkElement)sender).Tag as BrowserRowVm, 1);

        private void MoveBrowser(BrowserRowVm vm, int delta)
        {
            if (vm == null) return;
            var ids = _allBrowsers.Select(b => b.Id).ToList();
            var idx = ids.FindIndex(id => string.Equals(id, vm.Entry.Id, StringComparison.OrdinalIgnoreCase));
            var targetIdx = idx + delta;
            if (idx < 0 || targetIdx < 0 || targetIdx >= ids.Count) return;

            var tmp = ids[idx];
            ids[idx] = ids[targetIdx];
            ids[targetIdx] = tmp;
            _cfg.BrowserOrder = ids;
            SaveAndFlash();
            RefreshAllState();
        }

        private void ToggleHidden_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not BrowserRowVm vm) return;
            var id = vm.Entry.Id;
            var existingIdx = _cfg.HiddenBrowserIds.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            if (existingIdx >= 0) _cfg.HiddenBrowserIds.RemoveAt(existingIdx);
            else _cfg.HiddenBrowserIds.Add(id);

            SaveAndFlash();
            RefreshAllState();
        }

        private void TestBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not BrowserRowVm vm) return;
            var launcher = new BrowserLauncher(_selfExe);
            if (!launcher.TryLaunch(vm.Entry.ExecutablePath, vm.Entry.ArgumentsTemplate, "https://example.com", out var err))
            {
                MessageBox.Show(this, L.Format("S_MsgTestFailed", err), "LinkToBrowser", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void EditCustom_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not BrowserRowVm vm) return;
            var target = _cfg.CustomBrowsers.FirstOrDefault(c => string.Equals(c.Id, vm.Entry.Id, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            var dlg = new CustomBrowserDialog(_cfg.Theme, _selfExe, target) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.ResultEntry != null)
            {
                target.Name = dlg.ResultEntry.Name;
                target.ExecutablePath = dlg.ResultEntry.ExecutablePath;
                target.ArgumentsTemplate = dlg.ResultEntry.ArgumentsTemplate;

                if (_cfg.DefaultBrowser != null &&
                    string.Equals(_cfg.DefaultBrowser.Id, target.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _cfg.DefaultBrowser = new BrowserEntry
                    {
                        Id = target.Id,
                        Name = target.Name,
                        ExecutablePath = target.ExecutablePath,
                        ArgumentsTemplate = target.ArgumentsTemplate,
                        Kind = BrowserKind.Custom
                    };
                }

                SaveAndFlash();
                RefreshAllState();
            }
        }

        private void DeleteCustom_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not BrowserRowVm vm) return;
            _cfg.CustomBrowsers.RemoveAll(c => string.Equals(c.Id, vm.Entry.Id, StringComparison.OrdinalIgnoreCase));
            _cfg.BrowserOrder.RemoveAll(id => string.Equals(id, vm.Entry.Id, StringComparison.OrdinalIgnoreCase));
            _cfg.HiddenBrowserIds.RemoveAll(id => string.Equals(id, vm.Entry.Id, StringComparison.OrdinalIgnoreCase));
            if (_cfg.DefaultBrowser != null &&
                string.Equals(_cfg.DefaultBrowser.Id, vm.Entry.Id, StringComparison.OrdinalIgnoreCase))
            {
                _cfg.DefaultBrowser = null;
            }
            SaveAndFlash();
            RefreshAllState();
        }

        private void OpenConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(_configService.ConfigPath))
                {
                    _configService.Save(_cfg);
                }

                Process.Start(new ProcessStartInfo(_configService.ConfigPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.Format("S_MsgOpenConfigFailed", ex.Message), "LinkToBrowser", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            _logService.Clear();
            MessageBox.Show(this, L.Get("S_MsgLogsCleared"), "LinkToBrowser", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RefreshDiag_Click(object sender, RoutedEventArgs e)
        {
            RefreshRegistrationBanner();
            RefreshDiagnostics();
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e) => OpenExternalUrl("https://github.com/D-ruihao/LinkToBrowser");

        private void OpenReleases_Click(object sender, RoutedEventArgs e) => OpenExternalUrl("https://github.com/D-ruihao/LinkToBrowser/releases");

        private static void OpenExternalUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}
