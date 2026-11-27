using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LinkToBrowser.Core
{
    public sealed class BrowserLauncher
    {
        private const int MaxCommandLineChars = 32000;
        private const int AsfwAny = -1;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        private static readonly Regex LegacyPlaceholderRegex = new Regex(@"%(1|L)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SingleArgumentTokenRegex = new Regex(@"(^|\s)(--single-argument\s+)(?:""\{url\}""|\{url\})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SingleArgumentTailRegex = new Regex(@"(^|\s)--single-argument$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UrlTokenRegex = new Regex("\"\\{url\\}\"|\\{url\\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly string _selfExePath;
        private readonly Func<ProcessStartInfo, bool> _start;
        private readonly Func<string, bool> _fileExists;

        public BrowserLauncher(string selfExePath, Func<ProcessStartInfo, bool> start = null, Func<string, bool> fileExists = null)
        {
            _selfExePath = NormalizeFullPath(selfExePath);
            _start = start ?? (psi =>
            {
                try { AllowSetForegroundWindow(AsfwAny); } catch { }
                return Process.Start(psi) != null;
            });
            _fileExists = fileExists ?? File.Exists;
        }

        public static string BuildArguments(string template, string url)
        {
            var safeUrl = (url ?? string.Empty).Replace("\"", "%22");
            var quotedUrl = "\"" + safeUrl + "\"";
            var normalizedTemplate = LegacyPlaceholderRegex.Replace((template ?? string.Empty).Trim(), "{url}");

            if (SingleArgumentTokenRegex.IsMatch(normalizedTemplate))
            {
                var replaced = SingleArgumentTokenRegex.Replace(normalizedTemplate, m => m.Groups[1].Value + m.Groups[2].Value + safeUrl);
                return UrlTokenRegex.IsMatch(replaced)
                    ? UrlTokenRegex.Replace(replaced, _ => quotedUrl)
                    : replaced;
            }

            if (SingleArgumentTailRegex.IsMatch(normalizedTemplate))
            {
                return normalizedTemplate + " " + safeUrl;
            }

            if (UrlTokenRegex.IsMatch(normalizedTemplate))
            {
                return UrlTokenRegex.Replace(normalizedTemplate, _ => quotedUrl);
            }

            return normalizedTemplate.Length == 0
                ? quotedUrl
                : normalizedTemplate + " " + quotedUrl;
        }

        public bool IsSelfPath(string exePath)
        {
            var candidate = NormalizeFullPath(exePath);
            return candidate.Length > 0 &&
                   _selfExePath.Length > 0 &&
                   string.Equals(candidate, _selfExePath, StringComparison.OrdinalIgnoreCase);
        }

        public bool TryLaunch(string exe, string argsTemplate, string url, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(exe))
            {
                error = "Browser executable path is empty.";
                return false;
            }

            if (IsSelfPath(exe))
            {
                error = "Refused self-invocation of LinkToBrowser.";
                return false;
            }

            if (!_fileExists(exe))
            {
                error = $"Browser executable not found: {exe}";
                return false;
            }

            var builtArgs = BuildArguments(argsTemplate, url);
            if (exe.Length + 1 + builtArgs.Length > MaxCommandLineChars)
            {
                error = "Command line exceeds 32000 characters.";
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = builtArgs,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
                    UseShellExecute = false
                };
                if (!_start(psi))
                {
                    error = "Failed to start browser process.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string NormalizeFullPath(string path)
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
