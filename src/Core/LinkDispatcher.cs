using System;
using LinkToBrowser.Models;

namespace LinkToBrowser.Core
{
    public enum DispatchKind
    {
        Done,
        ShowPicker,
        ShowClipboard
    }

    public sealed class DispatchResult
    {
        public DispatchKind Kind { get; set; }
        public int ExitCode { get; set; }
        public string Url { get; set; } = string.Empty;
        public bool CopyOk { get; set; }
        public string Warning { get; set; } = string.Empty;
    }

    public sealed class LinkDispatcher
    {
        private readonly AppConfig _cfg;
        private readonly Func<string, string, string, (bool ok, string err)> _launch;
        private readonly Func<string, bool> _copy;
        private readonly Func<string, string> _resolveProgId;
        private readonly Func<ModifierKey, bool> _isKeyDown;
        private readonly Func<string, (bool ok, string exe, string args)> _splitCommand;
        private readonly Func<RecentBrowserTarget> _findRecentBrowser;
        private readonly Func<string, string, (bool ok, string err)> _openViaCdp;
        private readonly Action<IntPtr> _activateWindow;
        private readonly Func<string, IntPtr> _findWindowByUserDataDir;

        public LinkDispatcher(
            AppConfig cfg,
            Func<string, string, string, (bool ok, string err)> launch,
            Func<string, bool> copy,
            Func<string, string> resolveProgId,
            Func<ModifierKey, bool> isKeyDown,
            Func<string, (bool ok, string exe, string args)> splitCommand)
            : this(cfg, launch, copy, resolveProgId, isKeyDown, splitCommand, null, null, null, null)
        {
        }

        public LinkDispatcher(
            AppConfig cfg,
            Func<string, string, string, (bool ok, string err)> launch,
            Func<string, bool> copy,
            Func<string, string> resolveProgId,
            Func<ModifierKey, bool> isKeyDown,
            Func<string, (bool ok, string exe, string args)> splitCommand,
            Func<RecentBrowserTarget> findRecentBrowser,
            Func<string, string, (bool ok, string err)> openViaCdp,
            Action<IntPtr> activateWindow,
            Func<string, IntPtr> findWindowByUserDataDir = null)
        {
            _cfg = cfg ?? new AppConfig();
            _launch = launch;
            _copy = copy;
            _resolveProgId = resolveProgId;
            _isKeyDown = isKeyDown;
            _splitCommand = splitCommand;
            _findRecentBrowser = findRecentBrowser;
            _openViaCdp = openViaCdp;
            _activateWindow = activateWindow;
            _findWindowByUserDataDir = findWindowByUserDataDir;
        }

        public DispatchResult Dispatch(CliInvocation inv)
        {
            if (inv == null || inv.Kind != CliKind.HandleUrl)
            {
                return new DispatchResult { Kind = DispatchKind.Done, ExitCode = 0 };
            }

            if (!UrlService.TryNormalize(inv.RawUrl, out var url))
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.Done,
                    ExitCode = 2,
                    Url = inv.RawUrl ?? string.Empty,
                    Warning = "Invalid or unsupported URL."
                };
            }

            if (_cfg.StripTrackingParams)
            {
                url = UrlService.StripTracking(url);
            }

            if (inv.NoUi)
            {
                var ok = _copy(url);
                return new DispatchResult
                {
                    Kind = DispatchKind.Done,
                    ExitCode = ok ? 0 : 2,
                    Url = url,
                    CopyOk = ok
                };
            }

            var mode = (_cfg.Modifier != ModifierKey.None && _isKeyDown(_cfg.Modifier))
                ? GlobalMode.PickerPopup
                : _cfg.GlobalMode;

            return mode switch
            {
                GlobalMode.NativePassthrough => HandleNativePassthrough(url),
                GlobalMode.DefaultBrowser => HandleDefaultBrowser(url),
                GlobalMode.PickerPopup => new DispatchResult { Kind = DispatchKind.ShowPicker, Url = url },
                GlobalMode.ClipboardOnly => HandleClipboardOnly(url),
                GlobalMode.RecentBrowser => HandleRecentBrowser(url),
                _ => new DispatchResult { Kind = DispatchKind.ShowPicker, Url = url }
            };
        }

        private DispatchResult HandleRecentBrowser(string url)
        {
            var target = _findRecentBrowser?.Invoke();
            if (target == null || string.IsNullOrWhiteSpace(target.ExecutablePath))
            {
                return HandleRecentFallback(url);
            }

            if (_cfg.EnableCdpForIsolatedBrowsers &&
                target.HasDevToolsActivePort &&
                !string.IsNullOrWhiteSpace(target.UserDataDir) &&
                _openViaCdp != null)
            {
                var (cdpOk, cdpErr) = _openViaCdp(target.UserDataDir, url);
                if (cdpOk)
                {
                    try { _activateWindow?.Invoke(target.HWnd); } catch { }
                    return new DispatchResult
                    {
                        Kind = DispatchKind.Done,
                        ExitCode = 0,
                        Url = url
                    };
                }

                var copyOk = _copy(url);
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowClipboard,
                    Url = url,
                    CopyOk = copyOk,
                    Warning = L.Format("S_WarnCdpFallback", cdpErr)
                };
            }

            var res = LaunchOrFallbackToClipboard(target.ExecutablePath, target.ArgumentsTemplate, url);
            if (res.Kind == DispatchKind.Done && res.ExitCode == 0 && target.HWnd != IntPtr.Zero)
            {
                try { _activateWindow?.Invoke(target.HWnd); } catch { }
            }

            return res;
        }

        private DispatchResult HandleRecentFallback(string url)
        {
            return _cfg.RecentBrowserFallback switch
            {
                RecentFallbackMode.ClipboardOnly => HandleClipboardOnly(url),
                RecentFallbackMode.DefaultBrowser => HandleDefaultBrowser(url),
                RecentFallbackMode.NativePassthrough => HandleNativePassthrough(url),
                _ => new DispatchResult { Kind = DispatchKind.ShowPicker, Url = url }
            };
        }

        private DispatchResult HandleNativePassthrough(string url)
        {
            var progId = _cfg.OriginalDefaultProgId;
            if (string.IsNullOrWhiteSpace(progId) ||
                string.Equals(progId, SystemRegistration.ProgId, StringComparison.OrdinalIgnoreCase))
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    Warning = L.Get("S_WarnNoOriginal")
                };
            }

            var cmd = _resolveProgId(progId);
            if (string.IsNullOrWhiteSpace(cmd))
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    Warning = L.Format("S_WarnNoCommand", progId)
                };
            }

            var split = _splitCommand(cmd);
            if (!split.ok)
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    Warning = L.Format("S_WarnInvalidCommand", progId)
                };
            }

            return LaunchOrFallbackToClipboard(split.exe, split.args, url);
        }

        private DispatchResult HandleDefaultBrowser(string url)
        {
            var target = _cfg.DefaultBrowser;
            if (target == null || (string.IsNullOrWhiteSpace(target.ExecutablePath) && string.IsNullOrWhiteSpace(target.UserDataDir)))
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    Warning = L.Get("S_WarnNoDefaultTarget")
                };
            }

            if (_cfg.EnableCdpForIsolatedBrowsers &&
                !string.IsNullOrWhiteSpace(target.UserDataDir) &&
                _openViaCdp != null)
            {
                var (cdpOk, cdpErr) = _openViaCdp(target.UserDataDir, url);
                if (cdpOk)
                {
                    var hwnd = _findWindowByUserDataDir?.Invoke(target.UserDataDir) ?? IntPtr.Zero;
                    if (hwnd != IntPtr.Zero)
                    {
                        try { _activateWindow?.Invoke(hwnd); } catch { }
                    }

                    return new DispatchResult
                    {
                        Kind = DispatchKind.Done,
                        ExitCode = 0,
                        Url = url
                    };
                }

                if (target.Kind == BrowserKind.RunningWindow)
                {
                    var copyOk = _copy(url);
                    return new DispatchResult
                    {
                        Kind = DispatchKind.ShowClipboard,
                        Url = url,
                        CopyOk = copyOk,
                        Warning = L.Format("S_WarnIsolatedNotRunning", target.Name, cdpErr)
                    };
                }
            }

            return LaunchOrFallbackToClipboard(target.ExecutablePath, target.ArgumentsTemplate, url);
        }

        private DispatchResult LaunchOrFallbackToClipboard(string exe, string argsTemplate, string url)
        {
            var (ok, err) = _launch(exe, argsTemplate, url);
            if (ok)
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.Done,
                    ExitCode = 0,
                    Url = url
                };
            }

            var copyOk = _copy(url);
            return new DispatchResult
            {
                Kind = DispatchKind.ShowClipboard,
                Url = url,
                CopyOk = copyOk,
                Warning = L.Format("S_WarnLaunchFallback", err)
            };
        }

        private DispatchResult HandleClipboardOnly(string url)
        {
            var copyOk = _copy(url);
            if (!_cfg.ShowClipboardPopup && copyOk)
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.Done,
                    ExitCode = 0,
                    Url = url,
                    CopyOk = true
                };
            }

            return new DispatchResult
            {
                Kind = DispatchKind.ShowClipboard,
                Url = url,
                CopyOk = copyOk,
                Warning = copyOk ? string.Empty : L.Get("S_WarnClipBusy")
            };
        }
    }
}
