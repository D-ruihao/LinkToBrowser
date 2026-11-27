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

        public LinkDispatcher(
            AppConfig cfg,
            Func<string, string, string, (bool ok, string err)> launch,
            Func<string, bool> copy,
            Func<string, string> resolveProgId,
            Func<ModifierKey, bool> isKeyDown,
            Func<string, (bool ok, string exe, string args)> splitCommand)
        {
            _cfg = cfg ?? new AppConfig();
            _launch = launch;
            _copy = copy;
            _resolveProgId = resolveProgId;
            _isKeyDown = isKeyDown;
            _splitCommand = splitCommand;
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
                    Warning = "未配置原系统默认浏览器，请从下方选择或前往设置指定。"
                };
            }

            var cmd = _resolveProgId(progId);
            if (string.IsNullOrWhiteSpace(cmd))
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    Warning = $"原默认浏览器 ({progId}) 无命令行入口，请从下方选择。"
                };
            }

            var split = _splitCommand(cmd);
            if (!split.ok)
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    Warning = $"原默认浏览器 ({progId}) 启动程序不可用，请从下方选择。"
                };
            }

            return LaunchOrFallbackToClipboard(split.exe, split.args, url);
        }

        private DispatchResult HandleDefaultBrowser(string url)
        {
            var target = _cfg.DefaultBrowser;
            if (target == null || string.IsNullOrWhiteSpace(target.ExecutablePath))
            {
                return new DispatchResult
                {
                    Kind = DispatchKind.ShowPicker,
                    Url = url,
                    Warning = "尚未指定默认浏览器，请从下方选择。"
                };
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
                Warning = $"启动浏览器失败，已尝试复制到剪贴板兜底：{err}"
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
                Warning = copyOk ? string.Empty : "剪贴板被占用，复制失败：URL 已写入 links.log，请手动从下方复制。"
            };
        }
    }
}
