using System;
using System.IO;
using System.Text.RegularExpressions;

namespace LinkToBrowser.Core
{
    public static class CommandLineHelper
    {
        private static readonly Regex PlaceholderRegex = new Regex(@"%(1|L)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool TrySplit(string command, Func<string, bool> fileExists, out string exe, out string args)
        {
            exe = string.Empty;
            args = string.Empty;
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            var probe = fileExists ?? File.Exists;
            var trimmed = command.Trim();

            if (trimmed[0] == '"')
            {
                var closeQuote = trimmed.IndexOf('"', 1);
                if (closeQuote <= 1)
                {
                    return false;
                }

                var candidate = trimmed.Substring(1, closeQuote - 1).Trim();
                if (!probe(candidate))
                {
                    return false;
                }

                exe = candidate;
                args = NormalizePlaceholders(trimmed.Substring(closeQuote + 1).Trim());
                return true;
            }

            for (var i = trimmed.Length; i > 0; i--)
            {
                if (i < trimmed.Length && !char.IsWhiteSpace(trimmed[i]))
                {
                    continue;
                }

                var candidate = trimmed.Substring(0, i).TrimEnd();
                if (candidate.Length == 0 || !probe(candidate))
                {
                    continue;
                }

                exe = candidate;
                args = NormalizePlaceholders(trimmed.Substring(i).Trim());
                return true;
            }

            return false;
        }

        private static string NormalizePlaceholders(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                return string.Empty;
            }

            return PlaceholderRegex.Replace(args, "{url}");
        }
    }
}
