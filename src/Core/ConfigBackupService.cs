using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using LinkToBrowser.Models;

namespace LinkToBrowser.Core
{
    public sealed class ConfigBackupPayload
    {
        public int BackupFormatVersion { get; set; } = 1;
        public bool RulesOnly { get; set; }
        public string ExportedAtUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public AppConfig Config { get; set; } = new AppConfig();
    }

    public static class ConfigBackupService
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string ExportToJson(AppConfig cfg, bool rulesAndCustomOnly)
        {
            var normalized = CloneConfig(cfg ?? new AppConfig());
            if (rulesAndCustomOnly)
            {
                normalized = new AppConfig
                {
                    ConfigVersion = normalized.ConfigVersion,
                    EnableRoutingRules = normalized.EnableRoutingRules,
                    Rules = normalized.Rules,
                    CustomBrowsers = normalized.CustomBrowsers
                };
            }

            var payload = new ConfigBackupPayload
            {
                BackupFormatVersion = 1,
                RulesOnly = rulesAndCustomOnly,
                ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                Config = normalized
            };

            var rawJson = new JavaScriptSerializer().Serialize(payload);
            return FormatJson(rawJson);
        }

        public static bool TryParseBackup(string json, out ConfigBackupPayload payload, out string error)
        {
            payload = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Backup file is empty.";
                return false;
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                var rawObj = serializer.Deserialize<Dictionary<string, object>>(json);
                if (rawObj == null)
                {
                    error = "Invalid JSON object.";
                    return false;
                }

                bool HasKey(string name) => rawObj.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

                // Support both wrapped ConfigBackupPayload and raw AppConfig (config.json directly)
                if (HasKey("Config"))
                {
                    payload = serializer.Deserialize<ConfigBackupPayload>(json);
                }
                else if (HasKey("ConfigVersion") || HasKey("GlobalMode") || HasKey("Rules"))
                {
                    var directCfg = serializer.Deserialize<AppConfig>(json);
                    payload = new ConfigBackupPayload
                    {
                        BackupFormatVersion = 1,
                        RulesOnly = false,
                        Config = directCfg
                    };
                }
                else
                {
                    error = "Unrecognized LinkToBrowser backup format.";
                    return false;
                }

                if (payload?.Config == null)
                {
                    error = "Missing config data in backup.";
                    return false;
                }

                payload.Config.Normalize();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static AppConfig ApplyImport(AppConfig current, ConfigBackupPayload imported, bool replaceAll)
        {
            var cur = CloneConfig(current ?? new AppConfig());
            var inc = CloneConfig(imported?.Config ?? new AppConfig());
            var isRulesOnly = imported?.RulesOnly ?? false;

            if (replaceAll && !isRulesOnly)
            {
                // Keep machine-specific OriginalDefaultProgId if current machine already has a valid snapshot
                if (!string.IsNullOrWhiteSpace(cur.OriginalDefaultProgId) &&
                    !string.Equals(cur.OriginalDefaultProgId, SystemRegistration.ProgId, StringComparison.OrdinalIgnoreCase))
                {
                    inc.OriginalDefaultProgId = cur.OriginalDefaultProgId;
                }

                return inc.Normalize();
            }

            if (replaceAll && isRulesOnly)
            {
                // Replace only rules & custom browsers while preserving local machine settings
                cur.EnableRoutingRules = inc.EnableRoutingRules;
                cur.Rules = inc.Rules;
                cur.CustomBrowsers = inc.CustomBrowsers;
                cur.SampleRulesSeeded = true;
                return cur.Normalize();
            }

            // Merge rules and custom browsers without overwriting local settings
            var existingCustomIds = new HashSet<string>(
                cur.CustomBrowsers.Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id)).Select(c => c.Id),
                StringComparer.OrdinalIgnoreCase);

            foreach (var c in inc.CustomBrowsers)
            {
                if (c != null && !string.IsNullOrWhiteSpace(c.ExecutablePath) && existingCustomIds.Add(c.Id))
                {
                    cur.CustomBrowsers.Add(c);
                }
            }

            var existingRuleKeys = new HashSet<string>(
                cur.Rules.Select(r => $"{r.MatchKind}|{r.Pattern}|{r.SourceApps}"),
                StringComparer.OrdinalIgnoreCase);
            var existingRuleIds = new HashSet<string>(
                cur.Rules.Where(r => !string.IsNullOrWhiteSpace(r.Id)).Select(r => r.Id),
                StringComparer.OrdinalIgnoreCase);

            foreach (var r in inc.Rules)
            {
                if (r == null || (!RuleMatcher.HasValidTokens(r.Pattern) && !RuleMatcher.HasValidTokens(r.SourceApps)))
                {
                    continue;
                }

                var key = $"{r.MatchKind}|{r.Pattern}|{r.SourceApps}";
                if (existingRuleKeys.Add(key))
                {
                    if (string.IsNullOrWhiteSpace(r.Id) || !existingRuleIds.Add(r.Id))
                    {
                        r.Id = $"rule:{Guid.NewGuid():N}";
                        existingRuleIds.Add(r.Id);
                    }

                    cur.Rules.Add(r);
                }
            }

            if (inc.Rules.Count > 0)
            {
                cur.SampleRulesSeeded = true;
            }

            return cur.Normalize();
        }

        public static void CreatePreImportBackupIfExists(string configPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
                {
                    var dir = Path.GetDirectoryName(configPath) ?? string.Empty;
                    var bakPath = Path.Combine(dir, "config.bak.json");
                    File.Copy(configPath, bakPath, overwrite: true);
                }
            }
            catch
            {
            }
        }

        public static void WriteTextUtf8(string path, string content)
        {
            File.WriteAllText(path, content ?? string.Empty, Utf8NoBom);
        }

        private static AppConfig CloneConfig(AppConfig cfg)
        {
            var ser = new JavaScriptSerializer();
            var clone = ser.Deserialize<AppConfig>(ser.Serialize(cfg));
            return (clone ?? new AppConfig()).Normalize();
        }

        private static string FormatJson(string json)
        {
            var sb = new StringBuilder();
            var indent = 0;
            var inString = false;
            var escaped = false;

            foreach (var ch in json)
            {
                if (escaped)
                {
                    sb.Append(ch);
                    escaped = false;
                    continue;
                }

                if (ch == '\\' && inString)
                {
                    sb.Append(ch);
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    sb.Append(ch);
                    continue;
                }

                if (inString)
                {
                    sb.Append(ch);
                    continue;
                }

                switch (ch)
                {
                    case '{':
                    case '[':
                        sb.Append(ch);
                        sb.AppendLine();
                        indent++;
                        sb.Append(' ', indent * 2);
                        break;
                    case '}':
                    case ']':
                        sb.AppendLine();
                        indent = Math.Max(0, indent - 1);
                        sb.Append(' ', indent * 2);
                        sb.Append(ch);
                        break;
                    case ',':
                        sb.Append(ch);
                        sb.AppendLine();
                        sb.Append(' ', indent * 2);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(ch))
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
