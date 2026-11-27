using System;
using System.Collections.Generic;
using System.Linq;

namespace LinkToBrowser.Core
{
    public static class UrlService
    {
        private static readonly string[] ProtocolPrefixes =
        {
            "linktobrowser.url:",
            "linkclip.url:"
        };

        private static readonly HashSet<string> ExactTrackingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fbclid",
            "gclid",
            "msclkid",
            "spm"
        };

        private static readonly HashSet<string> KnownRedirectHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "link.zhihu.com",
            "link.juejin.cn",
            "link.csdn.net",
            "link.jianshu.com",
            "link.ld246.com",
            "c.pc.qq.com",
            "weixin110.qq.com",
            "l.facebook.com",
            "lm.facebook.com",
            "out.reddit.com",
            "safelinks.protection.outlook.com",
            "statics.teams.cdn.office.net"
        };

        private static readonly string[] RedirectPathMarkers =
        {
            "/link",
            "/jump",
            "/redirect",
            "/goto",
            "/out",
            "/away",
            "/exit",
            "/external",
            "/middle",
            "/url",
            "/l.php"
        };

        private static readonly HashSet<string> RedirectQueryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "target",
            "url",
            "u",
            "pfurl",
            "redirect",
            "redirect_url",
            "dest",
            "destination",
            "to",
            "link",
            "q"
        };

        private static readonly string[] AuthBypassMarkers =
        {
            "oauth",
            "authorize",
            "signin",
            "sign-in",
            "login",
            "sso",
            "saml",
            "oidc"
        };

        private static readonly HashSet<string> AuthBypassQueryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "client_id",
            "response_type",
            "code_challenge",
            "samlrequest"
        };

        public static bool TryNormalize(string raw, out string url)
        {
            url = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var candidate = UnwrapOuterQuotes(raw);
            foreach (var prefix in ProtocolPrefixes)
            {
                if (candidate.Length > prefix.Length &&
                    candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    candidate = UnwrapOuterQuotes(candidate.Substring(prefix.Length));
                    break;
                }
            }

            if (candidate.Length == 0)
            {
                return false;
            }

            foreach (var ch in candidate)
            {
                if (ch < 0x20)
                {
                    return false;
                }
            }

            var uriCandidate = candidate.Replace("\"", "%22");
            if (!Uri.TryCreate(uriCandidate, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            if (!isHttp || string.IsNullOrEmpty(uri.Host))
            {
                return false;
            }

            url = candidate;
            return true;
        }

        public static string Prepare(string normalizedUrl, Models.AppConfig cfg)
        {
            if (string.IsNullOrEmpty(normalizedUrl) || cfg == null)
            {
                return normalizedUrl;
            }

            var effective = normalizedUrl;
            if (cfg.UnwrapRedirectUrls)
            {
                effective = UnwrapRedirectUrl(effective);
            }

            if (cfg.StripTrackingParams)
            {
                effective = StripTracking(effective, cfg.CustomTrackingParams);
            }

            return effective;
        }

        public static string UnwrapRedirectUrl(string url, int maxDepth = 3)
        {
            if (string.IsNullOrWhiteSpace(url) || maxDepth <= 0)
            {
                return url;
            }

            var current = url;
            for (var depth = 0; depth < maxDepth; depth++)
            {
                if (!TryUnwrapSingleRedirect(current, out var next) ||
                    string.Equals(next, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = next;
            }

            return current;
        }

        private static bool TryUnwrapSingleRedirect(string url, out string unwrapped)
        {
            unwrapped = string.Empty;
            if (!Uri.TryCreate(url.Replace("\"", "%22"), UriKind.Absolute, out var uri))
            {
                return false;
            }

            var hashIndex = url.IndexOf('#');
            var beforeHash = hashIndex >= 0 ? url.Substring(0, hashIndex) : url;
            var queryIndex = beforeHash.IndexOf('?');
            if (queryIndex < 0)
            {
                return false;
            }

            var query = beforeHash.Substring(queryIndex + 1);
            if (query.Length == 0)
            {
                return false;
            }

            var path = uri.AbsolutePath ?? string.Empty;
            var isKnownHost = KnownRedirectHosts.Contains(uri.Host) ||
                              uri.Host.EndsWith(".safelinks.protection.outlook.com", StringComparison.OrdinalIgnoreCase);
            var isRedirectPath = RedirectPathMarkers.Any(m =>
                string.Equals(path, m, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(m + "/", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(m, StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(m + ".html", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(m + ".php", StringComparison.OrdinalIgnoreCase));

            if (!isKnownHost && !isRedirectPath)
            {
                return false;
            }

            if (!isKnownHost && AuthBypassMarkers.Any(m => path.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return false;
            }

            string candidateRaw = null;
            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;

                var key = pair.Substring(0, eq);
                if (!isKnownHost && AuthBypassQueryKeys.Contains(key))
                {
                    return false;
                }

                if (candidateRaw == null && RedirectQueryKeys.Contains(key))
                {
                    candidateRaw = pair.Substring(eq + 1);
                }
            }

            if (string.IsNullOrWhiteSpace(candidateRaw))
            {
                return false;
            }

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(candidateRaw.Replace("+", "%20"));
            }
            catch
            {
                return false;
            }

            if (!TryNormalize(decoded, out var normalizedTarget))
            {
                return false;
            }

            unwrapped = normalizedTarget;
            return true;
        }

        public static string StripTracking(string url, IEnumerable<string> customParams = null)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            var hashIndex = url.IndexOf('#');
            var beforeHash = hashIndex >= 0 ? url.Substring(0, hashIndex) : url;
            var fragment = hashIndex >= 0 ? url.Substring(hashIndex) : string.Empty;

            var queryIndex = beforeHash.IndexOf('?');
            if (queryIndex < 0)
            {
                return url;
            }

            var basePart = beforeHash.Substring(0, queryIndex);
            var query = beforeHash.Substring(queryIndex + 1);
            if (query.Length == 0)
            {
                return url;
            }

            HashSet<string> extraExact = null;
            List<string> extraPrefixes = null;
            if (customParams != null)
            {
                foreach (var raw in customParams)
                {
                    var token = (raw ?? string.Empty).Trim();
                    if (token.Length == 0) continue;
                    if (token.EndsWith("*", StringComparison.Ordinal))
                    {
                        var prefix = token.TrimEnd('*');
                        if (prefix.Length > 0)
                        {
                            extraPrefixes ??= new List<string>();
                            extraPrefixes.Add(prefix);
                        }
                    }
                    else
                    {
                        extraExact ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        extraExact.Add(token);
                    }
                }
            }

            var kept = new List<string>();
            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                var eqIndex = pair.IndexOf('=');
                var key = eqIndex >= 0 ? pair.Substring(0, eqIndex) : pair;
                if (IsTrackingKey(key, extraExact, extraPrefixes))
                {
                    continue;
                }

                kept.Add(pair);
            }

            if (kept.Count == 0)
            {
                return basePart + fragment;
            }

            return basePart + "?" + string.Join("&", kept) + fragment;
        }

        private static bool IsTrackingKey(string key, HashSet<string> extraExact, List<string> extraPrefixes)
        {
            if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || ExactTrackingKeys.Contains(key))
            {
                return true;
            }

            if (extraExact != null && extraExact.Contains(key))
            {
                return true;
            }

            if (extraPrefixes != null)
            {
                foreach (var prefix in extraPrefixes)
                {
                    if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static string Host(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(url.Replace("\"", "%22"), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            {
                return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
            }

            return url;
        }

        private static string UnwrapOuterQuotes(string value)
        {
            var s = value.Trim();
            while (s.Length >= 2 &&
                   ((s[0] == '"' && s[s.Length - 1] == '"') ||
                    (s[0] == '\'' && s[s.Length - 1] == '\'')))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }

            return s;
        }
    }
}
