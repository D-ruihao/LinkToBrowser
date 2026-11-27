using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LinkToBrowser.Models;

namespace LinkToBrowser.Core
{
    public static class RuleMatcher
    {
        private static readonly char[] PatternSeparators = { ',', ';', '\r', '\n' };
        private static readonly TimeSpan WildcardRegexTimeout = TimeSpan.FromMilliseconds(150);

        public static bool HasValidTokens(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            foreach (var raw in pattern.Split(PatternSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return true;
                }
            }

            return false;
        }

        public static RoutingRule Match(string url, IReadOnlyList<RoutingRule> rules, string sourceApp = null)
        {
            if (string.IsNullOrWhiteSpace(url) || rules == null || rules.Count == 0)
            {
                return null;
            }

            Uri.TryCreate(url.Replace("\"", "%22"), UriKind.Absolute, out var uri);
            var host = uri?.Host ?? string.Empty;
            var port = uri?.Port ?? -1;
            var authority = uri == null
                ? string.Empty
                : (uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}");

            foreach (var rule in rules)
            {
                if (rule == null || !rule.Enabled)
                {
                    continue;
                }

                if (!HasValidTokens(rule.Pattern) && !HasValidTokens(rule.SourceApps))
                {
                    continue;
                }

                if (IsRuleMatch(url, host, authority, rule, port, sourceApp))
                {
                    return rule;
                }
            }

            return null;
        }

        public static bool IsRuleMatch(string url, string host, string authority, RoutingRule rule, int port = -1, string sourceApp = null)
        {
            var hasSourceFilter = HasValidTokens(rule.SourceApps);
            if (hasSourceFilter && !MatchSourceApp(sourceApp, rule.SourceApps))
            {
                return false;
            }

            var hasUrlPattern = HasValidTokens(rule.Pattern);
            if (!hasUrlPattern)
            {
                return hasSourceFilter;
            }

            if (port < 0 && !string.IsNullOrEmpty(authority))
            {
                TrySplitHostAndPort(authority, out _, out port);
            }

            var tokens = rule.Pattern.Split(PatternSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawToken in tokens)
            {
                var token = rawToken.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                switch (rule.MatchKind)
                {
                    case RuleMatchKind.DomainSuffix:
                        if (MatchDomain(host, port, token))
                        {
                            return true;
                        }
                        break;

                    case RuleMatchKind.UrlPrefix:
                        if (url.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                        break;

                    case RuleMatchKind.Wildcard:
                        if (MatchWildcard(url, token))
                        {
                            return true;
                        }
                        break;
                }
            }

            return false;
        }

        public static bool MatchSourceApp(string actualSourceApp, string configuredSourceApps)
        {
            var actualFile = ProcessInfoHelper.NormalizeExeFileName(actualSourceApp);
            if (string.IsNullOrWhiteSpace(actualFile) || string.IsNullOrWhiteSpace(configuredSourceApps))
            {
                return false;
            }

            var actualBase = actualFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? actualFile.Substring(0, actualFile.Length - 4)
                : actualFile;

            foreach (var raw in configuredSourceApps.Split(PatternSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = ProcessInfoHelper.NormalizeExeFileName(raw);
                if (token.Length == 0) continue;

                var tokenBase = token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? token.Substring(0, token.Length - 4)
                    : token;

                if (string.Equals(actualFile, token, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(actualBase, tokenBase, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchDomain(string host, int urlPort, string token)
        {
            if (string.IsNullOrEmpty(host))
            {
                return false;
            }

            var clean = token.Trim();
            if (clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(7);
            }
            else if (clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(8);
            }

            var slashIdx = clean.IndexOf('/');
            if (slashIdx >= 0)
            {
                clean = clean.Substring(0, slashIdx);
            }

            if (clean.StartsWith("*.", StringComparison.Ordinal))
            {
                clean = clean.Substring(2);
            }

            if (!TrySplitHostAndPort(clean, out var cleanHost, out var expectedPort))
            {
                return false;
            }

            cleanHost = cleanHost.Trim('.');
            if (cleanHost.Length == 0)
            {
                return false;
            }

            if (expectedPort >= 0 && urlPort != expectedPort)
            {
                return false;
            }

            return string.Equals(host, cleanHost, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("." + cleanHost, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TrySplitHostAndPort(string value, out string host, out int port)
        {
            host = string.Empty;
            port = -1;
            var s = (value ?? string.Empty).Trim();
            if (s.Length == 0)
            {
                return false;
            }

            if (s[0] == '[')
            {
                var closeIdx = s.IndexOf(']');
                if (closeIdx < 0)
                {
                    return false;
                }

                host = s.Substring(0, closeIdx + 1);
                var rest = s.Substring(closeIdx + 1);
                if (rest.Length == 0)
                {
                    return true;
                }

                if (rest[0] == ':' && int.TryParse(rest.Substring(1), out var p) && p > 0 && p <= 65535)
                {
                    port = p;
                    return true;
                }

                return false;
            }

            var firstColon = s.IndexOf(':');
            if (firstColon < 0)
            {
                host = s;
                return true;
            }

            var lastColon = s.LastIndexOf(':');
            if (firstColon != lastColon)
            {
                // Unbracketed IPv6 literal (e.g. ::1)
                host = "[" + s + "]";
                return true;
            }

            host = s.Substring(0, lastColon);
            var portStr = s.Substring(lastColon + 1);
            if (!int.TryParse(portStr, out var parsedPort) || parsedPort <= 0 || parsedPort > 65535)
            {
                return false;
            }

            port = parsedPort;
            return host.Length > 0;
        }

        private static bool MatchWildcard(string url, string pattern)
        {
            try
            {
                var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                return Regex.IsMatch(url, regexPattern, RegexOptions.IgnoreCase, WildcardRegexTimeout);
            }
            catch
            {
                return false;
            }
        }

        public static List<RoutingRule> CreateDefaultSamples(bool isEnglish)
        {
            return new List<RoutingRule>
            {
                new RoutingRule
                {
                    Id = "sample:local-dev",
                    Enabled = false,
                    Name = isEnglish ? "Example: Local Dev Server" : "示例：本地开发调试",
                    MatchKind = RuleMatchKind.DomainSuffix,
                    Pattern = "localhost, 127.0.0.1",
                    Action = RuleActionKind.PickerPopup
                },
                new RoutingRule
                {
                    Id = "sample:github",
                    Enabled = false,
                    Name = isEnglish ? "Example: GitHub" : "示例：GitHub 仓库",
                    MatchKind = RuleMatchKind.DomainSuffix,
                    Pattern = "github.com, gist.github.com",
                    Action = RuleActionKind.PickerPopup
                },
                new RoutingRule
                {
                    Id = "sample:oauth",
                    Enabled = false,
                    Name = isEnglish ? "Example: OAuth & Callback Links" : "示例：OAuth 登录回调链接",
                    MatchKind = RuleMatchKind.Wildcard,
                    Pattern = "*oauth*, */login/callback*",
                    Action = RuleActionKind.ClipboardOnly
                }
            };
        }
    }
}
