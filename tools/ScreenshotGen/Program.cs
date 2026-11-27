using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using LinkToBrowser.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace LinkToBrowser.Tools.ScreenshotGen
{
    internal static class Program
    {
        private sealed class Options
        {
            public string OutputDir { get; set; } = string.Empty;
            public string Suffix { get; set; } = "v211";
            public LanguageMode Language { get; set; } = LanguageMode.EnUs;
            public double Dpi { get; set; } = 144.0;
            public HashSet<string> Targets { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "settings",
                "rules"
            };
        }

        private const string DemoExePath = @"C:\Tools\LinkToBrowser\LinkToBrowser.exe";

        [STAThread]
        public static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Console.Error.WriteLine($"Unhandled exception: {e.ExceptionObject}");
            };

            try
            {
                var options = ParseArgs(args);
                if (string.IsNullOrWhiteSpace(options.OutputDir))
                {
                    options.OutputDir = ResolveDefaultOutputDir();
                }

                Directory.CreateDirectory(options.OutputDir);
                GenerateScreenshots(options);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ScreenshotGen failed: {ex}");
                return 1;
            }
        }

        private static Options ParseArgs(string[] args)
        {
            var opts = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    opts.OutputDir = Path.GetFullPath(args[++i]);
                }
                else if (string.Equals(a, "--suffix", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    opts.Suffix = args[++i].Trim();
                }
                else if (string.Equals(a, "--dpi", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                         double.TryParse(args[++i], out var dpi) && dpi >= 72.0 && dpi <= 384.0)
                {
                    opts.Dpi = dpi;
                }
                else if (string.Equals(a, "--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    var lang = args[++i].Trim().ToLowerInvariant();
                    opts.Language = lang.StartsWith("zh") ? LanguageMode.ZhCn : LanguageMode.EnUs;
                }
                else if (string.Equals(a, "--only", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    var raw = args[++i];
                    opts.Targets.Clear();
                    if (string.Equals(raw.Trim(), "all", StringComparison.OrdinalIgnoreCase))
                    {
                        opts.Targets.UnionWith(new[] { "settings", "rules", "picker", "clipboard" });
                    }
                    else
                    {
                        foreach (var item in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var name = item.Trim().ToLowerInvariant();
                            if (name.Length > 0) opts.Targets.Add(name);
                        }
                    }
                }
                else if (string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Usage: ScreenshotGen [--out <dir>] [--suffix <v211>] [--only settings,rules|all] [--lang en|zh] [--dpi 144]");
                    Environment.Exit(0);
                }
            }

            return opts;
        }

        private static string ResolveDefaultOutputDir()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "LinkToBrowser.csproj")))
                {
                    return Path.Combine(dir.FullName, "docs", "screenshots");
                }
                dir = dir.Parent;
            }

            return Path.GetFullPath(Path.Combine("docs", "screenshots"));
        }

        private static void GenerateScreenshots(Options opts)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "LinkToBrowser_ScreenshotGen_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var app = new LinkToBrowser.App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += (_, e) =>
                {
                    Console.Error.WriteLine($"WPF dispatcher error: {e.Exception}");
                    e.Handled = true;
                };

                var cfgSvc = new ConfigService(tempDir, tempDir);
                var logSvc = new LogService(Path.Combine(tempDir, "logs"));
                var reg = new SystemRegistration(DemoExePath);

                var isEn = opts.Language == LanguageMode.EnUs;
                var cfg = new AppConfig
                {
                    GlobalMode = GlobalMode.RecentBrowser,
                    RecentBrowserFallback = RecentFallbackMode.PickerPopup,
                    OriginalDefaultProgId = "MSEdgeHTM",
                    Theme = ThemeMode.Dark,
                    Language = opts.Language,
                    EnableRoutingRules = true,
                    SampleRulesSeeded = true,
                    ClipboardAutoCloseSeconds = 5
                };

                var samples = RuleMatcher.CreateDefaultSamples(isEn);
                if (samples.Count >= 3)
                {
                    samples[1].Enabled = true;
                    samples[2].Enabled = true;
                }
                cfg.Rules = samples;
                cfgSvc.Save(cfg);

                L.Apply(opts.Language);

                if (opts.Targets.Contains("settings") || opts.Targets.Contains("rules"))
                {
                    CaptureSettingsAndRules(opts, cfgSvc, logSvc, reg);
                }

                if (opts.Targets.Contains("picker"))
                {
                    CapturePicker(opts, cfg, cfgSvc, logSvc);
                }

                if (opts.Targets.Contains("clipboard"))
                {
                    CaptureClipboard(opts, cfg, logSvc);
                }

                app.Shutdown();
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void CaptureSettingsAndRules(Options opts, ConfigService cfgSvc, LogService logSvc, SystemRegistration reg)
        {
            var win = new SettingsWindow(cfgSvc, logSvc, reg, DemoExePath);
            PrepareOffscreen(win, width: 920, height: 690);
            win.Show();
            PumpUi(300);

            // Populate demo registration banner
            var regBar = (InfoBar)win.FindName("RegStatusBar");
            regBar.Severity = InfoBarSeverity.Success;
            regBar.Title = L.Get("S_BannerOkTitle");
            regBar.Message = L.Get("S_BannerOkMsg");

            var regSub = (System.Windows.Controls.TextBlock)win.FindName("RegPathSubtitle");
            regSub.Text = L.Format("S_RegSubCurrent", $"\"{DemoExePath}\" \"%1\"");

            var regBtn = (Wpf.Ui.Controls.Button)win.FindName("RegisterBtn");
            regBtn.Content = L.Get("S_RegBtnUpdatePath");
            regBtn.Appearance = ControlAppearance.Secondary;

            var defCombo = (ComboBox)win.FindName("DefaultBrowserCombo");
            if (defCombo.Items.Count > 0 && defCombo.SelectedIndex < 0)
            {
                var edgeIdx = -1;
                for (var i = 0; i < defCombo.Items.Count; i++)
                {
                    if (defCombo.Items[i] is BrowserEntry b &&
                        string.Equals(b.ProgId, "MSEdgeHTM", StringComparison.OrdinalIgnoreCase))
                    {
                        edgeIdx = i;
                        break;
                    }
                }
                defCombo.SelectedIndex = edgeIdx >= 0 ? edgeIdx : 0;
            }

            // Hide transient "Auto-saved" badge for clean capture
            var savedBadge = (System.Windows.Controls.TextBlock)win.FindName("SavedBadgeText");
            savedBadge.Opacity = 0;

            if (opts.Targets.Contains("settings"))
            {
                var path = BuildOutputPath(opts, "settings");
                RenderWindowToPng(win, path, opts.Dpi, isDark: true);
            }

            if (opts.Targets.Contains("rules"))
            {
                win.Height = 640;
                var navRulesBtn = (Wpf.Ui.Controls.Button)win.FindName("NavRulesBtn");
                navRulesBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                var ruleTestBox = (Wpf.Ui.Controls.TextBox)win.FindName("RuleTestBox");
                ruleTestBox.Text = "https://github.com/D-ruihao/LinkToBrowser";
                savedBadge.Opacity = 0;

                var path = BuildOutputPath(opts, "rules");
                RenderWindowToPng(win, path, opts.Dpi, isDark: true);
            }

            SafeClose(win);
        }

        private static void CapturePicker(Options opts, AppConfig baseCfg, ConfigService cfgSvc, LogService logSvc)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
            PumpUi(150);

            var pickerCfg = new AppConfig
            {
                Language = opts.Language,
                Theme = ThemeMode.Dark,
                ShowChromiumProfiles = false,
                EnableCdpForIsolatedBrowsers = false,
                CustomBrowsers = new List<BrowserEntry>
                {
                    new BrowserEntry
                    {
                        Id = "demo:work-chrome",
                        Name = opts.Language == LanguageMode.ZhCn ? "Chrome（工作配置）" : "Chrome (Work Profile)",
                        ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                        ArgumentsTemplate = "--profile-directory=\"Work\" {url}",
                        Kind = BrowserKind.Custom
                    }
                }
            };
            pickerCfg.BrowserOrder.Add("demo:work-chrome");

            var win = new PickerWindow(
                "https://github.com/D-ruihao/LinkToBrowser/releases",
                string.Empty,
                pickerCfg,
                cfgSvc,
                logSvc,
                DemoExePath,
                onOpenSettings: () => { },
                onLaunchFailed: (_, _, _) => { });

            PrepareOffscreen(win, width: win.Width, height: double.NaN);
            win.Show();
            PumpUi(350);
            win.Left = -32000;
            win.Top = -32000;

            var path = BuildOutputPath(opts, "picker");
            RenderWindowToPng(win, path, opts.Dpi, isDark: true);
            SafeClose(win);
        }

        private static void CaptureClipboard(Options opts, AppConfig baseCfg, LogService logSvc)
        {
            var clipCfg = new AppConfig
            {
                Language = opts.Language,
                Theme = ThemeMode.Dark,
                ClipboardAutoCloseSeconds = 5
            };

            var win = new ClipboardWindow(
                new DispatchResult
                {
                    Kind = DispatchKind.ShowClipboard,
                    Url = "https://github.com/D-ruihao/LinkToBrowser",
                    CopyOk = true
                },
                clipCfg,
                logSvc,
                onOpenPicker: _ => { },
                onOpenSettings: () => { });

            PrepareOffscreen(win, width: win.Width, height: double.NaN);
            win.Show();
            PumpUi(300);
            win.Left = -32000;
            win.Top = -32000;

            var path = BuildOutputPath(opts, "clipboard");
            RenderWindowToPng(win, path, opts.Dpi, isDark: true);
            SafeClose(win);
        }

        private static void PrepareOffscreen(Window win, double width, double height)
        {
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.ShowInTaskbar = false;
            win.ShowActivated = false;
            win.Left = -32000;
            win.Top = -32000;
            if (!double.IsNaN(width) && width > 0) win.Width = width;
            if (!double.IsNaN(height) && height > 0) win.Height = height;
        }

        private static void SafeClose(Window win)
        {
            try { SystemThemeWatcher.UnWatch(win); } catch { }
            try { win.Close(); } catch { }
        }

        private static void PumpUi(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void RenderWindowToPng(Window window, string outputPath, double dpi, bool isDark)
        {
            PumpUi(300);
            window.UpdateLayout();

            var width = Math.Max(1, window.ActualWidth);
            var height = Math.Max(1, window.ActualHeight);
            var scale = dpi / 96.0;
            var pixelWidth = (int)Math.Round(width * scale);
            var pixelHeight = (int)Math.Round(height * scale);

            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bgColor = isDark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243);
                dc.DrawRectangle(new SolidColorBrush(bgColor), null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(window), null, new Rect(0, 0, width, height));
            }

            rtb.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(outputPath))
            {
                encoder.Save(fs);
            }

            Console.WriteLine($"Saved {outputPath} ({pixelWidth}x{pixelHeight} @ {dpi:0} DPI)");
        }

        private static string BuildOutputPath(Options opts, string name)
        {
            var fileName = string.IsNullOrWhiteSpace(opts.Suffix)
                ? $"{name}.png"
                : $"{name}-{opts.Suffix}.png";
            return Path.Combine(opts.OutputDir, fileName);
        }
    }
}
