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
        public string SourceApp { get; set; } = string.Empty;
        public string MatchedRuleName { get; set; } = string.Empty;
        public string RouteAction { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;

        public string FormatRouteDetail()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(SourceApp)) parts.Add($"src={SourceApp}");
            if (!string.IsNullOrWhiteSpace(MatchedRuleName)) parts.Add($"rule={MatchedRuleName}");
            if (!string.IsNullOrWhiteSpace(RouteAction)) parts.Add($"action={RouteAction}");
            if (!string.IsNullOrWhiteSpace(TargetName)) parts.Add($"target={TargetName}");
            if (!string.IsNullOrWhiteSpace(Warning)) parts.Add($"warn={Warning}");
            return string.Join(" | ", parts);
        }
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
        private readonly Func<string> _detectSourceApp;

        public LinkDispatcher(
            AppConfig cfg,
            Func<string, string, string, (bool ok, string err)> launch,
            Func<string, bool> copy,
            Func<string, string> resolveProgId,
            Func<ModifierKey, bool> isKeyDown,
            Func<string, (bool ok, string exe, string args)> splitCommand)
            : this(cfg, launch, copy, resolveProgId, isKeyDown, splitCommand, null, null, null, null, null)
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
            Func<string, IntPtr> findWindowByUserDataDir = null,
            Func<string> detectSourceApp = null)
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
            _detectSourceApp = detectSourceApp;
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

            url = UrlService.Prepare(url, _cfg);

            var sourceApp = string.Empty;
            try { sourceApp = _detectSourceApp?.Invoke() ?? string.Empty; } catch { }

            if (inv.NoUi)
            {
                var ok = _copy(url);
                return new DispatchResult
                {
                    Kind = DispatchKind.Done,
                    ExitCode = ok ? 0 : 2,
                    Url = url,
                    CopyOk = ok,
                    SourceApp = sourceApp,
                    RouteAction = "NoUiClipboard"
                };
            }

            if (_cfg.Modifier != ModifierKey.None && _isKeyDown(_cfg.Modifier))
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    SourceApp = sourceApp,
                    RouteAction = "ModifierPicker"
                };
            }

            if (_cfg.EnableRoutingRules && _cfg.Rules != null && _cfg.Rules.Count > 0)
            {
                var matchedRule = RuleMatcher.Match(url, _cfg.Rules, sourceApp);
                if (matchedRule != null)
                {
                    var ruleRes = ExecuteRule(matchedRule, url);
                    ruleRes.SourceApp = sourceApp;
                    ruleRes.MatchedRuleName = string.IsNullOrWhiteSpace(matchedRule.Name)
                        ? (string.IsNullOrWhiteSpace(matchedRule.Pattern) ? matchedRule.SourceApps : matchedRule.Pattern)
                        : matchedRule.Name;
                    return ruleRes;
                }
            }

            var globalRes = _cfg.GlobalMode switch
            {
                GlobalMode.NativePassthrough => HandleNativePassthrough(url),
                GlobalMode.DefaultBrowser => HandleDefaultBrowser(url),
                GlobalMode.PickerPopup => new DispatchResult { Kind = DispatchKind.ShowPicker, Url = url, RouteAction = "Global:PickerPopup" },
                GlobalMode.ClipboardOnly => HandleClipboardOnly(url),
                GlobalMode.RecentBrowser => HandleRecentBrowser(url),
                _ => new DispatchResult { Kind = DispatchKind.ShowPicker, Url = url, RouteAction = "Global:PickerPopup" }
            };
            globalRes.SourceApp = sourceApp;
            return globalRes;
        }

        private DispatchResult ExecuteRule(RoutingRule rule, string url)
        {
            var res = rule.Action switch
            {
                RuleActionKind.OpenTarget => ExecuteBrowserTarget(rule.TargetBrowser, url),
                RuleActionKind.ClipboardOnly => HandleClipboardOnly(url),
                RuleActionKind.PickerPopup => new DispatchResult { Kind = DispatchKind.ShowPicker, Url = url },
                RuleActionKind.RecentBrowser => HandleRecentBrowser(url),
                RuleActionKind.NativePassthrough => HandleNativePassthrough(url),
                _ => new DispatchResult { Kind = DispatchKind.ShowPicker, Url = url }
            };
            if (string.IsNullOrWhiteSpace(res.RouteAction))
            {
                res.RouteAction = $"Rule:{rule.Action}";
            }
            return res;
        }

        private DispatchResult HandleRecentBrowser(string url)
        {
            var target = _findRecentBrowser?.Invoke();
            if (target == null || string.IsNullOrWhiteSpace(target.ExecutablePath))
            {
                return HandleRecentFallback(url);
            }

            var targetLabel = System.IO.Path.GetFileName(target.ExecutablePath);

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
                        Url = url,
                        RouteAction = "RecentBrowser:CDP",
                        TargetName = targetLabel
                    };
                }

                var copyOk = _copy(url);
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowClipboard,
                    Url = url,
                    CopyOk = copyOk,
                    Warning = L.Format("S_WarnCdpFallback", cdpErr),
                    RouteAction = "RecentBrowser:CdpFallback",
                    TargetName = targetLabel
                };
            }

            var res = LaunchOrFallbackToClipboard(target.ExecutablePath, target.ArgumentsTemplate, url, targetLabel, "RecentBrowser");
            if (res.Kind == DispatchKind.Done && res.ExitCode == 0 && target.HWnd != IntPtr.Zero)
            {
                try { _activateWindow?.Invoke(target.HWnd); } catch { }
            }

            return res;
        }

        private DispatchResult HandleRecentFallback(string url)
        {
            var res = _cfg.RecentBrowserFallback switch
            {
                RecentFallbackMode.ClipboardOnly => HandleClipboardOnly(url),
                RecentFallbackMode.DefaultBrowser => HandleDefaultBrowser(url),
                RecentFallbackMode.NativePassthrough => HandleNativePassthrough(url),
                _ => new DispatchResult { Kind = DispatchKind.ShowPicker, Url = url }
            };
            res.RouteAction = $"RecentFallback:{_cfg.RecentBrowserFallback}";
            return res;
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
                    Warning = L.Get("S_WarnNoOriginal"),
                    RouteAction = "NativePassthrough:FallbackPicker"
                };
            }

            var cmd = _resolveProgId(progId);
            if (string.IsNullOrWhiteSpace(cmd))
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    Warning = L.Format("S_WarnNoCommand", progId),
                    RouteAction = "NativePassthrough:FallbackPicker",
                    TargetName = progId
                };
            }

            var split = _splitCommand(cmd);
            if (!split.ok)
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    Warning = L.Format("S_WarnInvalidCommand", progId),
                    RouteAction = "NativePassthrough:FallbackPicker",
                    TargetName = progId
                };
            }

            return LaunchOrFallbackToClipboard(split.exe, split.args, url, progId, "NativePassthrough");
        }

        private DispatchResult HandleDefaultBrowser(string url)
        {
            return ExecuteBrowserTarget(_cfg.DefaultBrowser, url);
        }

        private DispatchResult ExecuteBrowserTarget(BrowserEntry target, string url)
        {
            if (target == null || (string.IsNullOrWhiteSpace(target.ExecutablePath) && string.IsNullOrWhiteSpace(target.UserDataDir)))
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    Warning = L.Get("S_WarnNoDefaultTarget"),
                    RouteAction = "OpenTarget:FallbackPicker"
                };
            }

            var targetLabel = string.IsNullOrWhiteSpace(target.Name) ? target.ExecutablePath : target.Name;

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
                        Url = url,
                        RouteAction = "OpenTarget:CDP",
                        TargetName = targetLabel
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
                        Warning = L.Format("S_WarnIsolatedNotRunning", target.Name, cdpErr),
                        RouteAction = "OpenTarget:IsolatedFallback",
                        TargetName = targetLabel
                    };
                }
            }

            if (target.Kind == BrowserKind.RunningWindow)
            {
                var copyOk = _copy(url);
                var reason = _cfg.EnableCdpForIsolatedBrowsers
                    ? "DevToolsActivePort unavailable"
                    : "CDP integration is disabled";
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowClipboard,
                    Url = url,
                    CopyOk = copyOk,
                    Warning = L.Format("S_WarnIsolatedNotRunning", target.Name, reason),
                    RouteAction = "OpenTarget:IsolatedFallback",
                    TargetName = targetLabel
                };
            }

            return LaunchOrFallbackToClipboard(target.ExecutablePath, target.ArgumentsTemplate, url, targetLabel, "OpenTarget");
        }

        private DispatchResult LaunchOrFallbackToClipboard(string exe, string argsTemplate, string url, string targetName = null, string routeAction = "Launch")
        {
            var (ok, err) = _launch(exe, argsTemplate, url);
            if (ok)
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.Done,
                    ExitCode = 0,
                    Url = url,
                    RouteAction = routeAction,
                    TargetName = string.IsNullOrWhiteSpace(targetName) ? exe : targetName
                };
            }

            var copyOk = _copy(url);
            return new DispatchResult
            {
                Kind = DispatchKind.ShowClipboard,
                Url = url,
                CopyOk = copyOk,
                Warning = L.Format("S_WarnLaunchFallback", err),
                RouteAction = $"{routeAction}:LaunchFallback",
                TargetName = string.IsNullOrWhiteSpace(targetName) ? exe : targetName
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
                    CopyOk = true,
                    RouteAction = "ClipboardOnly"
                };
            }

            return new DispatchResult
            {
                Kind = DispatchKind.ShowClipboard,
                Url = url,
                CopyOk = copyOk,
                Warning = copyOk ? string.Empty : L.Get("S_WarnClipBusy"),
                RouteAction = "ClipboardOnly"
            };
        }
    }
}
