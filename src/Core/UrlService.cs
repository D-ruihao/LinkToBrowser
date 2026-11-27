using System;
using System.Collections.Generic;

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

        public static string StripTracking(string url)
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

            var kept = new List<string>();
            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                var eqIndex = pair.IndexOf('=');
                var key = eqIndex >= 0 ? pair.Substring(0, eqIndex) : pair;
                if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || ExactTrackingKeys.Contains(key))
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
            while (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }

            return s;
        }
    }
}
