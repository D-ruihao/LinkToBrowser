using System;
using System.Windows;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using LinkToBrowser.Views;

namespace LinkToBrowser
{
    public partial class App : Application
    {
        private static bool _isRunning;

        public static int RunInteractive(
            DispatchResult dispatchResult,
            ConfigService configService,
            LogService logService,
            SystemRegistration registration,
            string selfExe)
        {
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var cfg = configService.Load();

            if (dispatchResult == null)
            {
                OpenSettingsSingleInstance(app, configService, logService, registration, selfExe);
                return 0;
            }

            if (dispatchResult.Kind == DispatchKind.ShowClipboard)
            {
                ShowClipboardWindow(app, dispatchResult, cfg, configService, logService, registration, selfExe);
                return 0;
            }

            ShowPickerWindow(app, dispatchResult.Url, dispatchResult.Warning, cfg, configService, logService, registration, selfExe);
            return 0;
        }

        internal static void OpenSettingsSingleInstance(
            App app,
            ConfigService configService,
            LogService logService,
            SystemRegistration registration,
            string selfExe)
        {
            var host = new SingleInstanceSettingsHost();
            SettingsWindow window = null;

            if (!host.TryAcquirePrimary(() =>
            {
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (window == null)
                    {
                        return;
                    }

                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }

                    window.Show();
                    window.Activate();
                    window.Topmost = true;
                    window.Topmost = false;
                }));
            }))
            {
                host.Dispose();
                if (!_isRunning)
                {
                    app.Shutdown();
                }

                return;
            }

            window = new SettingsWindow(configService, logService, registration, selfExe);
            window.Closed += (_, _) =>
            {
                host.Dispose();
                if (app.Windows.Count == 0)
                {
                    app.Shutdown();
                }
            };

            app.MainWindow = window;
            window.Show();

            if (!_isRunning)
            {
                _isRunning = true;
                app.Run();
            }
        }

        internal static void ShowPickerWindow(
            App app,
            string url,
            string warning,
            AppConfig cfg,
            ConfigService configService,
            LogService logService,
            SystemRegistration registration,
            string selfExe)
        {
            var picker = new PickerWindow(
                url,
                warning,
                cfg,
                configService,
                logService,
                selfExe,
                onOpenSettings: () =>
                {
                    OpenSettingsSingleInstance(app, configService, logService, registration, selfExe);
                },
                onLaunchFailed: (failedUrl, err, copyOk) =>
                {
                    var fallbackResult = new DispatchResult
                    {
                        Kind = DispatchKind.ShowClipboard,
                        Url = failedUrl,
                        CopyOk = copyOk,
                        Warning = $"启动浏览器失败，已复制到剪贴板：{err}"
                    };
                    ShowClipboardWindow(app, fallbackResult, cfg, configService, logService, registration, selfExe);
                });

            picker.Closed += (_, _) =>
            {
                if (app.Windows.Count == 0)
                {
                    app.Shutdown();
                }
            };

            app.MainWindow = picker;
            picker.Show();

            if (!_isRunning)
            {
                _isRunning = true;
                app.Run();
            }
        }

        internal static void ShowClipboardWindow(
            App app,
            DispatchResult dispatchResult,
            AppConfig cfg,
            ConfigService configService,
            LogService logService,
            SystemRegistration registration,
            string selfExe)
        {
            var clipWindow = new ClipboardWindow(
                dispatchResult,
                cfg,
                logService,
                onOpenPicker: url =>
                {
                    ShowPickerWindow(app, url, string.Empty, cfg, configService, logService, registration, selfExe);
                },
                onOpenSettings: () =>
                {
                    OpenSettingsSingleInstance(app, configService, logService, registration, selfExe);
                });

            clipWindow.Closed += (_, _) =>
            {
                if (app.Windows.Count == 0)
                {
                    app.Shutdown();
                }
            };

            app.MainWindow = clipWindow;
            clipWindow.Show();

            if (!_isRunning)
            {
                _isRunning = true;
                app.Run();
            }
        }
    }
}
