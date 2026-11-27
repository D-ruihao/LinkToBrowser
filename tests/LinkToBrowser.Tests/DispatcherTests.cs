using LinkToBrowser.Core;
using LinkToBrowser.Models;
using Xunit;

namespace LinkToBrowser.Tests
{
    public sealed class DispatcherTests
    {
        [Theory]
        [InlineData(GlobalMode.NativePassthrough)]
        [InlineData(GlobalMode.DefaultBrowser)]
        [InlineData(GlobalMode.PickerPopup)]
        [InlineData(GlobalMode.ClipboardOnly)]
        public void NoUi_AnyMode_CopiesOnly_NeverLaunches(GlobalMode mode)
        {
            var launched = false;
            var copied = string.Empty;
            var cfg = new AppConfig
            {
                GlobalMode = mode,
                OriginalDefaultProgId = "ChromeHTML",
                DefaultBrowser = new BrowserEntry { Name = "Chrome", ExecutablePath = @"C:\Chrome\chrome.exe" }
            };

            var dispatcher = new LinkDispatcher(
                cfg,
                (_, _, _) => { launched = true; return (true, ""); },
                u => { copied = u; return true; },
                _ => @"C:\Chrome\chrome.exe",
                _ => false,
                _ => (true, @"C:\Chrome\chrome.exe", ""));

            var res = dispatcher.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test", NoUi = true });
            Assert.Equal(DispatchKind.Done, res.Kind);
            Assert.Equal(0, res.ExitCode);
            Assert.False(launched);
            Assert.Equal("https://a.test", copied);
        }

        [Fact]
        public void Native_ResolvesProgId_LaunchesSilently()
        {
            var launchedExe = string.Empty;
            var launchedArgs = string.Empty;
            var launchedUrl = string.Empty;
            var cfg = new AppConfig { GlobalMode = GlobalMode.NativePassthrough, OriginalDefaultProgId = "ChromeHTML" };

            var dispatcher = new LinkDispatcher(
                cfg,
                (exe, args, url) => { launchedExe = exe; launchedArgs = args; launchedUrl = url; return (true, ""); },
                _ => true,
                progId => progId == "ChromeHTML" ? @"""C:\Chrome\chrome.exe"" -- ""%1""" : null,
                _ => false,
                cmd => (true, @"C:\Chrome\chrome.exe", "-- {url}"));

            var res = dispatcher.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test" });
            Assert.Equal(DispatchKind.Done, res.Kind);
            Assert.Equal(0, res.ExitCode);
            Assert.Equal(@"C:\Chrome\chrome.exe", launchedExe);
            Assert.Equal("-- {url}", launchedArgs);
            Assert.Equal("https://a.test", launchedUrl);
        }

        [Theory]
        [InlineData("")]
        [InlineData("LinkToBrowser.URL")]
        [InlineData("AppXMissingCommand")]
        public void Native_ProgIdMissingSelfOrUnresolvable_ShowsPickerWithWarning(string progId)
        {
            var cfg = new AppConfig { GlobalMode = GlobalMode.NativePassthrough, OriginalDefaultProgId = progId };
            var dispatcher = new LinkDispatcher(
                cfg,
                (_, _, _) => (true, ""),
                _ => true,
                id => id == "ChromeHTML" ? @"C:\Chrome\chrome.exe" : null,
                _ => false,
                _ => (false, "", ""));

            var res = dispatcher.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test" });
            Assert.Equal(DispatchKind.ShowPicker, res.Kind);
            Assert.False(string.IsNullOrWhiteSpace(res.Warning));
        }

        [Fact]
        public void Native_LaunchFails_CopiesAndShowsClipboardWithWarning()
        {
            var copied = string.Empty;
            var cfg = new AppConfig { GlobalMode = GlobalMode.NativePassthrough, OriginalDefaultProgId = "ChromeHTML" };
            var dispatcher = new LinkDispatcher(
                cfg,
                (_, _, _) => (false, "access denied"),
                u => { copied = u; return true; },
                id => id == "ChromeHTML" ? @"C:\Chrome\chrome.exe" : null,
                _ => false,
                _ => (true, @"C:\Chrome\chrome.exe", ""));

            var res = dispatcher.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test" });
            Assert.Equal(DispatchKind.ShowClipboard, res.Kind);
            Assert.True(res.CopyOk);
            Assert.Equal("https://a.test", copied);
            Assert.Contains("access denied", res.Warning);
        }

        [Fact]
        public void Default_CachedBrowser_LaunchesSilently()
        {
            var launchedExe = string.Empty;
            var cfg = new AppConfig
            {
                GlobalMode = GlobalMode.DefaultBrowser,
                DefaultBrowser = new BrowserEntry
                {
                    Name = "Bit",
                    ExecutablePath = @"D:\Bit\chrome.exe",
                    ArgumentsTemplate = "--user-data-dir=1 {url}"
                }
            };

            var dispatcher = new LinkDispatcher(
                cfg,
                (exe, _, _) => { launchedExe = exe; return (true, ""); },
                _ => true,
                _ => null,
                _ => false,
                _ => (false, "", ""));

            var res = dispatcher.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test" });
            Assert.Equal(DispatchKind.Done, res.Kind);
            Assert.Equal(@"D:\Bit\chrome.exe", launchedExe);
        }

        [Fact]
        public void Default_NotSet_ShowsPickerWithWarning()
        {
            var cfg = new AppConfig { GlobalMode = GlobalMode.DefaultBrowser, DefaultBrowser = null };
            var dispatcher = new LinkDispatcher(
                cfg,
                (_, _, _) => (true, ""),
                _ => true,
                _ => null,
                _ => false,
                _ => (false, "", ""));

            var res = dispatcher.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test" });
            Assert.Equal(DispatchKind.ShowPicker, res.Kind);
            Assert.False(string.IsNullOrWhiteSpace(res.Warning));
        }

        [Fact]
        public void Default_LaunchFails_CopiesAndShowsClipboardWithWarning()
        {
            var copied = string.Empty;
            var cfg = new AppConfig
            {
                GlobalMode = GlobalMode.DefaultBrowser,
                DefaultBrowser = new BrowserEntry { Name = "Broken", ExecutablePath = @"D:\missing.exe" }
            };

            var dispatcher = new LinkDispatcher(
                cfg,
                (_, _, _) => (false, "Browser executable not found"),
                u => { copied = u; return true; },
                _ => null,
                _ => false,
                _ => (false, "", ""));

            var res = dispatcher.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test" });
            Assert.Equal(DispatchKind.ShowClipboard, res.Kind);
            Assert.True(res.CopyOk);
            Assert.Equal("https://a.test", copied);
            Assert.Contains("not found", res.Warning);
        }

        [Fact]
        public void ModifierHeld_DefaultMode_ShowsPicker_AndNoneIgnoresKeyState()
        {
            var cfgAlt = new AppConfig
            {
                GlobalMode = GlobalMode.DefaultBrowser,
                Modifier = ModifierKey.Alt,
                DefaultBrowser = new BrowserEntry { Name = "Chrome", ExecutablePath = @"C:\Chrome\chrome.exe" }
            };

            var dAlt = new LinkDispatcher(cfgAlt, (_, _, _) => (true, ""), _ => true, _ => null, k => k == ModifierKey.Alt, _ => (false, "", ""));
            Assert.Equal(DispatchKind.ShowPicker, dAlt.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test" }).Kind);

            var cfgNone = new AppConfig
            {
                GlobalMode = GlobalMode.DefaultBrowser,
                Modifier = ModifierKey.None,
                DefaultBrowser = new BrowserEntry { Name = "Chrome", ExecutablePath = @"C:\Chrome\chrome.exe" }
            };

            var dNone = new LinkDispatcher(cfgNone, (_, _, _) => (true, ""), _ => true, _ => null, _ => true, _ => (false, "", ""));
            Assert.Equal(DispatchKind.Done, dNone.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test" }).Kind);
        }

        [Fact]
        public void Clipboard_PopupOff_CopyOkDone_CopyFailsShowsWindow()
        {
            var cfg = new AppConfig { GlobalMode = GlobalMode.ClipboardOnly, ShowClipboardPopup = false };

            var dOk = new LinkDispatcher(cfg, (_, _, _) => (true, ""), _ => true, _ => null, _ => false, _ => (false, "", ""));
            var resOk = dOk.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test" });
            Assert.Equal(DispatchKind.Done, resOk.Kind);
            Assert.True(resOk.CopyOk);

            var dFail = new LinkDispatcher(cfg, (_, _, _) => (true, ""), _ => false, _ => null, _ => false, _ => (false, "", ""));
            var resFail = dFail.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test" });
            Assert.Equal(DispatchKind.ShowClipboard, resFail.Kind);
            Assert.False(resFail.CopyOk);
        }

        [Fact]
        public void StripTrackingEnabled_UrlCleanedBeforeLaunch()
        {
            var launchedUrl = string.Empty;
            var cfg = new AppConfig
            {
                GlobalMode = GlobalMode.DefaultBrowser,
                StripTrackingParams = true,
                DefaultBrowser = new BrowserEntry { Name = "Chrome", ExecutablePath = @"C:\Chrome\chrome.exe" }
            };

            var d = new LinkDispatcher(
                cfg,
                (_, _, u) => { launchedUrl = u; return (true, ""); },
                _ => true,
                _ => null,
                _ => false,
                _ => (false, "", ""));

            d.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "https://a.test/p?utm_source=x&id=1" });
            Assert.Equal("https://a.test/p?id=1", launchedUrl);
        }

        [Fact]
        public void InvalidUrl_ExitCode2_NoAction()
        {
            var launched = false;
            var copied = false;
            var d = new LinkDispatcher(
                new AppConfig { GlobalMode = GlobalMode.DefaultBrowser },
                (_, _, _) => { launched = true; return (true, ""); },
                _ => { copied = true; return true; },
                _ => null,
                _ => false,
                _ => (false, "", ""));

            var res = d.Dispatch(new CliInvocation { Kind = CliKind.HandleUrl, RawUrl = "javascript:alert(1)" });
            Assert.Equal(DispatchKind.Done, res.Kind);
            Assert.Equal(2, res.ExitCode);
            Assert.False(launched);
            Assert.False(copied);
        }

        [Fact]
        public void TrySnapshotWithResolvers_WhenUserChoiceIsAlreadyLinkToBrowser_FallsBackToEdge()
        {
            var cfg = new AppConfig { OriginalDefaultProgId = string.Empty };
            var changed = SystemRegistration.TrySnapshotWithResolvers(
                cfg,
                @"D:\Tools\LinkToBrowser.exe",
                _ => "LinkToBrowser.URL",
                progId => progId == "MSEdgeHTM" ? @"""C:\Edge\msedge.exe"" --single-argument %1" : null,
                cmd => cmd != null ? (true, @"C:\Edge\msedge.exe", "--single-argument {url}") : (false, "", ""));

            Assert.True(changed);
            Assert.Equal("MSEdgeHTM", cfg.OriginalDefaultProgId);
        }

        [Fact]
        public void TrySnapshotWithResolvers_WhenUserChoiceIsRealBrowser_CapturesItOverEdge()
        {
            var cfg = new AppConfig { OriginalDefaultProgId = "MSEdgeHTM" };
            var changed = SystemRegistration.TrySnapshotWithResolvers(
                cfg,
                @"D:\Tools\LinkToBrowser.exe",
                scheme => scheme == "https" ? "FirefoxURL-ABC" : "",
                progId => progId == "FirefoxURL-ABC" ? @"""C:\Firefox\firefox.exe"" -url ""%1""" : null,
                cmd => cmd != null ? (true, @"C:\Firefox\firefox.exe", "-url \"{url}\"") : (false, "", ""));

            Assert.True(changed);
            Assert.Equal("FirefoxURL-ABC", cfg.OriginalDefaultProgId);
        }
    }
}
