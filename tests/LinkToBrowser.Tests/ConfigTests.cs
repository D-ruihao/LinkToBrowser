using System;
using System.IO;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using Xunit;

namespace LinkToBrowser.Tests
{
    public sealed class ConfigTests : IDisposable
    {
        private readonly string _rootDir;
        private readonly string _exeDir;
        private readonly string _localDir;

        public ConfigTests()
        {
            _rootDir = Path.Combine(Path.GetTempPath(), "LinkToBrowserTests_" + Guid.NewGuid().ToString("N"));
            _exeDir = Path.Combine(_rootDir, "exe");
            _localDir = Path.Combine(_rootDir, "local");
            Directory.CreateDirectory(_exeDir);
            Directory.CreateDirectory(_localDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, true); } catch { }
        }

        [Fact]
        public void Load_NoFiles_ReturnsDefaults()
        {
            var svc = new ConfigService(_exeDir, _localDir);
            var cfg = svc.Load();
            Assert.Equal(GlobalMode.ClipboardOnly, cfg.GlobalMode);
            Assert.Equal("MSEdgeHTM", cfg.OriginalDefaultProgId);
            Assert.Null(cfg.DefaultBrowser);
            Assert.Equal(ModifierKey.None, cfg.Modifier);
            Assert.True(cfg.ShowClipboardPopup);
            Assert.Equal(0, cfg.ClipboardAutoCloseSeconds);
            Assert.False(cfg.StripTrackingParams);
            Assert.True(cfg.ShowChromiumProfiles);
            Assert.Equal(ThemeMode.System, cfg.Theme);
        }

        [Fact]
        public void Save_FirstTime_WritesExeDirWithoutTmpLeft()
        {
            var svc = new ConfigService(_exeDir, _localDir);
            svc.Save(new AppConfig { GlobalMode = GlobalMode.PickerPopup });

            var expectedPath = Path.Combine(_exeDir, "config.json");
            Assert.Equal(expectedPath, svc.ConfigPath);
            Assert.True(File.Exists(expectedPath));
            Assert.False(File.Exists(expectedPath + ".tmp"));
        }

        [Fact]
        public void SaveTwice_RoundTripsFields()
        {
            var svc = new ConfigService(_exeDir, _localDir);
            svc.Save(new AppConfig { GlobalMode = GlobalMode.DefaultBrowser });

            var updated = new AppConfig
            {
                GlobalMode = GlobalMode.PickerPopup,
                OriginalDefaultProgId = "ChromeHTML",
                Modifier = ModifierKey.Alt,
                ClipboardAutoCloseSeconds = 5,
                StripTrackingParams = true,
                ShowChromiumProfiles = false,
                Theme = ThemeMode.Dark
            };
            updated.CustomBrowsers.Add(new BrowserEntry
            {
                Id = BrowserEntry.MakeId(BrowserKind.Custom, @"D:\Bit\chrome.exe", "--user-data-dir=1 {url}"),
                Name = "Bit Kernel",
                ExecutablePath = @"D:\Bit\chrome.exe",
                ArgumentsTemplate = "--user-data-dir=1 {url}",
                Kind = BrowserKind.Custom
            });
            updated.DefaultBrowser = updated.CustomBrowsers[0];

            svc.Save(updated);

            var loaded = new ConfigService(_exeDir, _localDir).Load();
            Assert.Equal(GlobalMode.PickerPopup, loaded.GlobalMode);
            Assert.Equal("ChromeHTML", loaded.OriginalDefaultProgId);
            Assert.Equal(ModifierKey.Alt, loaded.Modifier);
            Assert.Equal(5, loaded.ClipboardAutoCloseSeconds);
            Assert.True(loaded.StripTrackingParams);
            Assert.False(loaded.ShowChromiumProfiles);
            Assert.Equal(ThemeMode.Dark, loaded.Theme);
            Assert.Single(loaded.CustomBrowsers);
            Assert.Equal("Bit Kernel", loaded.CustomBrowsers[0].Name);
            Assert.NotNull(loaded.DefaultBrowser);
            Assert.Equal("Bit Kernel", loaded.DefaultBrowser.Name);
        }

        [Fact]
        public void Load_Corrupt_ReturnsDefaults()
        {
            File.WriteAllText(Path.Combine(_exeDir, "config.json"), "{bad");
            var loaded = new ConfigService(_exeDir, _localDir).Load();
            Assert.Equal(GlobalMode.ClipboardOnly, loaded.GlobalMode);
        }

        [Fact]
        public void Load_PrefersExeDirOverLocalAppData()
        {
            new ConfigService(_localDir, _localDir).Save(new AppConfig { GlobalMode = GlobalMode.NativePassthrough });
            new ConfigService(_exeDir, _localDir).Save(new AppConfig { GlobalMode = GlobalMode.PickerPopup });

            var svc = new ConfigService(_exeDir, _localDir);
            var loaded = svc.Load();
            Assert.Equal(GlobalMode.PickerPopup, loaded.GlobalMode);
            Assert.Equal(Path.Combine(_exeDir, "config.json"), svc.ConfigPath);
        }

        [Fact]
        public void MakeId_NormalizesCaseAndWhitespace()
        {
            var id1 = BrowserEntry.MakeId(BrowserKind.Registry, @"C:\Program Files\Chrome\chrome.exe", "");
            var id2 = BrowserEntry.MakeId(BrowserKind.Registry, @"c:\program files\chrome\CHROME.EXE ", null);
            Assert.Equal(id1, id2);
            Assert.StartsWith("Registry:", id1);
        }

        [Fact]
        public void LogService_WritesArgsAndLinksAndClears()
        {
            var logDir = Path.Combine(_rootDir, "logs");
            var log = new LogService(logDir);
            log.LogArgs(new[] { "--no-ui", "https://example.com" });
            log.LogLink("https://example.com", true);
            log.LogLink("https://bad.test", false, "exe missing");

            var argsLines = File.ReadAllLines(Path.Combine(logDir, "args.log"));
            var linksLines = File.ReadAllLines(Path.Combine(logDir, "links.log"));
            Assert.Single(argsLines);
            Assert.Contains("\tARGS\t--no-ui | https://example.com", argsLines[0]);
            Assert.Equal(2, linksLines.Length);
            Assert.Contains("\tOK\thttps://example.com", linksLines[0]);
            Assert.Contains("\tFAIL\thttps://bad.test\texe missing", linksLines[1]);

            log.Clear();
            Assert.False(File.Exists(Path.Combine(logDir, "args.log")));
            Assert.False(File.Exists(Path.Combine(logDir, "links.log")));
        }

        [Fact]
        public void LogService_FallsBackToSecondaryDir_WhenPrimaryCannotBeCreated()
        {
            var blockingFile = Path.Combine(_rootDir, "not-a-dir");
            File.WriteAllText(blockingFile, "occupied");
            var fallbackLogs = Path.Combine(_localDir, "logs");

            var log = new LogService(blockingFile, fallbackLogs);
            log.LogLink("https://example.com", true);

            Assert.Equal(fallbackLogs, log.LogDir);
            Assert.True(File.Exists(Path.Combine(fallbackLogs, "links.log")));
        }
    }
}
