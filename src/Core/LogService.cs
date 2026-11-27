using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LinkToBrowser.Core
{
    public sealed class LogService
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly string _primaryLogDir;
        private readonly string _fallbackLogDir;

        public LogService(string primaryLogDir, string fallbackLogDir = null)
        {
            _primaryLogDir = string.IsNullOrWhiteSpace(primaryLogDir) ? Path.GetTempPath() : primaryLogDir;
            _fallbackLogDir = string.IsNullOrWhiteSpace(fallbackLogDir) ? null : fallbackLogDir;
            LogDir = ResolveExistingOrPrimaryDir();
        }

        public string LogDir { get; private set; }

        public void LogArgs(string[] args)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tARGS\t{string.Join(" | ", args ?? Array.Empty<string>())}\r\n";
            AppendLine("args.log", line);
        }

        public void LogWarn(string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tWARN\t{message}\r\n";
            AppendLine("args.log", line);
        }

        public void LogLink(string url, bool ok, string detail = null)
        {
            var status = ok ? "OK" : "FAIL";
            var suffix = string.IsNullOrEmpty(detail) ? string.Empty : "\t" + detail;
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{status}\t{url}{suffix}\r\n";
            AppendLine("links.log", line);
        }

        public void Clear()
        {
            DeleteIfExists(Path.Combine(LogDir, "args.log"));
            DeleteIfExists(Path.Combine(LogDir, "links.log"));
        }

        public void OpenDirectory(Action<ProcessStartInfo> starter = null)
        {
            var targetDir = EnsureWritableDir();
            var psi = new ProcessStartInfo("explorer.exe", $"\"{targetDir}\"");
            if (starter != null)
            {
                starter(psi);
                return;
            }

            Process.Start(psi);
        }

        private string ResolveExistingOrPrimaryDir()
        {
            if (Directory.Exists(_primaryLogDir))
            {
                return _primaryLogDir;
            }

            if (!string.IsNullOrEmpty(_fallbackLogDir) && Directory.Exists(_fallbackLogDir))
            {
                return _fallbackLogDir;
            }

            return _primaryLogDir;
        }

        private string EnsureWritableDir()
        {
            if (TryCreateDir(_primaryLogDir))
            {
                LogDir = _primaryLogDir;
                return LogDir;
            }

            if (!string.IsNullOrEmpty(_fallbackLogDir) && TryCreateDir(_fallbackLogDir))
            {
                LogDir = _fallbackLogDir;
                return LogDir;
            }

            return LogDir;
        }

        private void AppendLine(string fileName, string line)
        {
            if (TryAppendToDir(_primaryLogDir, fileName, line))
            {
                LogDir = _primaryLogDir;
                return;
            }

            if (!string.IsNullOrEmpty(_fallbackLogDir) && TryAppendToDir(_fallbackLogDir, fileName, line))
            {
                LogDir = _fallbackLogDir;
            }
        }

        private static bool TryCreateDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                return Directory.Exists(dir);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryAppendToDir(string dir, string fileName, string line)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, fileName);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                var bytes = Utf8NoBom.GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
