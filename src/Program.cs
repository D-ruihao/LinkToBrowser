using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LinkToBrowser.Core;
using LinkToBrowser.Models;

namespace LinkToBrowser
{
    public static class Program
    {
        private const int VkShift = 0x10;
        private const int VkMenu = 0x12;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [STAThread]
        public static int Main(string[] args)
        {
            var selfExe = GetSelfExePath();
            var exeDir = Path.GetDirectoryName(selfExe) ?? AppDomain.CurrentDomain.BaseDirectory;
            var localDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LinkToBrowser");

            var logService = new LogService(Path.Combine(exeDir, "logs"), Path.Combine(localDir, "logs"));
            logService.LogArgs(args);

            var inv = CliParser.Parse(args);
            var configService = new ConfigService(exeDir, localDir);
            var registration = new SystemRegistration(selfExe);

            switch (inv.Kind)
            {
                case CliKind.Register:
                    try
                    {
                        var regCfg = configService.Load();
                        L.Apply(regCfg.Language);
                        registration.Register();
                        if (registration.TrySnapshot(regCfg))
                        {
                            configService.Save(regCfg);
                        }

                        return 0;
                    }
                    catch
                    {
                        return 1;
                    }

                case CliKind.Unregister:
                    try
                    {
                        registration.Unregister();
                        return 0;
                    }
                    catch
                    {
                        return 1;
                    }

                case CliKind.Ignore:
                    return 0;

                case CliKind.OpenSettings:
                    return RunUi(null, configService, logService, registration, selfExe);

                case CliKind.HandleUrl:
                {
                    if (!string.IsNullOrEmpty(registration.GetRegisteredCommand()) && !registration.PathMatchesSelf())
                    {
                        logService.LogWarn($"Registered command does not match current executable: {selfExe}");
                    }

                    var cfg = configService.Load();
                    L.Apply(cfg.Language);
                    var launcher = new BrowserLauncher(selfExe);
                    var dispatcher = new LinkDispatcher(
                        cfg,
                        (exe, template, url) =>
                        {
                            var ok = launcher.TryLaunch(exe, template, url, out var err);
                            return (ok, err);
                        },
                        url => ClipboardService.CopyWithRetry(url),
                        BrowserCatalog.ResolveProgIdCommand,
                        IsModifierKeyDown,
                        cmd =>
                        {
                            var ok = CommandLineHelper.TrySplit(cmd, File.Exists, out var exe, out var template);
                            return (ok, exe, template);
                        },
                        () => RecentBrowserService.FindMostRecentBrowser(cfg, selfExe),
                        (userDataDir, url) =>
                        {
                            var ok = CdpClient.TryOpenTab(userDataDir, url, out var err);
                            return (ok, err);
                        },
                        RecentBrowserService.ActivateWindow,
                        userDataDir => RecentBrowserService.FindWindowByUserDataDir(userDataDir, selfExe));

                    var result = dispatcher.Dispatch(inv);
                    if (result.Kind == DispatchKind.Done)
                    {
                        var ok = result.ExitCode == 0;
                        logService.LogLink(result.Url, ok, ok ? null : result.Warning);
                        return result.ExitCode;
                    }

                    if (result.Kind == DispatchKind.ShowClipboard)
                    {
                        logService.LogLink(result.Url, result.CopyOk, result.Warning);
                    }

                    return RunUi(result, configService, logService, registration, selfExe);
                }

                default:
                    return 0;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunUi(
            DispatchResult dispatchResult,
            ConfigService configService,
            LogService logService,
            SystemRegistration registration,
            string selfExe)
        {
            return App.RunInteractive(dispatchResult, configService, logService, registration, selfExe);
        }

        private static bool IsModifierKeyDown(ModifierKey key)
        {
            return key switch
            {
                ModifierKey.Alt => (GetAsyncKeyState(VkMenu) & 0x8000) != 0,
                ModifierKey.Shift => (GetAsyncKeyState(VkShift) & 0x8000) != 0,
                _ => false
            };
        }

        private static string GetSelfExePath()
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                if (!string.IsNullOrEmpty(proc.MainModule?.FileName))
                {
                    return proc.MainModule.FileName;
                }
            }
            catch
            {
            }

            return Assembly.GetExecutingAssembly().Location;
        }
    }
}
