using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using LinkToBrowser.Views;
using Wpf.Ui.Controls;
using Xunit;

namespace LinkToBrowser.Tests
{
    public sealed class StaWpfFixture : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly Thread _staThread;
        private int _disposed;

        public StaWpfFixture()
        {
            var ready = new ManualResetEventSlim(false);
            Dispatcher disp = null;
            _staThread = new Thread(() =>
            {
                disp = Dispatcher.CurrentDispatcher;
                if (Application.Current == null)
                {
                    var app = new LinkToBrowser.App();
                    app.InitializeComponent();
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                }
                ready.Set();
                Dispatcher.Run();
            });
            _staThread.SetApartmentState(ApartmentState.STA);
            _staThread.IsBackground = true;
            _staThread.Start();
            ready.Wait();
            _dispatcher = disp;
        }

        public void Run(Action action) => _dispatcher.Invoke(action);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_dispatcher == null || _dispatcher.HasShutdownStarted) return;

            try
            {
                _dispatcher.Invoke((Action)(() =>
                {
                    var app = Application.Current;
                    if (app != null)
                    {
                        for (int i = app.Windows.Count - 1; i >= 0; i--)
                        {
                            var w = app.Windows[i];
                            try { Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(w); } catch { }
                            try { w.Close(); } catch { }
                        }
                        try { app.Shutdown(); } catch { }
                    }
                    _dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                }), TimeSpan.FromSeconds(3));
            }
            catch
            {
                try { _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); } catch { }
            }

            try { _staThread?.Join(3000); } catch { }
        }
    }

    public sealed class UiAndFixTests : IClassFixture<StaWpfFixture>, IDisposable
    {
        private readonly StaWpfFixture _wpf;
        private readonly string _tempDir;

        public UiAndFixTests(StaWpfFixture wpf)
        {
            _wpf = wpf;
            _tempDir = Path.Combine(Path.GetTempPath(), "LinkToBrowserUiTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void ClipboardAutoCloseTracker_StartsUnpausedAndCountsDown_PausesOnlyOnHoverOrUrlFocus()
        {
            var tracker = new ClipboardAutoCloseTracker(copyOk: true, autoCloseSeconds: 3, warning: string.Empty);
            Assert.True(tracker.IsEnabled);
            Assert.False(tracker.IsPaused);
            Assert.Equal(3, tracker.RemainingSeconds);

            Assert.False(tracker.Tick());
            Assert.Equal(2, tracker.RemainingSeconds);

            tracker.SetUrlFocused(true);
            Assert.True(tracker.IsPaused);
            Assert.False(tracker.Tick());
            Assert.Equal(2, tracker.RemainingSeconds);

            tracker.SetUrlFocused(false);
            Assert.False(tracker.IsPaused);
            Assert.False(tracker.Tick());
            Assert.Equal(1, tracker.RemainingSeconds);
            Assert.True(tracker.Tick());
            Assert.Equal(0, tracker.RemainingSeconds);
        }

        [Fact]
        public void Settings_BuildProgIdChoices_WhenOriginalEmpty_SelectsPlaceholderInsteadOfFirstBrowser()
        {
            var browsers = new List<BrowserEntry>
            {
                new BrowserEntry { Name = "Google Chrome", ProgId = "ChromeHTML" },
                new BrowserEntry { Name = "Microsoft Edge", ProgId = "MSEdgeHTM" }
            };

            var (choices, selectedIndex) = SettingsWindow.BuildProgIdChoices(string.Empty, browsers);
            Assert.Equal(0, selectedIndex);
            Assert.Equal(string.Empty, choices[0].ProgId);
            Assert.Equal("ChromeHTML", choices[1].ProgId);

            var (choices2, selectedIndex2) = SettingsWindow.BuildProgIdChoices("MSEdgeHTM", browsers);
            Assert.Equal(1, selectedIndex2);
            Assert.Equal("MSEdgeHTM", choices2[selectedIndex2].ProgId);
        }

        [Fact]
        public void Picker_ShouldLaunchDigitHotkey_OnlyWhenFilterEmptyAndNoModifiers()
        {
            Assert.True(PickerWindow.ShouldLaunchDigitHotkey(Key.D1, ModifierKeys.None, string.Empty, out var digit1));
            Assert.Equal(1, digit1);

            Assert.False(PickerWindow.ShouldLaunchDigitHotkey(Key.D1, ModifierKeys.None, "chr", out _));
            Assert.False(PickerWindow.ShouldLaunchDigitHotkey(Key.D1, ModifierKeys.Control, string.Empty, out _));
        }

        [Fact]
        public void LogService_OpenDirectory_CreatesMissingDirAndQuotesPathsWithSpaces()
        {
            var spacedDir = Path.Combine(_tempDir, "John Doe", "AppData", "LinkToBrowser");
            var svc = new LogService(spacedDir);
            ProcessStartInfo captured = null;

            svc.OpenDirectory(psi => captured = psi);

            Assert.True(Directory.Exists(spacedDir));
            Assert.NotNull(captured);
            Assert.Equal("explorer.exe", captured.FileName);
            Assert.Equal($"\"{spacedDir}\"", captured.Arguments);
        }

        [Fact]
        public void EvaluateRegistrationBanner_WhenUnregisteredWhileUserChoiceStillLinkToBrowser_ReportsPendingRestoreNotOtherPath()
        {
            var banner = SettingsWindow.EvaluateRegistrationBanner(
                isDefault: true,
                isRegistered: false,
                pathMatchesSelf: false,
                httpProg: "LinkToBrowser.URL",
                httpsProg: "LinkToBrowser.URL");

            Assert.Equal(InfoBarSeverity.Warning, banner.Severity);
            Assert.Contains("尚未改回", banner.Title);
            Assert.DoesNotContain("其它位置", banner.Title);
            Assert.Equal("立即注册", banner.ButtonText);
        }

        [Fact]
        public void ClipboardWindow_CloseButtonWithCountdownFitsInsideWindowWithoutClipping()
        {
            string closeText = null;
            double rowDesiredWidth = 0;
            double availableContentWidth = 0;

            _wpf.Run(() =>
            {
                var win = new ClipboardWindow(
                    new DispatchResult { Kind = DispatchKind.ShowClipboard, Url = "https://example.com/a", CopyOk = true },
                    new AppConfig { ClipboardAutoCloseSeconds = 3 },
                    new LogService(_tempDir),
                    _ => { },
                    () => { });

                var closeBtn = (System.Windows.Controls.Button)win.FindName("CloseBtn");
                closeText = closeBtn.Content?.ToString();
                var buttonGrid = (FrameworkElement)closeBtn.Parent;
                buttonGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                rowDesiredWidth = buttonGrid.DesiredSize.Width;
                availableContentWidth = win.Width - 32;
                win.Close();
            });

            Assert.Equal("关闭 (Esc) · 3s", closeText);
            Assert.True(rowDesiredWidth > 0 && rowDesiredWidth <= availableContentWidth,
                $"Button row width {rowDesiredWidth} exceeds available content width {availableContentWidth}");
        }

        [Fact]
        public void ClipboardAutoCloseTracker_Disable_StopsCountdown()
        {
            var tracker = new ClipboardAutoCloseTracker(copyOk: true, autoCloseSeconds: 3, warning: string.Empty);
            Assert.True(tracker.IsEnabled);

            tracker.Disable();

            Assert.False(tracker.IsEnabled);
            Assert.Equal(0, tracker.RemainingSeconds);
            Assert.False(tracker.Tick());
        }

        [Fact]
        public void SettingsWindow_NativeModeCardWithLongProgId_FitsWithinMinWidthWithoutOverflow()
        {
            double availableCardWidth = 0;
            double nativeRowDesiredWidth = 0;
            double origComboWidth = 0;
            double nativeRightEdge = 0;
            double defaultRowDesiredWidth = 0;
            double defaultComboWidth = 0;
            double defaultRightEdge = 0;

            _wpf.Run(() =>
            {
                var cfgSvc = new ConfigService(_tempDir, _tempDir);
                cfgSvc.Save(new AppConfig
                {
                    GlobalMode = GlobalMode.NativePassthrough,
                    OriginalDefaultProgId = "DoubaoHTM.5LMDGO4MI2T2ZBGQTRUD4IP3SU",
                    DefaultBrowser = new BrowserEntry
                    {
                        Id = "long-default",
                        Name = new string('M', 70) + " Long Default Browser",
                        ExecutablePath = @"C:\Program Files\Long\Browser\browser.exe",
                        Kind = BrowserKind.Custom
                    }
                });

                var win = new SettingsWindow(
                    cfgSvc,
                    new LogService(_tempDir),
                    new SystemRegistration(@"D:\Tools\LinkToBrowser.exe"),
                    @"D:\Tools\LinkToBrowser.exe");

                availableCardWidth = win.MinWidth - 196 - 48 - 30 - 18;

                var nativeRadio = (FrameworkElement)win.FindName("ModeNativeRadio");
                var nativeGrid = (FrameworkElement)nativeRadio.Parent;
                var origCombo = (FrameworkElement)win.FindName("OriginalProgIdCombo");
                var nativeRightPanel = (FrameworkElement)origCombo.Parent;

                nativeGrid.Measure(new Size(availableCardWidth, double.PositiveInfinity));
                nativeRowDesiredWidth = nativeGrid.DesiredSize.Width;
                nativeGrid.Arrange(new Rect(0, 0, availableCardWidth, nativeGrid.DesiredSize.Height));
                nativeGrid.UpdateLayout();
                origComboWidth = origCombo.ActualWidth;
                nativeRightEdge = nativeRightPanel.TransformToAncestor(nativeGrid)
                    .Transform(new Point(nativeRightPanel.ActualWidth, 0)).X;

                var defaultRadio = (FrameworkElement)win.FindName("ModeDefaultRadio");
                var defaultGrid = (FrameworkElement)defaultRadio.Parent;
                var defaultCombo = (FrameworkElement)win.FindName("DefaultBrowserCombo");

                defaultGrid.Measure(new Size(availableCardWidth, double.PositiveInfinity));
                defaultRowDesiredWidth = defaultGrid.DesiredSize.Width;
                defaultGrid.Arrange(new Rect(0, 0, availableCardWidth, defaultGrid.DesiredSize.Height));
                defaultGrid.UpdateLayout();
                defaultComboWidth = defaultCombo.ActualWidth;
                defaultRightEdge = defaultCombo.TransformToAncestor(defaultGrid)
                    .Transform(new Point(defaultCombo.ActualWidth, 0)).X;

                win.Close();
            });

            Assert.True(nativeRowDesiredWidth > 0 && nativeRowDesiredWidth <= availableCardWidth,
                $"Native row width {nativeRowDesiredWidth} exceeds min available card width {availableCardWidth}");
            Assert.True(origComboWidth > 0 && nativeRightEdge <= availableCardWidth + 0.5,
                $"Native right panel edge {nativeRightEdge} (combo={origComboWidth}) exceeds {availableCardWidth}");
            Assert.True(defaultRowDesiredWidth > 0 && defaultRowDesiredWidth <= availableCardWidth,
                $"Default row width {defaultRowDesiredWidth} exceeds min available card width {availableCardWidth}");
            Assert.True(defaultComboWidth > 0 && defaultRightEdge <= availableCardWidth + 0.5,
                $"Default combo right edge {defaultRightEdge} (combo={defaultComboWidth}) exceeds {availableCardWidth}");
        }

        [Fact]
        public void SettingsWindow_SelectingDefaultModeWithoutSelection_AutoSelectsPreferredDefaultBrowser()
        {
            BrowserEntry savedDefault = null;
            object comboSelected = null;
            object comboToolTip = null;

            _wpf.Run(() =>
            {
                var cfgSvc = new ConfigService(_tempDir, _tempDir);
                cfgSvc.Save(new AppConfig
                {
                    GlobalMode = GlobalMode.PickerPopup,
                    OriginalDefaultProgId = "CustomMatchProg",
                    DefaultBrowser = null,
                    CustomBrowsers = new List<BrowserEntry>
                    {
                        new BrowserEntry
                        {
                            Id = "custom-match",
                            Name = "Custom Preferred Browser With Long Name",
                            ExecutablePath = @"C:\Dummy\browser.exe",
                            ProgId = "CustomMatchProg",
                            Kind = BrowserKind.Custom
                        }
                    }
                });

                var win = new SettingsWindow(
                    cfgSvc,
                    new LogService(_tempDir),
                    new SystemRegistration(@"D:\Tools\LinkToBrowser.exe"),
                    @"D:\Tools\LinkToBrowser.exe");

                var defaultRadio = (System.Windows.Controls.RadioButton)win.FindName("ModeDefaultRadio");
                var defaultCombo = (ComboBox)win.FindName("DefaultBrowserCombo");
                defaultRadio.IsChecked = true;

                comboSelected = defaultCombo.SelectedItem;
                comboToolTip = defaultCombo.ToolTip;
                savedDefault = cfgSvc.Load().DefaultBrowser;
                win.Close();
            });

            Assert.NotNull(comboSelected);
            Assert.NotNull(savedDefault);
            Assert.Equal("Custom Preferred Browser With Long Name", savedDefault.Name);
            Assert.Equal("Custom Preferred Browser With Long Name", comboToolTip);
        }

        [Fact]
        public void PickerWindow_WithLongWarningAndLongBrowserName_LoadsAndFitsWithinWindowWidth()
        {
            double winWidth = 0;
            double rootDesiredWidth = 0;
            double listDesiredWidth = 0;
            double copyBtnWidth = 0;
            int itemCount = 0;

            _wpf.Run(() =>
            {
                var cfg = new AppConfig
                {
                    CustomBrowsers = new List<BrowserEntry>
                    {
                        new BrowserEntry
                        {
                            Id = "long-picker-item",
                            Name = new string('B', 80) + " Chromium Browser",
                            ExecutablePath = @"C:\VeryLong\Nested\Directory\Path\To\Browser\chrome.exe",
                            ArgumentsTemplate = "--profile-directory=\"Profile 1\" --single-argument {url}",
                            Kind = BrowserKind.Custom
                        }
                    }
                };

                var win = new PickerWindow(
                    "https://example.com/very/long/path/query?param=value",
                    "这是一段较长的警告提示：用于验证 PickerWindow 在出现提示信息时也能正常自适应布局。",
                    cfg,
                    new ConfigService(_tempDir, _tempDir),
                    new LogService(_tempDir),
                    @"D:\Tools\LinkToBrowser.exe",
                    () => { },
                    (_, _, _) => { });

                winWidth = win.Width;
                var root = (FrameworkElement)win.Content;
                var browserList = (System.Windows.Controls.ListBox)win.FindName("BrowserList");
                var copyBtn = (FrameworkElement)win.FindName("CopyBtn");

                root.Measure(new Size(winWidth, win.MaxHeight));
                rootDesiredWidth = root.DesiredSize.Width;
                listDesiredWidth = browserList.DesiredSize.Width;
                root.Arrange(new Rect(0, 0, winWidth, root.DesiredSize.Height));
                root.UpdateLayout();

                copyBtnWidth = copyBtn.ActualWidth;
                itemCount = browserList.Items.Count;
                win.Close();
            });

            Assert.True(itemCount >= 1);
            Assert.True(copyBtnWidth > 0);
            Assert.True(rootDesiredWidth > 0 && rootDesiredWidth <= winWidth + 0.5,
                $"Picker root desired width {rootDesiredWidth} exceeds {winWidth}");
            Assert.True(listDesiredWidth <= winWidth,
                $"Picker list desired width {listDesiredWidth} exceeds {winWidth}");
        }

        [Fact]
        public void CustomBrowserDialog_ButtonsFitWithinWindowWidth()
        {
            double winWidth = 0;
            double rootDesiredWidth = 0;
            var buttonWidths = new List<double>();
            double maxRightEdge = 0;

            _wpf.Run(() =>
            {
                var win = new CustomBrowserDialog(ThemeMode.Light, @"D:\Tools\LinkToBrowser.exe");
                winWidth = win.Width;
                var root = (FrameworkElement)win.Content;

                root.Measure(new Size(winWidth, double.PositiveInfinity));
                rootDesiredWidth = root.DesiredSize.Width;
                root.Arrange(new Rect(0, 0, winWidth, root.DesiredSize.Height));
                root.UpdateLayout();

                foreach (var btn in FindVisualChildren<System.Windows.Controls.Button>(root))
                {
                    if (btn.Visibility == Visibility.Visible)
                    {
                        buttonWidths.Add(btn.ActualWidth);
                        var rightEdge = btn.TransformToAncestor(root).Transform(new Point(btn.ActualWidth, 0)).X;
                        if (rightEdge > maxRightEdge) maxRightEdge = rightEdge;
                    }
                }

                win.Close();
            });

            Assert.True(buttonWidths.Count >= 4);
            Assert.All(buttonWidths, w => Assert.True(w > 0));
            Assert.True(rootDesiredWidth > 0 && rootDesiredWidth <= winWidth + 0.5,
                $"CustomBrowserDialog root desired width {rootDesiredWidth} exceeds {winWidth}");
            Assert.True(maxRightEdge <= winWidth + 0.5,
                $"CustomBrowserDialog button right edge {maxRightEdge} exceeds {winWidth}");
        }

        [Fact]
        public void LocalizationService_SwitchesBetweenChineseAndEnglish()
        {
            try
            {
                L.Apply(LanguageMode.EnUs);
                Assert.True(L.IsEnglish);
                var banner = SettingsWindow.EvaluateRegistrationBanner(
                    isDefault: true,
                    isRegistered: true,
                    pathMatchesSelf: true,
                    httpProg: "LinkToBrowser.URL",
                    httpsProg: "LinkToBrowser.URL");
                Assert.Contains("Handling", banner.Title);

                L.Apply(LanguageMode.ZhCn);
                Assert.False(L.IsEnglish);
                var zhBanner = SettingsWindow.EvaluateRegistrationBanner(
                    isDefault: true,
                    isRegistered: true,
                    pathMatchesSelf: true,
                    httpProg: "LinkToBrowser.URL",
                    httpsProg: "LinkToBrowser.URL");
                Assert.Contains("已接管", zhBanner.Title);
            }
            finally
            {
                L.Apply(LanguageMode.ZhCn);
            }
        }

        [Fact]
        public void ClipboardWindow_EnglishMode_CloseButtonWithCountdownFitsInsideWindowWithoutClipping()
        {
            string closeText = null;
            double rowDesiredWidth = 0;
            double availableContentWidth = 0;

            try
            {
                _wpf.Run(() =>
                {
                    var win = new ClipboardWindow(
                        new DispatchResult { Kind = DispatchKind.ShowClipboard, Url = "https://example.com/a", CopyOk = true },
                        new AppConfig { ClipboardAutoCloseSeconds = 3, Language = LanguageMode.EnUs },
                        new LogService(_tempDir),
                        _ => { },
                        () => { });

                    var closeBtn = (System.Windows.Controls.Button)win.FindName("CloseBtn");
                    closeText = closeBtn.Content?.ToString();
                    var buttonGrid = (FrameworkElement)closeBtn.Parent;
                    buttonGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    rowDesiredWidth = buttonGrid.DesiredSize.Width;
                    availableContentWidth = win.Width - 32;
                    win.Close();
                });

                Assert.Equal("Close (Esc) · 3s", closeText);
                Assert.True(rowDesiredWidth > 0 && rowDesiredWidth <= availableContentWidth,
                    $"English button row width {rowDesiredWidth} exceeds available content width {availableContentWidth}");
            }
            finally
            {
                _wpf.Run(() => L.Apply(LanguageMode.ZhCn));
            }
        }

        [Fact]
        public void RuleDialog_PatternBoxIsMultiline_AndButtonsFitWithinWindowWidth()
        {
            bool acceptsReturn = false;
            double winWidth = 0;
            double rootDesiredWidth = 0;
            double maxRightEdge = 0;

            _wpf.Run(() =>
            {
                var win = new RuleDialog(ThemeMode.Dark, new[]
                {
                    new BrowserEntry { Id = "edge", Name = "Microsoft Edge", ExecutablePath = @"C:\msedge.exe" }
                });
                winWidth = win.Width;
                var patternBox = (System.Windows.Controls.TextBox)win.FindName("PatternBox");
                acceptsReturn = patternBox.AcceptsReturn;

                var root = (FrameworkElement)win.Content;
                root.Measure(new Size(winWidth, double.PositiveInfinity));
                rootDesiredWidth = root.DesiredSize.Width;
                root.Arrange(new Rect(0, 0, winWidth, root.DesiredSize.Height));
                root.UpdateLayout();

                foreach (var btn in FindVisualChildren<System.Windows.Controls.Button>(root))
                {
                    if (btn.Visibility == Visibility.Visible)
                    {
                        var rightEdge = btn.TransformToAncestor(root).Transform(new Point(btn.ActualWidth, 0)).X;
                        if (rightEdge > maxRightEdge) maxRightEdge = rightEdge;
                    }
                }

                win.Close();
            });

            Assert.True(acceptsReturn);
            Assert.True(rootDesiredWidth > 0 && rootDesiredWidth <= winWidth + 0.5,
                $"RuleDialog root desired width {rootDesiredWidth} exceeds {winWidth}");
            Assert.True(maxRightEdge <= winWidth + 0.5,
                $"RuleDialog button right edge {maxRightEdge} exceeds {winWidth}");
        }

        [Fact]
        public void UiHelpers_MixedDpiPlacement_ClampsInsideSecondaryMonitorPhysicalBounds()
        {
            // Secondary 100% monitor located to the right of a 150% primary monitor (X = 2560..4480, Y = 0..1040)
            var secondaryWorkArea = new System.Drawing.Rectangle(2560, 0, 1920, 1040);
            var cursorNearRightEdge = new System.Drawing.Point(4400, 950);

            var (leftPx, topPx) = UiHelpers.ComputePlacementNearCursorPx(
                cursorNearRightEdge,
                secondaryWorkArea,
                scaleX: 1.0,
                scaleY: 1.0,
                widthDip: 450,
                heightDip: 360);

            Assert.InRange(leftPx, secondaryWorkArea.Left + 8, secondaryWorkArea.Right - 450);
            Assert.InRange(topPx, secondaryWorkArea.Top + 8, secondaryWorkArea.Bottom - 360);

            var (centerLeft, centerTop) = UiHelpers.ComputeCenteredOnScreenPx(
                secondaryWorkArea,
                scaleX: 1.25,
                scaleY: 1.25,
                widthDip: 400,
                heightDip: 200);
            Assert.Equal(2560 + (1920 - 500) / 2, centerLeft);
            Assert.Equal((1040 - 250) / 2, centerTop);
        }

        [Fact]
        public void PickerWindow_SaveRememberDomainRule_InsertsAtTopAndDeduplicatesSameDomain()
        {
            var cfgSvc = new ConfigService(_tempDir, _tempDir);
            var cfg = new AppConfig
            {
                EnableRoutingRules = false,
                Rules =
                {
                    new RoutingRule
                    {
                        Id = "r-broad",
                        Enabled = true,
                        Name = "Broad Wildcard",
                        MatchKind = RuleMatchKind.Wildcard,
                        Pattern = "*github*",
                        Action = RuleActionKind.ClipboardOnly
                    }
                }
            };
            cfgSvc.Save(cfg);

            var edge = new BrowserEntry { Id = "edge", Name = "Microsoft Edge", ExecutablePath = @"C:\msedge.exe" };
            var created = PickerWindow.SaveRememberDomainRule(cfg, cfgSvc, "https://github.com/D-ruihao/LinkToBrowser", edge);

            Assert.NotNull(created);
            var reloaded = cfgSvc.Load();
            Assert.True(reloaded.EnableRoutingRules);
            Assert.Equal(2, reloaded.Rules.Count);
            Assert.Equal("github.com", reloaded.Rules[0].Pattern);
            Assert.Equal("Microsoft Edge", reloaded.Rules[0].TargetBrowser.Name);
            Assert.Equal("r-broad", reloaded.Rules[1].Id);

            // Remembering same domain again with Chrome replaces top entry instead of duplicating
            var chrome = new BrowserEntry { Id = "chrome", Name = "Google Chrome", ExecutablePath = @"C:\chrome.exe" };
            PickerWindow.SaveRememberDomainRule(reloaded, cfgSvc, "https://github.com/other", chrome);
            var reloaded2 = cfgSvc.Load();
            Assert.Equal(2, reloaded2.Rules.Count);
            Assert.Equal("Google Chrome", reloaded2.Rules[0].TargetBrowser.Name);
        }

        [Fact]
        public void SettingsWindow_RemoveCustomBrowserFromConfig_DisablesReferencingRulesAndClearsTarget()
        {
            var custom = new BrowserEntry
            {
                Id = "custom:work",
                Name = "Work Browser",
                ExecutablePath = @"C:\work.exe",
                Kind = BrowserKind.Custom
            };
            var cfg = new AppConfig
            {
                DefaultBrowser = custom,
                CustomBrowsers = { custom },
                Rules =
                {
                    new RoutingRule
                    {
                        Id = "r1",
                        Enabled = true,
                        Pattern = "corp.local",
                        Action = RuleActionKind.OpenTarget,
                        TargetBrowser = custom
                    },
                    new RoutingRule
                    {
                        Id = "r2",
                        Enabled = true,
                        Pattern = "github.com",
                        Action = RuleActionKind.ClipboardOnly
                    }
                }
            };

            var affected = SettingsWindow.RemoveCustomBrowserFromConfig(cfg, custom.Id);
            Assert.Equal(1, affected);
            Assert.Empty(cfg.CustomBrowsers);
            Assert.Null(cfg.DefaultBrowser);
            Assert.False(cfg.Rules[0].Enabled);
            Assert.Null(cfg.Rules[0].TargetBrowser);
            Assert.True(cfg.Rules[1].Enabled);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) yield return typed;
                foreach (var descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
