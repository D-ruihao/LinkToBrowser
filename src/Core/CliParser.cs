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
        HandleUrl
    }

    public sealed class CliInvocation
    {
        public CliKind Kind { get; set; }
        public string RawUrl { get; set; } = string.Empty;
        public bool NoUi { get; set; }
    }

    public static class CliParser
    {
        public static CliInvocation Parse(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return new CliInvocation { Kind = CliKind.OpenSettings };
            }

            var noUi = false;
            var openSettings = false;
            var register = false;
            var unregister = false;
            var parts = new List<string>();

            foreach (var raw in args)
            {
                if (raw == null)
                {
                    continue;
                }

                var trimmed = raw.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (string.Equals(trimmed, "--no-ui", StringComparison.OrdinalIgnoreCase))
                {
                    noUi = true;
                }
                else if (string.Equals(trimmed, "--settings", StringComparison.OrdinalIgnoreCase))
                {
                    openSettings = true;
                }
                else if (string.Equals(trimmed, "--register", StringComparison.OrdinalIgnoreCase))
                {
                    register = true;
                }
                else if (string.Equals(trimmed, "--unregister", StringComparison.OrdinalIgnoreCase))
                {
                    unregister = true;
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
                return new CliInvocation { Kind = CliKind.Register, NoUi = noUi };
            }

            if (unregister)
            {
                return new CliInvocation { Kind = CliKind.Unregister, NoUi = noUi };
            }

            if (parts.Count > 0)
            {
                var joined = string.Join(" ", parts);
                return new CliInvocation
                {
                    Kind = CliKind.HandleUrl,
                    RawUrl = joined,
                    NoUi = noUi
                };
            }

            if (openSettings)
            {
                return new CliInvocation { Kind = CliKind.OpenSettings, NoUi = noUi };
            }

            return new CliInvocation { Kind = CliKind.Ignore, NoUi = noUi };
        }
    }
}
