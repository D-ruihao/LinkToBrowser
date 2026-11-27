using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using LinkToBrowser.Models;

namespace LinkToBrowser.Core
{
    public static class CliCommandHandler
    {
        private const uint AttachParentProcess = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        public static int Execute(
            CliInvocation inv,
            ConfigService configService,
            LogService logService,
            SystemRegistration registration,
            string selfExe,
            TextWriter output = null)
        {
            var ownsStdout = output == null;
            StreamWriter ownedWriter = null;
            if (ownsStdout)
            {
                output = OpenStdoutWriter(out ownedWriter);
            }

            try
            {
                return inv.Command switch
                {
                    "help" => HandleHelp(inv, output),
                    "status" => HandleStatus(inv, configService, logService, registration, selfExe, output),
                    "list-browsers" => HandleListBrowsers(inv, configService, selfExe, output),
                    "set-mode" => HandleSetMode(inv, configService, selfExe, output),
                    "test-url" => HandleTestUrl(inv, configService, registration, selfExe, output),
                    "rules" => HandleRules(inv, configService, selfExe, output),
                    "export" => HandleExport(inv, configService, output),
                    "import" => HandleImport(inv, configService, output),
                    "open-browser" => HandleOpenBrowser(inv, configService, logService, selfExe, output),
                    _ => WriteResult(inv, output, ok: false, error: $"Unknown command: {inv.Command}", data: null, exitCode: 1)
                };
            }
            catch (Exception ex)
            {
                return WriteResult(inv, output, ok: false, error: ex.Message, data: null, exitCode: 1);
            }
            finally
            {
                try { output.Flush(); } catch { }
                try { ownedWriter?.Dispose(); } catch { }
            }
        }

        public static int WriteError(CliInvocation inv, string error, int exitCode = 1, TextWriter output = null)
        {
            var ownsStdout = output == null;
            StreamWriter ownedWriter = null;
            if (ownsStdout)
            {
                output = OpenStdoutWriter(out ownedWriter);
            }

            try
            {
                return WriteResult(inv ?? new CliInvocation(), output, ok: false, error: error, data: null, exitCode: exitCode);
            }
            finally
            {
                try { output.Flush(); } catch { }
                try { ownedWriter?.Dispose(); } catch { }
            }
        }

        private static TextWriter OpenStdoutWriter(out StreamWriter ownedWriter)
        {
            ownedWriter = null;
            if (Console.IsOutputRedirected)
            {
                try
                {
                    ownedWriter = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
                    {
                        AutoFlush = true
                    };
                    return ownedWriter;
                }
                catch
                {
                }
            }

            EnsureConsoleAttached();
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
            return Console.Out;
        }

        private static void EnsureConsoleAttached()
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    AttachConsole(AttachParentProcess);
                }
            }
            catch
            {
            }
        }

        private static int HandleHelp(CliInvocation inv, TextWriter output)
        {
            var commands = new[]
            {
                new { command = "--status [--include-running] [--json]", description = "Show config, registration status, and summary" },
                new { command = "--list-browsers [--include-hidden] [--include-running] [--json]", description = "List discovered, custom, and active isolated browsers" },
                new { command = "--set-mode <native|default|picker|clipboard|recent> [--target <id|name>] [--fallback <picker|clipboard|default|native>] [--json]", description = "Set global routing mode" },
                new { command = "--test-url <url> [--source-app <exe>] [--json]", description = "Dry-run URL routing and rule matching without launching" },
                new { command = "--rules list [--json]", description = "List all URL routing rules" },
                new { command = "--rules add [--pattern <pat>] [--source-app <apps>] [--kind domain|prefix|wildcard] [--action open|clipboard|picker|recent|native] [--target <id|name>] [--name <label>] [--disabled] [--json]", description = "Add a routing rule" },
                new { command = "--rules enable|disable|remove <id|name> [--json]", description = "Enable, disable, or remove a routing rule" },
                new { command = "--browser <id|name> <url> [--json]", description = "Open URL directly in a specific browser or active CDP window" },
                new { command = "--export <path> [--rules-only] [--json]", description = "Export configuration or rules backup JSON" },
                new { command = "--import <path> [--replace] [--json]", description = "Import backup JSON (merge by default, or replace)" },
                new { command = "--config-dir <dir>", description = "Override config and log directory" }
            };

            if (inv.Json)
            {
                return WriteResult(inv, output, ok: true, error: null, data: new { version = GetAppVersion(), commands });
            }

            output.WriteLine($"LinkToBrowser v{GetAppVersion()} CLI");
            foreach (var c in commands)
            {
                output.WriteLine($"  {c.command}");
                output.WriteLine($"      {c.description}");
            }
            return 0;
        }

        private static int HandleStatus(
            CliInvocation inv,
            ConfigService configService,
            LogService logService,
            SystemRegistration registration,
            string selfExe,
            TextWriter output)
        {
            var cfg = configService.Load();
            var browsers = inv.IncludeRunning
                ? BrowserCatalog.GetAllWithRunning(cfg, selfExe, includeHidden: true)
                : BrowserCatalog.GetAll(cfg, selfExe, includeHidden: true);

            var data = new
            {
                version = GetAppVersion(),
                executable = selfExe,
                configPath = configService.ConfigPath,
                logDir = logService.LogDir,
                registeredCommand = registration.GetRegisteredCommand(),
                pathMatchesSelf = registration.PathMatchesSelf(),
                isSystemDefault = registration.IsSystemDefault(),
                httpUserChoice = registration.GetUserChoice("http"),
                httpsUserChoice = registration.GetUserChoice("https"),
                globalMode = cfg.GlobalMode.ToString(),
                recentFallback = cfg.RecentBrowserFallback.ToString(),
                defaultBrowser = cfg.DefaultBrowser == null ? null : new
                {
                    id = cfg.DefaultBrowser.Id,
                    name = cfg.DefaultBrowser.Name,
                    userDataDir = cfg.DefaultBrowser.UserDataDir
                },
                originalDefaultProgId = cfg.OriginalDefaultProgId,
                modifier = cfg.Modifier.ToString(),
                language = cfg.Language.ToString(),
                theme = cfg.Theme.ToString(),
                enableRoutingRules = cfg.EnableRoutingRules,
                enableCdp = cfg.EnableCdpForIsolatedBrowsers,
                unwrapRedirectUrls = cfg.UnwrapRedirectUrls,
                stripTrackingParams = cfg.StripTrackingParams,
                customTrackingParams = cfg.CustomTrackingParams,
                rulesTotal = cfg.Rules.Count,
                rulesEnabled = cfg.Rules.Count(r => r.Enabled),
                browsersTotal = browsers.Count
            };

            if (inv.Json)
            {
                return WriteResult(inv, output, ok: true, error: null, data: data);
            }

            output.WriteLine($"Version:      {data.version}");
            output.WriteLine($"ConfigPath:   {data.configPath}");
            output.WriteLine($"GlobalMode:   {data.globalMode}");
            output.WriteLine($"RulesEnabled: {data.enableRoutingRules} ({data.rulesEnabled}/{data.rulesTotal} active)");
            output.WriteLine($"SystemDef:    {data.isSystemDefault} (http={data.httpUserChoice}, https={data.httpsUserChoice})");
            return 0;
        }

        private static int HandleListBrowsers(CliInvocation inv, ConfigService configService, string selfExe, TextWriter output)
        {
            var cfg = configService.Load();
            var hidden = new HashSet<string>(cfg.HiddenBrowserIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var list = inv.IncludeRunning
                ? BrowserCatalog.GetAllWithRunning(cfg, selfExe, inv.IncludeHidden)
                : BrowserCatalog.GetAll(cfg, selfExe, inv.IncludeHidden);

            var rows = list.Select(b => new
            {
                id = b.Id,
                name = b.Name,
                kind = b.Kind.ToString(),
                executablePath = b.ExecutablePath,
                argumentsTemplate = b.ArgumentsTemplate,
                progId = b.ProgId,
                userDataDir = b.UserDataDir,
                hidden = hidden.Contains(b.Id)
            }).ToList();

            if (inv.Json)
            {
                return WriteResult(inv, output, ok: true, error: null, data: new { count = rows.Count, browsers = rows });
            }

            foreach (var r in rows)
            {
                output.WriteLine($"[{r.kind}] {r.name}  ({r.id})");
                output.WriteLine($"    exe: {r.executablePath} {r.argumentsTemplate}");
            }
            return 0;
        }

        private static int HandleSetMode(CliInvocation inv, ConfigService configService, string selfExe, TextWriter output)
        {
            if (!TryParseGlobalMode(inv.ModeArg, out var mode))
            {
                return WriteResult(inv, output, ok: false, error: $"Invalid mode '{inv.ModeArg}'. Expected: native|default|picker|clipboard|recent.", data: null, exitCode: 2);
            }

            var cfg = configService.Load();
            cfg.GlobalMode = mode;

            if (!string.IsNullOrWhiteSpace(inv.TargetArg))
            {
                var browsers = BrowserCatalog.GetAllWithRunning(cfg, selfExe, includeHidden: true);
                var matched = MatchBrowser(browsers, inv.TargetArg);
                if (matched == null)
                {
                    return WriteResult(inv, output, ok: false, error: $"Target browser not found: {inv.TargetArg}", data: null, exitCode: 2);
                }

                cfg.DefaultBrowser = CloneBrowser(matched);
            }

            if (!string.IsNullOrWhiteSpace(inv.FallbackArg))
            {
                if (!TryParseFallbackMode(inv.FallbackArg, out var fb))
                {
                    return WriteResult(inv, output, ok: false, error: $"Invalid fallback '{inv.FallbackArg}'. Expected: picker|clipboard|default|native.", data: null, exitCode: 2);
                }

                cfg.RecentBrowserFallback = fb;
            }

            configService.Save(cfg);
            var resData = new
            {
                globalMode = cfg.GlobalMode.ToString(),
                recentFallback = cfg.RecentBrowserFallback.ToString(),
                defaultBrowser = cfg.DefaultBrowser?.Name,
                configPath = configService.ConfigPath
            };

            return WriteResult(inv, output, ok: true, error: null, data: resData, plainMessage: $"Mode updated to {cfg.GlobalMode}");
        }

        private static int HandleTestUrl(
            CliInvocation inv,
            ConfigService configService,
            SystemRegistration registration,
            string selfExe,
            TextWriter output)
        {
            if (!UrlService.TryNormalize(inv.RawUrl, out var url))
            {
                return WriteResult(inv, output, ok: false, error: "Invalid or unsupported URL.", data: null, exitCode: 2);
            }

            var cfg = configService.Load();
            var unwrappedUrl = cfg.UnwrapRedirectUrls ? UrlService.UnwrapRedirectUrl(url) : url;
            var effectiveUrl = cfg.StripTrackingParams ? UrlService.StripTracking(unwrappedUrl, cfg.CustomTrackingParams) : unwrappedUrl;
            var sourceApp = ProcessInfoHelper.NormalizeExeFileName(inv.SourceAppArg);
            var matchedRule = cfg.EnableRoutingRules ? RuleMatcher.Match(effectiveUrl, cfg.Rules, sourceApp) : null;

            string plannedAction;
            string targetDesc = string.Empty;

            if (matchedRule != null)
            {
                plannedAction = $"Rule:{matchedRule.Action}";
                targetDesc = matchedRule.Action == RuleActionKind.OpenTarget
                    ? (matchedRule.TargetBrowser?.Name ?? "(unset)")
                    : matchedRule.Action.ToString();
            }
            else
            {
                plannedAction = $"Global:{cfg.GlobalMode}";
                if (cfg.GlobalMode == GlobalMode.DefaultBrowser)
                {
                    targetDesc = cfg.DefaultBrowser?.Name ?? "(unset)";
                }
                else if (cfg.GlobalMode == GlobalMode.NativePassthrough)
                {
                    targetDesc = cfg.OriginalDefaultProgId;
                }
            }

            var data = new
            {
                inputUrl = inv.RawUrl,
                normalizedUrl = url,
                unwrappedUrl,
                effectiveUrl,
                sourceApp,
                matchedRule = matchedRule == null ? null : new
                {
                    id = matchedRule.Id,
                    name = matchedRule.Name,
                    matchKind = matchedRule.MatchKind.ToString(),
                    pattern = matchedRule.Pattern,
                    sourceApps = matchedRule.SourceApps,
                    action = matchedRule.Action.ToString(),
                    targetBrowser = matchedRule.TargetBrowser?.Name
                },
                plannedAction,
                target = targetDesc
            };

            if (inv.Json)
            {
                return WriteResult(inv, output, ok: true, error: null, data: data);
            }

            output.WriteLine($"URL:    {effectiveUrl}");
            output.WriteLine($"Action: {plannedAction} -> {targetDesc}");
            return 0;
        }

        private static int HandleRules(CliInvocation inv, ConfigService configService, string selfExe, TextWriter output)
        {
            var cfg = configService.Load();
            var sub = (inv.SubCommand ?? "list").ToLowerInvariant();

            switch (sub)
            {
                case "list":
                {
                    var items = cfg.Rules.Select((r, i) => new
                    {
                        index = i,
                        id = r.Id,
                        enabled = r.Enabled,
                        name = r.Name,
                        matchKind = r.MatchKind.ToString(),
                        pattern = r.Pattern,
                        sourceApps = r.SourceApps,
                        action = r.Action.ToString(),
                        targetBrowser = r.TargetBrowser?.Name
                    }).ToList();

                    if (inv.Json)
                    {
                        return WriteResult(inv, output, ok: true, error: null, data: new { enabled = cfg.EnableRoutingRules, count = items.Count, rules = items });
                    }

                    foreach (var r in items)
                    {
                        var srcPart = string.IsNullOrWhiteSpace(r.sourceApps) ? string.Empty : $" from {r.sourceApps}";
                        output.WriteLine($"[{(r.enabled ? "ON" : "OFF")}] {r.name} ({r.matchKind}: {r.pattern}{srcPart}) -> {r.action} {r.targetBrowser} [id={r.id}]");
                    }
                    return 0;
                }

                case "add":
                {
                    if (!RuleMatcher.HasValidTokens(inv.PatternArg) && !RuleMatcher.HasValidTokens(inv.SourceAppArg))
                    {
                        return WriteResult(inv, output, ok: false, error: "--pattern or --source-app is required and must contain at least one valid token.", data: null, exitCode: 2);
                    }

                    if (!string.IsNullOrWhiteSpace(inv.IdArg) &&
                        cfg.Rules.Any(r => string.Equals(r.Id, inv.IdArg, StringComparison.OrdinalIgnoreCase)))
                    {
                        return WriteResult(inv, output, ok: false, error: $"Rule id already exists: {inv.IdArg}", data: null, exitCode: 2);
                    }

                    var matchKind = RuleMatchKind.DomainSuffix;
                    if (!string.IsNullOrWhiteSpace(inv.MatchKindArg) && !TryParseMatchKind(inv.MatchKindArg, out matchKind))
                    {
                        return WriteResult(inv, output, ok: false, error: $"Invalid --kind '{inv.MatchKindArg}'. Expected: domain|prefix|wildcard.", data: null, exitCode: 2);
                    }

                    var action = string.IsNullOrWhiteSpace(inv.TargetArg) ? RuleActionKind.PickerPopup : RuleActionKind.OpenTarget;
                    if (!string.IsNullOrWhiteSpace(inv.ActionArg) && !TryParseRuleAction(inv.ActionArg, out action))
                    {
                        return WriteResult(inv, output, ok: false, error: $"Invalid --action '{inv.ActionArg}'. Expected: open|clipboard|picker|recent|native.", data: null, exitCode: 2);
                    }

                    BrowserEntry targetBrowser = null;
                    if (action == RuleActionKind.OpenTarget)
                    {
                        if (string.IsNullOrWhiteSpace(inv.TargetArg))
                        {
                            return WriteResult(inv, output, ok: false, error: "--target is required when --action is open.", data: null, exitCode: 2);
                        }

                        var browsers = BrowserCatalog.GetAllWithRunning(cfg, selfExe, includeHidden: true);
                        targetBrowser = MatchBrowser(browsers, inv.TargetArg);
                        if (targetBrowser == null)
                        {
                            return WriteResult(inv, output, ok: false, error: $"Target browser not found: {inv.TargetArg}", data: null, exitCode: 2);
                        }
                        targetBrowser = CloneBrowser(targetBrowser);
                    }

                    var defaultName = !string.IsNullOrWhiteSpace(inv.PatternArg) ? inv.PatternArg : inv.SourceAppArg;
                    var newRule = new RoutingRule
                    {
                        Id = string.IsNullOrWhiteSpace(inv.IdArg) ? $"rule:{Guid.NewGuid():N}" : inv.IdArg,
                        Enabled = !inv.Disabled,
                        Name = string.IsNullOrWhiteSpace(inv.NameArg) ? defaultName : inv.NameArg,
                        MatchKind = matchKind,
                        Pattern = (inv.PatternArg ?? string.Empty).Trim(),
                        SourceApps = (inv.SourceAppArg ?? string.Empty).Trim(),
                        Action = action,
                        TargetBrowser = targetBrowser
                    };

                    cfg.Rules.Add(newRule);
                    cfg.SampleRulesSeeded = true;
                    configService.Save(cfg);

                    var ruleView = new
                    {
                        id = newRule.Id,
                        enabled = newRule.Enabled,
                        name = newRule.Name,
                        matchKind = newRule.MatchKind.ToString(),
                        pattern = newRule.Pattern,
                        sourceApps = newRule.SourceApps,
                        action = newRule.Action.ToString(),
                        targetBrowser = newRule.TargetBrowser?.Name
                    };

                    return WriteResult(inv, output, ok: true, error: null, data: new { rule = ruleView }, plainMessage: $"Added rule '{newRule.Name}' ({newRule.Id})");
                }

                case "enable":
                case "disable":
                case "remove":
                {
                    var query = !string.IsNullOrWhiteSpace(inv.IdArg) ? inv.IdArg : inv.NameArg;
                    if (string.IsNullOrWhiteSpace(query))
                    {
                        return WriteResult(inv, output, ok: false, error: "Rule id or name is required.", data: null, exitCode: 2);
                    }

                    var idx = cfg.Rules.FindIndex(r =>
                        string.Equals(r.Id, query, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.Name, query, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.Pattern, query, StringComparison.OrdinalIgnoreCase));

                    if (idx < 0)
                    {
                        return WriteResult(inv, output, ok: false, error: $"Rule not found: {query}", data: null, exitCode: 2);
                    }

                    var target = cfg.Rules[idx];
                    if (sub == "remove")
                    {
                        cfg.Rules.RemoveAt(idx);
                    }
                    else
                    {
                        target.Enabled = sub == "enable";
                    }

                    configService.Save(cfg);
                    return WriteResult(inv, output, ok: true, error: null, data: new { subCommand = sub, id = target.Id, name = target.Name }, plainMessage: $"Rule '{target.Name}' {sub}d.");
                }

                default:
                    return WriteResult(inv, output, ok: false, error: $"Unknown --rules subcommand '{inv.SubCommand}'. Expected: list|add|enable|disable|remove.", data: null, exitCode: 2);
            }
        }

        private static int HandleExport(CliInvocation inv, ConfigService configService, TextWriter output)
        {
            if (string.IsNullOrWhiteSpace(inv.FileArg))
            {
                return WriteResult(inv, output, ok: false, error: "--export requires a file path.", data: null, exitCode: 2);
            }

            var cfg = configService.Load();
            var json = ConfigBackupService.ExportToJson(cfg, inv.RulesOnly);
            var fullPath = Path.GetFullPath(inv.FileArg);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            ConfigBackupService.WriteTextUtf8(fullPath, json);
            return WriteResult(inv, output, ok: true, error: null, data: new { path = fullPath, rulesOnly = inv.RulesOnly }, plainMessage: $"Exported backup to {fullPath}");
        }

        private static int HandleImport(CliInvocation inv, ConfigService configService, TextWriter output)
        {
            if (string.IsNullOrWhiteSpace(inv.FileArg) || !File.Exists(inv.FileArg))
            {
                return WriteResult(inv, output, ok: false, error: $"Backup file not found: {inv.FileArg}", data: null, exitCode: 2);
            }

            var raw = File.ReadAllText(inv.FileArg);
            if (!ConfigBackupService.TryParseBackup(raw, out var payload, out var parseErr))
            {
                return WriteResult(inv, output, ok: false, error: parseErr, data: null, exitCode: 2);
            }

            var current = configService.Load();
            ConfigBackupService.CreatePreImportBackupIfExists(configService.ConfigPath);
            var updated = ConfigBackupService.ApplyImport(current, payload, inv.Replace);
            configService.Save(updated);

            return WriteResult(inv, output, ok: true, error: null, data: new
            {
                replaced = inv.Replace,
                rulesOnly = payload.RulesOnly,
                rulesCount = updated.Rules.Count,
                customBrowsersCount = updated.CustomBrowsers.Count,
                configPath = configService.ConfigPath
            }, plainMessage: $"Imported backup ({updated.Rules.Count} rules).");
        }

        private static int HandleOpenBrowser(
            CliInvocation inv,
            ConfigService configService,
            LogService logService,
            string selfExe,
            TextWriter output)
        {
            if (!UrlService.TryNormalize(inv.RawUrl, out var url))
            {
                return WriteResult(inv, output, ok: false, error: "Invalid or unsupported URL.", data: null, exitCode: 2);
            }

            var cfg = configService.Load();
            url = UrlService.Prepare(url, cfg);

            var browsers = BrowserCatalog.GetAllWithRunning(cfg, selfExe, includeHidden: true);
            var target = MatchBrowser(browsers, inv.BrowserArg);
            if (target == null)
            {
                return WriteResult(inv, output, ok: false, error: $"Browser not found: {inv.BrowserArg}", data: null, exitCode: 2);
            }

            if (cfg.EnableCdpForIsolatedBrowsers &&
                !string.IsNullOrWhiteSpace(target.UserDataDir) &&
                CdpClient.HasDevToolsActivePort(target.UserDataDir))
            {
                if (CdpClient.TryOpenTab(target.UserDataDir, url, out var cdpErr))
                {
                    var hwnd = RecentBrowserService.FindWindowByUserDataDir(target.UserDataDir, selfExe);
                    if (hwnd != IntPtr.Zero)
                    {
                        RecentBrowserService.ActivateWindow(hwnd);
                    }

                    logService.LogLink(url, true, $"action=CliOpen:CDP | target={target.Name}");
                    return WriteResult(inv, output, ok: true, error: null, data: new { method = "cdp", target = target.Name, url }, plainMessage: $"Opened in {target.Name} via CDP");
                }

                if (target.Kind == BrowserKind.RunningWindow)
                {
                    logService.LogLink(url, false, $"action=CliOpen:CDP | target={target.Name} | warn={cdpErr}");
                    return WriteResult(inv, output, ok: false, error: cdpErr, data: null, exitCode: 2);
                }
            }

            if (target.Kind == BrowserKind.RunningWindow)
            {
                var errMsg = cfg.EnableCdpForIsolatedBrowsers
                    ? $"Target isolated browser ({target.Name}) DevToolsActivePort is unavailable."
                    : $"Target isolated browser ({target.Name}) requires CDP integration to be enabled.";
                logService.LogLink(url, false, $"action=CliOpen:Isolated | target={target.Name} | warn={errMsg}");
                return WriteResult(inv, output, ok: false, error: errMsg, data: null, exitCode: 2);
            }

            var launcher = new BrowserLauncher(selfExe);
            if (!launcher.TryLaunch(target.ExecutablePath, target.ArgumentsTemplate, url, out var launchErr))
            {
                logService.LogLink(url, false, $"action=CliOpen:Process | target={target.Name} | warn={launchErr}");
                return WriteResult(inv, output, ok: false, error: launchErr, data: null, exitCode: 2);
            }

            logService.LogLink(url, true, $"action=CliOpen:Process | target={target.Name}");
            return WriteResult(inv, output, ok: true, error: null, data: new { method = "process", target = target.Name, url }, plainMessage: $"Opened in {target.Name}");
        }

        private static BrowserEntry MatchBrowser(IReadOnlyList<BrowserEntry> browsers, string query)
        {
            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0 || browsers == null) return null;

            return browsers.FirstOrDefault(b => string.Equals(b.Id, q, StringComparison.OrdinalIgnoreCase))
                ?? browsers.FirstOrDefault(b => string.Equals(b.Name, q, StringComparison.OrdinalIgnoreCase))
                ?? browsers.FirstOrDefault(b => (b.Name ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? browsers.FirstOrDefault(b => (b.ProgId ?? string.Empty).Equals(q, StringComparison.OrdinalIgnoreCase));
        }

        private static BrowserEntry CloneBrowser(BrowserEntry b)
        {
            return new BrowserEntry
            {
                Id = b.Id,
                Name = b.Name,
                ExecutablePath = b.ExecutablePath,
                ArgumentsTemplate = b.ArgumentsTemplate,
                ProgId = b.ProgId,
                UserDataDir = b.UserDataDir,
                Kind = b.Kind
            };
        }

        private static bool TryParseGlobalMode(string raw, out GlobalMode mode)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "native":
                case "nativepassthrough":
                    mode = GlobalMode.NativePassthrough; return true;
                case "default":
                case "defaultbrowser":
                case "specific":
                    mode = GlobalMode.DefaultBrowser; return true;
                case "picker":
                case "pickerpopup":
                    mode = GlobalMode.PickerPopup; return true;
                case "clipboard":
                case "clipboardonly":
                    mode = GlobalMode.ClipboardOnly; return true;
                case "recent":
                case "recentbrowser":
                    mode = GlobalMode.RecentBrowser; return true;
                default:
                    mode = GlobalMode.PickerPopup; return false;
            }
        }

        private static bool TryParseFallbackMode(string raw, out RecentFallbackMode mode)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "picker":
                case "pickerpopup":
                    mode = RecentFallbackMode.PickerPopup; return true;
                case "clipboard":
                case "clipboardonly":
                    mode = RecentFallbackMode.ClipboardOnly; return true;
                case "default":
                case "defaultbrowser":
                    mode = RecentFallbackMode.DefaultBrowser; return true;
                case "native":
                case "nativepassthrough":
                    mode = RecentFallbackMode.NativePassthrough; return true;
                default:
                    mode = RecentFallbackMode.PickerPopup; return false;
            }
        }

        private static bool TryParseMatchKind(string raw, out RuleMatchKind kind)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "domain":
                case "domainsuffix":
                    kind = RuleMatchKind.DomainSuffix; return true;
                case "prefix":
                case "urlprefix":
                    kind = RuleMatchKind.UrlPrefix; return true;
                case "wildcard":
                case "glob":
                    kind = RuleMatchKind.Wildcard; return true;
                default:
                    kind = RuleMatchKind.DomainSuffix; return false;
            }
        }

        private static bool TryParseRuleAction(string raw, out RuleActionKind action)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "open":
                case "opentarget":
                case "browser":
                    action = RuleActionKind.OpenTarget; return true;
                case "clipboard":
                case "clipboardonly":
                    action = RuleActionKind.ClipboardOnly; return true;
                case "picker":
                case "pickerpopup":
                    action = RuleActionKind.PickerPopup; return true;
                case "recent":
                case "recentbrowser":
                    action = RuleActionKind.RecentBrowser; return true;
                case "native":
                case "nativepassthrough":
                    action = RuleActionKind.NativePassthrough; return true;
                default:
                    action = RuleActionKind.OpenTarget; return false;
            }
        }

        private static string GetAppVersion()
        {
            var v = typeof(CliCommandHandler).Assembly.GetName().Version;
            return v == null ? "2.1.1" : $"{v.Major}.{v.Minor}.{v.Build}";
        }

        private static int WriteResult(CliInvocation inv, TextWriter output, bool ok, string error, object data, string plainMessage = null, int exitCode = 0)
        {
            if (inv.Json)
            {
                var envelope = new Dictionary<string, object>
                {
                    ["ok"] = ok,
                    ["error"] = error,
                    ["data"] = data
                };
                output.WriteLine(new JavaScriptSerializer().Serialize(envelope));
                return ok ? 0 : exitCode;
            }

            if (!ok)
            {
                output.WriteLine("Error: " + (error ?? "Unknown error"));
                return exitCode;
            }

            if (!string.IsNullOrEmpty(plainMessage))
            {
                output.WriteLine(plainMessage);
            }

            return 0;
        }
    }
}
