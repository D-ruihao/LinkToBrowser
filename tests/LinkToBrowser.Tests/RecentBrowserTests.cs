using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using Xunit;

namespace LinkToBrowser.Tests
{
    public sealed class RecentBrowserTests : IDisposable
    {
        private readonly string _tempDir;

        public RecentBrowserTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LinkToBrowserRecentTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void TryExtractFlagValue_HandlesQuotedAndUnquotedFlags()
        {
            const string cmd = @"C:\BitBrowser\BitBrowser.exe --disable-gpu --user-data-dir=D:\bitbrowser-cache\0296ab --fingerprint-config=C:\cfg --profile-directory=""Profile 1"" about:blank";

            Assert.True(RecentBrowserService.TryExtractFlagValue(cmd, "user-data-dir", out var udd));
            Assert.Equal(@"D:\bitbrowser-cache\0296ab", udd);

            Assert.True(RecentBrowserService.TryExtractFlagValue(cmd, "profile-directory", out var profile));
            Assert.Equal("Profile 1", profile);

            Assert.True(RecentBrowserService.TryExtractFlagValue(@"chrome.exe --user-data-dir=""C:\Users\Test User\Chrome Data""", "user-data-dir", out var spaced));
            Assert.Equal(@"C:\Users\Test User\Chrome Data", spaced);
        }

        [Fact]
        public void FindMostRecentBrowserFromCandidates_IgnoresWebViewAndHostApps_AllowsMinimizedAndIsolatedBrowsers()
        {
            var selfExe = @"C:\Tools\LinkToBrowser\LinkToBrowser.exe";
            var windows = new List<WindowCandidateInfo>
            {
                // Foreground chat / editor windows
                new WindowCandidateInfo { HWnd = new IntPtr(1), ClassName = "Chrome_WidgetWin_1", ExecutablePath = @"C:\App\Code.exe", CommandLine = @"Code.exe --user-data-dir=C:\CodeData" },
                // Embedded WebView2 (Clash / Teams)
                new WindowCandidateInfo { HWnd = new IntPtr(2), ClassName = "Chrome_WidgetWin_1", ExecutablePath = @"C:\Edge\msedgewebview2.exe", CommandLine = @"msedgewebview2.exe --embedded-browser-webview=1 --user-data-dir=C:\WebView" },
                // Management shell of BitBrowser (should be ignored)
                new WindowCandidateInfo { HWnd = new IntPtr(3), ClassName = "Chrome_WidgetWin_1", ExecutablePath = @"D:\bitbrowser\比特浏览器.exe", CommandLine = @"""D:\bitbrowser\比特浏览器.exe""" },
                // Minimized BitBrowser kernel window -> should STILL be matched!
                new WindowCandidateInfo
                {
                    HWnd = new IntPtr(4),
                    ClassName = "Chrome_WidgetWin_1",
                    ExecutablePath = @"C:\Users\Admin\AppData\Roaming\BitBrowser\Chrome-bin\152\BitBrowser.exe",
                    CommandLine = @"BitBrowser.exe --user-data-dir=D:\bitbrowser-cache\env1 about:blank",
                    IsMinimized = true
                },
                new WindowCandidateInfo
                {
                    HWnd = new IntPtr(5),
                    ClassName = "Chrome_WidgetWin_1",
                    ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    CommandLine = @"""C:\Program Files\Google\Chrome\Application\chrome.exe"" --profile-directory=""Profile 1"""
                }
            };

            var target = RecentBrowserService.FindMostRecentBrowserFromCandidates(
                windows,
                Array.Empty<BrowserEntry>(),
                selfExe,
                udd => string.Equals(udd, @"D:\bitbrowser-cache\env1", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(target);
            Assert.Equal(new IntPtr(4), target.HWnd);
            Assert.True(target.IsMinimized);
            Assert.Equal(@"D:\bitbrowser-cache\env1", target.UserDataDir);
            Assert.True(target.HasDevToolsActivePort);
        }

        [Fact]
        public void FindMostRecentBrowserFromCandidates_MatchesChromeProfileWindow()
        {
            var windows = new List<WindowCandidateInfo>
            {
                new WindowCandidateInfo
                {
                    HWnd = new IntPtr(10),
                    ClassName = "Chrome_WidgetWin_1",
                    ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    CommandLine = @"""C:\Program Files\Google\Chrome\Application\chrome.exe"" --profile-directory=""Profile 2""",
                    IsMinimized = true
                }
            };

            var target = RecentBrowserService.FindMostRecentBrowserFromCandidates(
                windows,
                Array.Empty<BrowserEntry>(),
                @"C:\Tools\LinkToBrowser\LinkToBrowser.exe",
                _ => false);

            Assert.NotNull(target);
            Assert.Equal("Profile 2", target.ProfileDirectory);
            Assert.Equal("--profile-directory=\"Profile 2\" {url}", target.ArgumentsTemplate);
            Assert.False(target.HasDevToolsActivePort);
        }

        [Fact]
        public void DevToolsActivePort_ParsingAndPayloadValidation()
        {
            var portFile = Path.Combine(_tempDir, "DevToolsActivePort");
            File.WriteAllText(portFile, "14286\n/devtools/browser/abc-123\n");

            Assert.True(CdpClient.TryParseDevToolsActivePort(portFile, out var port, out var wsPath));
            Assert.Equal(14286, port);
            Assert.Equal("/devtools/browser/abc-123", wsPath);

            var payload = CdpClient.BuildCreateTargetPayload(1, "https://example.com/a?x=\"1\"");
            Assert.Contains("Target.createTarget", payload);
            Assert.Contains("https://example.com/a", payload);

            Assert.True(CdpClient.TryValidateCreateTargetResponse("{\"id\":1,\"result\":{\"targetId\":\"T1\"}}", out _));
            Assert.False(CdpClient.TryValidateCreateTargetResponse("{\"id\":1,\"error\":{\"message\":\"blocked\"}}", out var err));
            Assert.Equal("blocked", err);
        }

        [Fact]
        public async Task TryOpenTab_EndToEndOverLoopbackWebSocket()
        {
            var freePort = GetFreeTcpPort();
            var wsPath = "/devtools/browser/test-id";
            File.WriteAllText(Path.Combine(_tempDir, "DevToolsActivePort"), $"{freePort}\n{wsPath}\n");

            string receivedJson = null;
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{freePort}/devtools/browser/");
            listener.Start();

            var serverTask = Task.Run(async () =>
            {
                var ctx = await listener.GetContextAsync();
                var wsCtx = await ctx.AcceptWebSocketAsync(null);
                var ws = wsCtx.WebSocket;
                var buf = new byte[4096];
                var recv = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                receivedJson = Encoding.UTF8.GetString(buf, 0, recv.Count);

                var reply = Encoding.UTF8.GetBytes("{\"id\":1,\"result\":{\"targetId\":\"TAB-99\"}}");
                await ws.SendAsync(new ArraySegment<byte>(reply), WebSocketMessageType.Text, true, CancellationToken.None);
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "ok", CancellationToken.None);
            });

            string err = null;
            var ok = await Task.Run(() => CdpClient.TryOpenTab(_tempDir, "https://example.com/hello", out err, timeoutMs: 3000));
            await serverTask;

            Assert.True(ok, $"Expected CDP call to succeed, got error: {err}");
            Assert.Contains("Target.createTarget", receivedJson);
            Assert.Contains("https://example.com/hello", receivedJson);
        }

        [Fact]
        public void Dispatcher_RecentBrowser_UsesCdpAndActivatesWindow_WhenDevToolsActivePortExists()
        {
            var cfg = new AppConfig
            {
                GlobalMode = GlobalMode.RecentBrowser,
                EnableCdpForIsolatedBrowsers = true
            };
            IntPtr activated = IntPtr.Zero;
            string cdpUdd = null;
            string cdpUrl = null;
            var launched = false;

            var d = new LinkDispatcher(
                cfg,
                (_, _, _) => { launched = true; return (true, null); },
                _ => true,
                _ => string.Empty,
                _ => false,
                _ => (false, string.Empty, string.Empty),
                () => new RecentBrowserTarget
                {
                    HWnd = new IntPtr(777),
                    ExecutablePath = @"C:\BitBrowser\BitBrowser.exe",
                    UserDataDir = @"D:\cache\env1",
                    HasDevToolsActivePort = true,
                    IsMinimized = true
                },
                (udd, url) => { cdpUdd = udd; cdpUrl = url; return (true, string.Empty); },
                hwnd => activated = hwnd);

            var res = d.Dispatch(CliParser.Parse(new[] { "https://example.com/test" }));

            Assert.Equal(DispatchKind.Done, res.Kind);
            Assert.Equal(0, res.ExitCode);
            Assert.False(launched);
            Assert.Equal(@"D:\cache\env1", cdpUdd);
            Assert.Equal("https://example.com/test", cdpUrl);
            Assert.Equal(new IntPtr(777), activated);
        }

        [Fact]
        public void Dispatcher_RecentBrowser_FallsBackWhenNoBrowserRunning()
        {
            var cfg = new AppConfig
            {
                GlobalMode = GlobalMode.RecentBrowser,
                RecentBrowserFallback = RecentFallbackMode.ClipboardOnly,
                ShowClipboardPopup = false
            };
            string copied = null;

            var d = new LinkDispatcher(
                cfg,
                (_, _, _) => (false, "not called"),
                u => { copied = u; return true; },
                _ => string.Empty,
                _ => false,
                _ => (false, string.Empty, string.Empty),
                () => null,
                (_, _) => (false, "not called"),
                _ => { });

            var res = d.Dispatch(CliParser.Parse(new[] { "https://example.com/fallback" }));

            Assert.Equal(DispatchKind.Done, res.Kind);
            Assert.Equal(0, res.ExitCode);
            Assert.Equal("https://example.com/fallback", copied);
        }

        private static int GetFreeTcpPort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        [Fact]
        public void GetRunningIsolatedBrowsersFromCandidates_ExtractsWindowTitleAndDeduplicatesUserDataDir()
        {
            var windows = new List<WindowCandidateInfo>
            {
                new WindowCandidateInfo
                {
                    HWnd = new IntPtr(101),
                    ClassName = "Chrome_WidgetWin_1",
                    WindowTitle = "GitHub - BitBrowser",
                    ExecutablePath = @"C:\BitBrowser\BitBrowser.exe",
                    CommandLine = @"BitBrowser.exe --user-data-dir=D:\cache\envA about:blank"
                },
                new WindowCandidateInfo
                {
                    HWnd = new IntPtr(102),
                    ClassName = "Chrome_WidgetWin_1",
                    WindowTitle = "Secondary Tab - BitBrowser",
                    ExecutablePath = @"C:\BitBrowser\BitBrowser.exe",
                    CommandLine = @"BitBrowser.exe --user-data-dir=D:\cache\envA about:blank"
                },
                new WindowCandidateInfo
                {
                    HWnd = new IntPtr(103),
                    ClassName = "Chrome_WidgetWin_1",
                    WindowTitle = "Google Chrome",
                    ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    CommandLine = @"""C:\Program Files\Google\Chrome\Application\chrome.exe"""
                }
            };

            var entries = RecentBrowserService.GetRunningIsolatedBrowsersFromCandidates(
                windows,
                Array.Empty<BrowserEntry>(),
                @"C:\Tools\LinkToBrowser\LinkToBrowser.exe",
                udd => string.Equals(udd, @"D:\cache\envA", StringComparison.OrdinalIgnoreCase));

            Assert.Single(entries);
            Assert.Equal(BrowserKind.RunningWindow, entries[0].Kind);
            Assert.Equal(@"D:\cache\envA", entries[0].UserDataDir);
            Assert.Contains("GitHub - BitBrowser", entries[0].Name);
        }

        [Fact]
        public void MergeWithRunning_PlacesUnorderedActiveWindowsFirst_AndRespectsHiddenAndOrder()
        {
            var baseEntries = new List<BrowserEntry>
            {
                new BrowserEntry { Id = "reg:chrome", Name = "Google Chrome", ExecutablePath = @"C:\chrome.exe" },
                new BrowserEntry { Id = "reg:edge", Name = "Microsoft Edge", ExecutablePath = @"C:\edge.exe" }
            };
            var running = new List<BrowserEntry>
            {
                new BrowserEntry { Id = "cdp:d:\\cache\\env1", Name = "BitBrowser · Env1", ExecutablePath = @"C:\BitBrowser.exe", UserDataDir = @"D:\cache\env1", Kind = BrowserKind.RunningWindow }
            };

            var cfg = new AppConfig();
            var merged = BrowserCatalog.MergeWithRunning(baseEntries, running, cfg, includeHidden: false);
            Assert.Equal(3, merged.Count);
            Assert.Equal("cdp:d:\\cache\\env1", merged[0].Id);

            cfg.HiddenBrowserIds.Add("cdp:d:\\cache\\env1");
            var visibleOnly = BrowserCatalog.MergeWithRunning(baseEntries, running, cfg, includeHidden: false);
            Assert.Equal(2, visibleOnly.Count);
        }

        [Fact]
        public void Dispatcher_DefaultBrowser_UsesCdpWhenRunningWindowTargetIsActive_AndShowsClipboardWhenInactive()
        {
            var activeEntry = new BrowserEntry
            {
                Id = "cdp:d:\\cache\\env1",
                Name = "BitBrowser · Env1",
                ExecutablePath = @"C:\BitBrowser.exe",
                UserDataDir = @"D:\cache\env1",
                Kind = BrowserKind.RunningWindow
            };
            var cfg = new AppConfig
            {
                GlobalMode = GlobalMode.DefaultBrowser,
                DefaultBrowser = activeEntry,
                EnableCdpForIsolatedBrowsers = true
            };

            IntPtr activated = IntPtr.Zero;
            var launchedCli = false;
            var dispatcherOk = new LinkDispatcher(
                cfg,
                (_, _, _) => { launchedCli = true; return (true, null); },
                _ => true,
                _ => string.Empty,
                _ => false,
                _ => (false, string.Empty, string.Empty),
                () => null,
                (udd, url) => (udd == @"D:\cache\env1", "ok"),
                hwnd => activated = hwnd,
                udd => udd == @"D:\cache\env1" ? new IntPtr(888) : IntPtr.Zero);

            var okRes = dispatcherOk.Dispatch(CliParser.Parse(new[] { "https://example.com/a" }));
            Assert.Equal(DispatchKind.Done, okRes.Kind);
            Assert.False(launchedCli);
            Assert.Equal(new IntPtr(888), activated);

            // Now simulate target isolated window closed -> falls back to clipboard with warning without launching exe
            var copied = false;
            var dispatcherInactive = new LinkDispatcher(
                cfg,
                (_, _, _) => { launchedCli = true; return (true, null); },
                _ => { copied = true; return true; },
                _ => string.Empty,
                _ => false,
                _ => (false, string.Empty, string.Empty),
                () => null,
                (_, _) => (false, "DevToolsActivePort not found"),
                _ => { },
                _ => IntPtr.Zero);

            var inactiveRes = dispatcherInactive.Dispatch(CliParser.Parse(new[] { "https://example.com/b" }));
            Assert.Equal(DispatchKind.ShowClipboard, inactiveRes.Kind);
            Assert.False(launchedCli);
            Assert.True(copied);
            Assert.False(string.IsNullOrWhiteSpace(inactiveRes.Warning));
        }
    }
}
