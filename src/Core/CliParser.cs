using System;
using System.Collections.Generic;

namespace LinkToBrowser.Core
{
    public enum CliKind
    {
        OpenSettings,
        Register,
        Unregister,
        Ignore,
        HandleUrl,
        ScriptCommand
    }

    public sealed class CliInvocation
    {
        public CliKind Kind { get; set; }
        public string RawUrl { get; set; } = string.Empty;
        public bool NoUi { get; set; }
        public bool Json { get; set; }
        public bool IncludeHidden { get; set; }
        public bool IncludeRunning { get; set; }
        public bool RulesOnly { get; set; }
        public bool Replace { get; set; }
        public bool Disabled { get; set; }
        public string ConfigDir { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string SubCommand { get; set; } = string.Empty;
        public string ModeArg { get; set; } = string.Empty;
        public string TargetArg { get; set; } = string.Empty;
        public string FallbackArg { get; set; } = string.Empty;
        public string PatternArg { get; set; } = string.Empty;
        public string MatchKindArg { get; set; } = string.Empty;
        public string ActionArg { get; set; } = string.Empty;
        public string NameArg { get; set; } = string.Empty;
        public string FileArg { get; set; } = string.Empty;
        public string IdArg { get; set; } = string.Empty;
        public string BrowserArg { get; set; } = string.Empty;
        public string SourceAppArg { get; set; } = string.Empty;
    }

    public static class CliParser
    {
        public static CliInvocation Parse(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return new CliInvocation { Kind = CliKind.OpenSettings };
            }

            var inv = new CliInvocation();
            var openSettings = false;
            var register = false;
            var unregister = false;
            var parts = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                var raw = args[i];
                if (raw == null) continue;

                var trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;

                if (EqualsIgnoreCase(trimmed, "--no-ui"))
                {
                    inv.NoUi = true;
                }
                else if (EqualsIgnoreCase(trimmed, "--json"))
                {
                    inv.Json = true;
                }
                else if (EqualsIgnoreCase(trimmed, "--include-hidden"))
                {
                    inv.IncludeHidden = true;
                }
                else if (EqualsIgnoreCase(trimmed, "--include-running"))
                {
                    inv.IncludeRunning = true;
                }
                else if (EqualsIgnoreCase(trimmed, "--rules-only"))
                {
                    inv.RulesOnly = true;
                }
                else if (EqualsIgnoreCase(trimmed, "--replace"))
                {
                    inv.Replace = true;
                }
                else if (EqualsIgnoreCase(trimmed, "--disabled"))
                {
                    inv.Disabled = true;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--config-dir", out var cfgDir) ||
                         TryTakeFlagValue(args, ref i, trimmed, "--config", out cfgDir))
                {
                    inv.ConfigDir = cfgDir;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--browser", out var browserVal))
                {
                    inv.BrowserArg = browserVal;
                    if (string.IsNullOrEmpty(inv.Command))
                    {
                        inv.Command = "open-browser";
                    }
                }
                else if (EqualsIgnoreCase(trimmed, "--help") || EqualsIgnoreCase(trimmed, "-h"))
                {
                    inv.Command = "help";
                }
                else if (EqualsIgnoreCase(trimmed, "--status") || EqualsIgnoreCase(trimmed, "--info"))
                {
                    inv.Command = "status";
                }
                else if (EqualsIgnoreCase(trimmed, "--list-browsers"))
                {
                    inv.Command = "list-browsers";
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--set-mode", out var modeVal))
                {
                    inv.Command = "set-mode";
                    inv.ModeArg = modeVal;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--test-url", out var testUrl))
                {
                    inv.Command = "test-url";
                    inv.RawUrl = testUrl;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--export", out var exportFile))
                {
                    inv.Command = "export";
                    inv.FileArg = exportFile;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--import", out var importFile))
                {
                    inv.Command = "import";
                    inv.FileArg = importFile;
                }
                else if (EqualsIgnoreCase(trimmed, "--rules"))
                {
                    inv.Command = "rules";
                    if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]) && !args[i + 1].Trim().StartsWith("-", StringComparison.Ordinal))
                    {
                        inv.SubCommand = args[++i].Trim();
                        if ((EqualsIgnoreCase(inv.SubCommand, "remove") ||
                             EqualsIgnoreCase(inv.SubCommand, "enable") ||
                             EqualsIgnoreCase(inv.SubCommand, "disable")) &&
                            i + 1 < args.Length &&
                            !string.IsNullOrWhiteSpace(args[i + 1]) &&
                            !args[i + 1].Trim().StartsWith("-", StringComparison.Ordinal))
                        {
                            inv.IdArg = args[++i].Trim();
                        }
                    }
                    else
                    {
                        inv.SubCommand = "list";
                    }
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--target", out var targetVal))
                {
                    inv.TargetArg = targetVal;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--fallback", out var fallbackVal))
                {
                    inv.FallbackArg = fallbackVal;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--pattern", out var patVal))
                {
                    inv.PatternArg = patVal;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--kind", out var kindVal))
                {
                    inv.MatchKindArg = kindVal;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--action", out var actionVal))
                {
                    inv.ActionArg = actionVal;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--name", out var nameVal))
                {
                    inv.NameArg = nameVal;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--id", out var idVal))
                {
                    inv.IdArg = idVal;
                }
                else if (TryTakeFlagValue(args, ref i, trimmed, "--source-app", out var srcVal) ||
                         TryTakeFlagValue(args, ref i, trimmed, "--source-apps", out srcVal))
                {
                    inv.SourceAppArg = srcVal;
                }
                else if (EqualsIgnoreCase(trimmed, "--settings"))
                {
                    openSettings = true;
                }
                else if (EqualsIgnoreCase(trimmed, "--register"))
                {
                    register = true;
                }
                else if (EqualsIgnoreCase(trimmed, "--unregister"))
                {
                    unregister = true;
                }
                else if (trimmed.StartsWith("--url=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = raw.Substring(raw.IndexOf('=') + 1);
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        parts.Add(val);
                    }
                }
                else if (trimmed.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }
                else
                {
                    parts.Add(raw);
                }
            }

            if (register)
            {
                inv.Kind = CliKind.Register;
                return inv;
            }

            if (unregister)
            {
                inv.Kind = CliKind.Unregister;
                return inv;
            }

            if (!string.IsNullOrEmpty(inv.Command))
            {
                if (parts.Count > 0 && string.IsNullOrEmpty(inv.RawUrl))
                {
                    inv.RawUrl = string.Join(" ", parts);
                }

                inv.Kind = CliKind.ScriptCommand;
                return inv;
            }

            if (parts.Count > 0)
            {
                inv.Kind = CliKind.HandleUrl;
                inv.RawUrl = string.Join(" ", parts);
                return inv;
            }

            inv.Kind = openSettings ? CliKind.OpenSettings : CliKind.Ignore;
            return inv;
        }

        private static bool EqualsIgnoreCase(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryTakeFlagValue(string[] args, ref int index, string trimmed, string flagName, out string value)
        {
            value = string.Empty;
            if (trimmed.StartsWith(flagName + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = args[index].Substring(args[index].IndexOf('=') + 1).Trim();
                return true;
            }

            if (EqualsIgnoreCase(trimmed, flagName))
            {
                if (index + 1 < args.Length && args[index + 1] != null)
                {
                    var next = args[index + 1].Trim();
                    if (!next.StartsWith("--", StringComparison.Ordinal))
                    {
                        value = next;
                        index++;
                    }
                }
                return true;
            }

            return false;
        }
    }
}
