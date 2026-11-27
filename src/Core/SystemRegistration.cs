using System;
using System.Diagnostics;
using System.IO;
using LinkToBrowser.Models;
using Microsoft.Win32;

namespace LinkToBrowser.Core
{
    public sealed class SystemRegistration
    {
        public const string ProgId = "LinkToBrowser.URL";
        private readonly string _selfExePath;

        public SystemRegistration(string selfExePath)
        {
            _selfExePath = NormalizePath(selfExePath);
        }

        public void Register()
        {
            using (var baseKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                baseKey.SetValue(null, "LinkToBrowser URL Handler");
                baseKey.SetValue("URL Protocol", string.Empty);
                baseKey.SetValue("FriendlyTypeName", "LinkToBrowser");

                using (var iconKey = baseKey.CreateSubKey("DefaultIcon"))
                {
                    iconKey.SetValue(null, $"{_selfExePath},0");
                }

                using (var cmdKey = baseKey.CreateSubKey(@"shell\open\command"))
                {
                    cmdKey.SetValue(null, $"\"{_selfExePath}\" \"%1\"");
                }
            }

            using (var capKey = Registry.CurrentUser.CreateSubKey(@"Software\LinkToBrowser\Capabilities"))
            {
                capKey.SetValue("ApplicationName", "LinkToBrowser");
                capKey.SetValue("ApplicationIcon", $"{_selfExePath},0");
                capKey.SetValue("ApplicationDescription", L.Get("S_RegAppDescription"));

                using (var assocKey = capKey.CreateSubKey("URLAssociations"))
                {
                    assocKey.SetValue("http", ProgId);
                    assocKey.SetValue("https", ProgId);
                }
            }

            using (var regApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                regApps.SetValue("LinkToBrowser", @"Software\LinkToBrowser\Capabilities");
            }
        }

        public void Unregister()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\LinkToBrowser", false); } catch { }
            try
            {
                using var regApps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true);
                regApps?.DeleteValue("LinkToBrowser", false);
            }
            catch
            {
            }
        }

        public string GetUserChoice(string scheme)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice");
                return (key?.GetValue("ProgId") as string) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public string GetRegisteredCommand()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
                return (key?.GetValue(null) as string) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool PathMatchesSelf()
        {
            var cmd = GetRegisteredCommand();
            if (!CommandLineHelper.TrySplit(cmd, _ => true, out var exe, out _))
            {
                return false;
            }

            return string.Equals(NormalizePath(exe), _selfExePath, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsSystemDefault()
        {
            return string.Equals(GetUserChoice("http"), ProgId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(GetUserChoice("https"), ProgId, StringComparison.OrdinalIgnoreCase);
        }

        public bool TrySnapshot(AppConfig cfg)
        {
            return TrySnapshotWithResolvers(
                cfg,
                _selfExePath,
                GetUserChoice,
                BrowserCatalog.ResolveProgIdCommand,
                cmd =>
                {
                    var ok = CommandLineHelper.TrySplit(cmd, File.Exists, out var exe, out var args);
                    return (ok, exe, args);
                });
        }

        public static bool TrySnapshotWithResolvers(
            AppConfig cfg,
            string selfExePath,
            Func<string, string> getUserChoice,
            Func<string, string> resolveProgId,
            Func<string, (bool ok, string exe, string args)> splitCommand)
        {
            if (cfg == null)
            {
                return false;
            }

            var normalizedSelf = NormalizePath(selfExePath);
            var progId = getUserChoice("https");
            if (string.IsNullOrWhiteSpace(progId))
            {
                progId = getUserChoice("http");
            }

            if (IsValidTargetProgId(progId, normalizedSelf, resolveProgId, splitCommand))
            {
                if (string.Equals(cfg.OriginalDefaultProgId, progId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                cfg.OriginalDefaultProgId = progId;
                return true;
            }

            if ((string.IsNullOrWhiteSpace(cfg.OriginalDefaultProgId) ||
                 string.Equals(cfg.OriginalDefaultProgId, ProgId, StringComparison.OrdinalIgnoreCase)) &&
                IsValidTargetProgId(AppConfig.DefaultOriginalProgId, normalizedSelf, resolveProgId, splitCommand))
            {
                cfg.OriginalDefaultProgId = AppConfig.DefaultOriginalProgId;
                return true;
            }

            return false;
        }

        private static bool IsValidTargetProgId(
            string progId,
            string normalizedSelf,
            Func<string, string> resolveProgId,
            Func<string, (bool ok, string exe, string args)> splitCommand)
        {
            if (string.IsNullOrWhiteSpace(progId) ||
                string.Equals(progId, ProgId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var cmd = resolveProgId(progId);
            var split = splitCommand(cmd);
            if (!split.ok)
            {
                return false;
            }

            return !string.Equals(NormalizePath(split.exe), normalizedSelf, StringComparison.OrdinalIgnoreCase);
        }

        public static void OpenDefaultAppsSettings()
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path.Trim().Trim('"'));
            }
            catch
            {
                return path.Trim();
            }
        }
    }
}
