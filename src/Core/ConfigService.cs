using System;
using System.IO;
using System.Security;
using System.Text;
using System.Web.Script.Serialization;
using LinkToBrowser.Models;

namespace LinkToBrowser.Core
{
    public sealed class ConfigService
    {
        private const string ConfigFileName = "config.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly string _exeDir;
        private readonly string _localAppDataDir;

        public ConfigService(string exeDir, string localAppDataDir)
        {
            _exeDir = exeDir ?? string.Empty;
            _localAppDataDir = localAppDataDir ?? string.Empty;
            ConfigPath = Path.Combine(_exeDir, ConfigFileName);
        }

        public string ConfigPath { get; private set; }

        public AppConfig Load()
        {
            var exePath = Path.Combine(_exeDir, ConfigFileName);
            var localPath = Path.Combine(_localAppDataDir, ConfigFileName);

            if (File.Exists(exePath))
            {
                ConfigPath = exePath;
                return ReadFileOrDefault(exePath);
            }

            if (File.Exists(localPath))
            {
                ConfigPath = localPath;
                return ReadFileOrDefault(localPath);
            }

            ConfigPath = exePath;
            return new AppConfig();
        }

        public void Save(AppConfig config)
        {
            var normalized = (config ?? new AppConfig()).Normalize();
            var json = new JavaScriptSerializer().Serialize(normalized);

            var exePath = Path.Combine(_exeDir, ConfigFileName);
            if (TryWriteAtomic(exePath, json))
            {
                ConfigPath = exePath;
                return;
            }

            var localPath = Path.Combine(_localAppDataDir, ConfigFileName);
            if (TryWriteAtomic(localPath, json))
            {
                ConfigPath = localPath;
            }
        }

        private static AppConfig ReadFileOrDefault(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Utf8NoBom);
                var json = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new AppConfig();
                }

                var loaded = new JavaScriptSerializer().Deserialize<AppConfig>(json);
                return (loaded ?? new AppConfig()).Normalize();
            }
            catch
            {
                return new AppConfig();
            }
        }

        private static bool TryWriteAtomic(string targetPath, string content)
        {
            var tmpPath = targetPath + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(tmpPath, content, Utf8NoBom);
                if (File.Exists(targetPath))
                {
                    File.Replace(tmpPath, targetPath, null, true);
                }
                else
                {
                    File.Move(tmpPath, targetPath);
                }

                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is SecurityException)
            {
                try
                {
                    if (File.Exists(tmpPath))
                    {
                        File.Delete(tmpPath);
                    }
                }
                catch
                {
                }

                return false;
            }
        }
    }
}
