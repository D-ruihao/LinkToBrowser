using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using LinkToBrowser.Models;

namespace LinkToBrowser.Core
{
    public sealed class WindowCandidateInfo
    {
        public IntPtr HWnd { get; set; }
        public int ProcessId { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string CommandLine { get; set; } = string.Empty;
        public bool IsMinimized { get; set; }
        public bool IsToolWindow { get; set; }
        public bool HasOwner { get; set; }
    }

    public sealed class RecentBrowserTarget
    {
        public IntPtr HWnd { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
        public string CommandLine { get; set; } = string.Empty;
        public string ArgumentsTemplate { get; set; } = string.Empty;
        public string UserDataDir { get; set; } = string.Empty;
        public string ProfileDirectory { get; set; } = string.Empty;
        public bool HasDevToolsActivePort { get; set; }
        public bool IsMinimized { get; set; }
    }

    public static class RecentBrowserService
    {
        private const int GwlExStyle = -20;
        private const int WsExToolWindow = 0x00000080;
        private const uint GwOwner = 4;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int SwRestore = 9;
        private const int AsfwAny = -1;

        private static readonly HashSet<string> StandardBrowserExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome.exe",
            "msedge.exe",
            "firefox.exe",
            "brave.exe",
            "vivaldi.exe",
            "opera.exe",
            "browser.exe",
            "arc.exe",
            "quark.exe",
            "360chrome.exe",
            "360se.exe",
            "qqbrowser.exe",
            "waterfox.exe",
            "librewolf.exe",
            "floorp.exe",
            "zen.exe",
            "tor.exe",
            "doubao.exe"
        };

        private static readonly HashSet<string> NonBrowserDesktopExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "msedgewebview2.exe",
            "webview2.exe",
            "code.exe",
            "cursor.exe",
            "antigravity.exe",
            "weixin.exe",
            "wechat.exe",
            "qq.exe",
            "feishu.exe",
            "lark.exe",
            "discord.exe",
            "slack.exe",
            "teams.exe",
            "ms-teams.exe",
            "obsidian.exe",
            "notion.exe",
            "postman.exe",
            "clash-verge.exe",
            "比特浏览器.exe",
            "adspower.exe",
            "hubstudio.exe",
            "xl_ext_chrome.exe",
            "extension-host.exe"
        };

        private static readonly string[] EmbeddedCommandMarkers =
        {
            "--embedded-browser-webview",
            "--type=",
            "chrome-extension://",
            "--parent-window=",
            "--app-id="
        };

        public static RecentBrowserTarget FindMostRecentBrowser(AppConfig cfg, string selfExePath)
        {
            var browsers = BrowserCatalog.GetAll(cfg, selfExePath, includeHidden: false);
            var candidates = CaptureTopWindowsZOrder(browsers, selfExePath);
            return FindMostRecentBrowserFromCandidates(
                candidates,
                browsers,
                selfExePath,
                CdpClient.HasDevToolsActivePort);
        }

        public static List<BrowserEntry> GetRunningIsolatedBrowsers(
            AppConfig cfg,
            string selfExePath,
            bool includeHidden)
        {
            if (cfg != null && !cfg.EnableCdpForIsolatedBrowsers)
            {
                return new List<BrowserEntry>();
            }

            var browsers = BrowserCatalog.GetAll(cfg, selfExePath, includeHidden: true);
            var candidates = CaptureTopWindowsZOrder(browsers, selfExePath);
            var running = GetRunningIsolatedBrowsersFromCandidates(
                candidates,
                browsers,
                selfExePath,
                CdpClient.HasDevToolsActivePort);

            if (!includeHidden && cfg?.HiddenBrowserIds != null && cfg.HiddenBrowserIds.Count > 0)
            {
                var hidden = new HashSet<string>(cfg.HiddenBrowserIds, StringComparer.OrdinalIgnoreCase);
                running.RemoveAll(b => hidden.Contains(b.Id));
            }

            return running;
        }

        public static List<BrowserEntry> GetRunningIsolatedBrowsersFromCandidates(
            IReadOnlyList<WindowCandidateInfo> zOrderWindows,
            IReadOnlyList<BrowserEntry> knownBrowsers,
            string selfExePath,
            Func<string, bool> hasDevToolsPort)
        {
            var result = new List<BrowserEntry>();
            if (zOrderWindows == null || zOrderWindows.Count == 0)
            {
                return result;
            }

            var knownList = knownBrowsers ?? Array.Empty<BrowserEntry>();
            var knownExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in knownList)
            {
                var norm = NormalizePath(b.ExecutablePath);
                if (!string.IsNullOrEmpty(norm))
                {
                    knownExes.Add(norm);
                }
            }

            var normalizedSelf = NormalizePath(selfExePath);
            var seenUserDataDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var win in zOrderWindows)
            {
                if (win == null || win.HasOwner || win.IsToolWindow)
                {
                    continue;
                }

                if (!IsBrowserWindowCandidate(win.ExecutablePath, win.ClassName, win.CommandLine, normalizedSelf, knownExes))
                {
                    continue;
                }

                if (!TryExtractFlagValue(win.CommandLine, "user-data-dir", out var userDataDir) ||
                    string.IsNullOrWhiteSpace(userDataDir))
                {
                    continue;
                }

                if (!(hasDevToolsPort?.Invoke(userDataDir) ?? false))
                {
                    continue;
                }

                if (!seenUserDataDirs.Add(userDataDir))
                {
                    continue;
                }

                var normExe = NormalizePath(win.ExecutablePath);
                var displayName = FormatRunningWindowName(normExe, win.WindowTitle, userDataDir);
                result.Add(new BrowserEntry
                {
                    Id = $"cdp:{userDataDir.Trim().ToLowerInvariant()}",
                    Name = displayName,
                    ExecutablePath = normExe,
                    ArgumentsTemplate = $"--user-data-dir=\"{userDataDir}\" {{url}}",
                    UserDataDir = userDataDir,
                    Kind = BrowserKind.RunningWindow
                });
            }

            return result;
        }

        public static IntPtr FindWindowByUserDataDir(string userDataDir, string selfExePath)
        {
            if (string.IsNullOrWhiteSpace(userDataDir))
            {
                return IntPtr.Zero;
            }

            var candidates = CaptureTopWindowsZOrder(Array.Empty<BrowserEntry>(), selfExePath);
            foreach (var win in candidates)
            {
                if (TryExtractFlagValue(win.CommandLine, "user-data-dir", out var udd) &&
                    string.Equals(udd, userDataDir, StringComparison.OrdinalIgnoreCase))
                {
                    return win.HWnd;
                }
            }

            return IntPtr.Zero;
        }

        private static string FormatRunningWindowName(string exePath, string windowTitle, string userDataDir)
        {
            var exeName = Path.GetFileNameWithoutExtension(exePath);
            var title = (windowTitle ?? string.Empty).Trim();
            if (title.Length > 36)
            {
                title = title.Substring(0, 35) + "…";
            }

            if (!string.IsNullOrEmpty(title))
            {
                return $"{exeName} · {title}";
            }

            var folderName = Path.GetFileName(userDataDir.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(folderName) ? exeName : $"{exeName} · {folderName}";
        }

        public static RecentBrowserTarget FindMostRecentBrowserFromCandidates(
            IReadOnlyList<WindowCandidateInfo> zOrderWindows,
            IReadOnlyList<BrowserEntry> knownBrowsers,
            string selfExePath,
            Func<string, bool> hasDevToolsPort)
        {
            if (zOrderWindows == null || zOrderWindows.Count == 0)
            {
                return null;
            }

            var knownList = knownBrowsers ?? Array.Empty<BrowserEntry>();
            var knownExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in knownList)
            {
                var norm = NormalizePath(b.ExecutablePath);
                if (!string.IsNullOrEmpty(norm))
                {
                    knownExes.Add(norm);
                }
            }

            var normalizedSelf = NormalizePath(selfExePath);

            foreach (var win in zOrderWindows)
            {
                if (win == null || win.HasOwner || win.IsToolWindow)
                {
                    continue;
                }

                if (!IsBrowserWindowCandidate(win.ExecutablePath, win.ClassName, win.CommandLine, normalizedSelf, knownExes))
                {
                    continue;
                }

                var normExe = NormalizePath(win.ExecutablePath);
                TryExtractFlagValue(win.CommandLine, "user-data-dir", out var userDataDir);
                TryExtractFlagValue(win.CommandLine, "profile-directory", out var profileDir);

                var matchedEntry = MatchBrowserEntry(knownList, normExe, userDataDir, profileDir);
                var argsTemplate = ResolveLaunchTemplate(matchedEntry, userDataDir, profileDir);
                var hasCdp = !string.IsNullOrEmpty(userDataDir) && (hasDevToolsPort?.Invoke(userDataDir) ?? false);

                return new RecentBrowserTarget
                {
                    HWnd = win.HWnd,
                    ExecutablePath = normExe,
                    CommandLine = win.CommandLine ?? string.Empty,
                    ArgumentsTemplate = argsTemplate,
                    UserDataDir = userDataDir ?? string.Empty,
                    ProfileDirectory = profileDir ?? string.Empty,
                    HasDevToolsActivePort = hasCdp,
                    IsMinimized = win.IsMinimized
                };
            }

            return null;
        }

        public static bool IsBrowserWindowCandidate(
            string executablePath,
            string className,
            string commandLine,
            string normalizedSelfExe,
            ISet<string> knownBrowserExes)
        {
            var normExe = NormalizePath(executablePath);
            if (string.IsNullOrEmpty(normExe))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(normalizedSelfExe) &&
                string.Equals(normExe, normalizedSelfExe, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fileName = Path.GetFileName(normExe);
            if (NonBrowserDesktopExeNames.Contains(fileName))
            {
                return false;
            }

            var cmd = commandLine ?? string.Empty;
            foreach (var marker in EmbeddedCommandMarkers)
            {
                if (cmd.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            if (knownBrowserExes != null && knownBrowserExes.Contains(normExe))
            {
                return true;
            }

            if (StandardBrowserExeNames.Contains(fileName))
            {
                return true;
            }

            var isBrowserClass =
                string.Equals(className, "Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "Chrome_WidgetWin_0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "MozillaWindowClass", StringComparison.OrdinalIgnoreCase);

            if (isBrowserClass && TryExtractFlagValue(cmd, "user-data-dir", out var udd) && !string.IsNullOrWhiteSpace(udd))
            {
                return true;
            }

            return false;
        }

        public static bool TryExtractFlagValue(string commandLine, string flagName, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(commandLine) || string.IsNullOrWhiteSpace(flagName))
            {
                return false;
            }

            var pattern = $@"(?:^|\s)--{Regex.Escape(flagName)}(?:=|\s+)(?:""([^""]+)""|(\S+))";
            var match = Regex.Match(commandLine, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            value = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim();
            return value.Length > 0;
        }

        private static BrowserEntry MatchBrowserEntry(
            IReadOnlyList<BrowserEntry> knownList,
            string normExe,
            string userDataDir,
            string profileDir)
        {
            if (!string.IsNullOrEmpty(userDataDir))
            {
                var byUdd = knownList.FirstOrDefault(b =>
                    string.Equals(NormalizePath(b.ExecutablePath), normExe, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(b.ArgumentsTemplate) &&
                    b.ArgumentsTemplate.IndexOf(userDataDir, StringComparison.OrdinalIgnoreCase) >= 0);
                if (byUdd != null)
                {
                    return byUdd;
                }
            }

            if (!string.IsNullOrEmpty(profileDir))
            {
                var byProfile = knownList.FirstOrDefault(b =>
                    string.Equals(NormalizePath(b.ExecutablePath), normExe, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(b.ArgumentsTemplate) &&
                    b.ArgumentsTemplate.IndexOf(profileDir, StringComparison.OrdinalIgnoreCase) >= 0);
                if (byProfile != null)
                {
                    return byProfile;
                }
            }

            if (!string.IsNullOrEmpty(userDataDir) || !string.IsNullOrEmpty(profileDir))
            {
                return null;
            }

            return knownList.FirstOrDefault(b =>
                string.Equals(NormalizePath(b.ExecutablePath), normExe, StringComparison.OrdinalIgnoreCase) &&
                b.Kind != BrowserKind.ChromiumProfile)
                ?? knownList.FirstOrDefault(b =>
                    string.Equals(NormalizePath(b.ExecutablePath), normExe, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveLaunchTemplate(BrowserEntry matchedEntry, string userDataDir, string profileDir)
        {
            if (matchedEntry != null && matchedEntry.Kind == BrowserKind.Custom && !string.IsNullOrWhiteSpace(matchedEntry.ArgumentsTemplate))
            {
                return matchedEntry.ArgumentsTemplate;
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(userDataDir))
            {
                parts.Add($"--user-data-dir=\"{userDataDir}\"");
            }

            if (!string.IsNullOrWhiteSpace(profileDir))
            {
                parts.Add($"--profile-directory=\"{profileDir}\"");
            }

            if (parts.Count > 0)
            {
                parts.Add("{url}");
                return string.Join(" ", parts);
            }

            return matchedEntry?.ArgumentsTemplate ?? string.Empty;
        }

        public static void ActivateWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SwRestore);
                }

                AllowSetForegroundWindow(AsfwAny);
                SetForegroundWindow(hWnd);
            }
            catch
            {
            }
        }

        private static List<WindowCandidateInfo> CaptureTopWindowsZOrder(IReadOnlyList<BrowserEntry> knownBrowsers, string selfExePath)
        {
            var results = new List<WindowCandidateInfo>();
            var knownExes = new HashSet<string>(
                knownBrowsers.Select(b => NormalizePath(b.ExecutablePath)).Where(p => !string.IsNullOrEmpty(p)),
                StringComparer.OrdinalIgnoreCase);
            var normalizedSelf = NormalizePath(selfExePath);
            var cmdCache = new Dictionary<int, string>();

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd) || GetWindowTextLength(hWnd) == 0)
                {
                    return true;
                }

                var hasOwner = GetWindow(hWnd, GwOwner) != IntPtr.Zero;
                var isToolWindow = (GetWindowLong(hWnd, GwlExStyle) & WsExToolWindow) != 0;
                if (hasOwner || isToolWindow)
                {
                    return true;
                }

                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == 0)
                {
                    return true;
                }

                var exePath = QueryProcessExePath(pid);
                if (string.IsNullOrEmpty(exePath) || string.Equals(NormalizePath(exePath), normalizedSelf, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var fileName = Path.GetFileName(exePath);
                if (NonBrowserDesktopExeNames.Contains(fileName))
                {
                    return true;
                }

                var sb = new StringBuilder(128);
                GetClassName(hWnd, sb, sb.Capacity);
                var cls = sb.ToString();

                var mightBeBrowser =
                    knownExes.Contains(NormalizePath(exePath)) ||
                    StandardBrowserExeNames.Contains(fileName) ||
                    string.Equals(cls, "Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cls, "Chrome_WidgetWin_0", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cls, "MozillaWindowClass", StringComparison.OrdinalIgnoreCase);

                if (!mightBeBrowser)
                {
                    return true;
                }

                if (!cmdCache.TryGetValue((int)pid, out var cmd))
                {
                    cmd = QueryProcessCommandLine((int)pid);
                    cmdCache[(int)pid] = cmd;
                }

                var titleLen = GetWindowTextLength(hWnd);
                var titleSb = new StringBuilder(titleLen + 1);
                GetWindowText(hWnd, titleSb, titleSb.Capacity);

                results.Add(new WindowCandidateInfo
                {
                    HWnd = hWnd,
                    ProcessId = (int)pid,
                    ClassName = cls,
                    WindowTitle = titleSb.ToString(),
                    ExecutablePath = exePath,
                    CommandLine = cmd,
                    IsMinimized = IsIconic(hWnd),
                    IsToolWindow = isToolWindow,
                    HasOwner = hasOwner
                });
                return true;
            }, IntPtr.Zero);

            return results;
        }

        private static string QueryProcessExePath(uint pid)
        {
            var handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
                if (handle == IntPtr.Zero)
                {
                    return string.Empty;
                }

                var sb = new StringBuilder(1024);
                var size = sb.Capacity;
                return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString(0, size) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
        }

        private static string QueryProcessCommandLine(int pid)
        {
            try
            {
                using var mo = new ManagementObject($"Win32_Process.Handle='{pid}'");
                return mo["CommandLine"] as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
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

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
