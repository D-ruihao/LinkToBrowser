using System;
using System.Collections.Generic;

namespace LinkToBrowser.Models
{
    public enum GlobalMode
    {
        NativePassthrough,
        DefaultBrowser,
        PickerPopup,
        ClipboardOnly,
        RecentBrowser
    }

    public enum RecentFallbackMode
    {
        PickerPopup,
        ClipboardOnly,
        DefaultBrowser,
        NativePassthrough
    }

    public enum RuleMatchKind
    {
        DomainSuffix,
        UrlPrefix,
        Wildcard
    }

    public enum RuleActionKind
    {
        OpenTarget,
        ClipboardOnly,
        PickerPopup,
        RecentBrowser,
        NativePassthrough
    }

    public sealed class RoutingRule
    {
        public string Id { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = string.Empty;
        public RuleMatchKind MatchKind { get; set; } = RuleMatchKind.DomainSuffix;
        public string Pattern { get; set; } = string.Empty;
        public RuleActionKind Action { get; set; } = RuleActionKind.OpenTarget;
        public BrowserEntry TargetBrowser { get; set; }
    }

    public enum ModifierKey
    {
        None,
        Alt,
        Shift
    }

    public enum ThemeMode
    {
        System,
        Dark,
        Light
    }

    public enum LanguageMode
    {
        ZhCn,
        EnUs,
        System
    }

    public sealed class AppConfig
    {
        public const string DefaultOriginalProgId = "MSEdgeHTM";

        public int ConfigVersion { get; set; } = 1;
        public GlobalMode GlobalMode { get; set; } = GlobalMode.ClipboardOnly;
        public BrowserEntry DefaultBrowser { get; set; }
        public string OriginalDefaultProgId { get; set; } = DefaultOriginalProgId;
        public ModifierKey Modifier { get; set; } = ModifierKey.None;
        public bool ShowClipboardPopup { get; set; } = true;
        public int ClipboardAutoCloseSeconds { get; set; }
        public bool StripTrackingParams { get; set; }
        public bool ShowChromiumProfiles { get; set; } = true;
        public ThemeMode Theme { get; set; } = ThemeMode.System;
        public LanguageMode Language { get; set; } = LanguageMode.ZhCn;
        public RecentFallbackMode RecentBrowserFallback { get; set; } = RecentFallbackMode.PickerPopup;
        public bool EnableCdpForIsolatedBrowsers { get; set; } = true;
        public bool EnableRoutingRules { get; set; } = true;
        public bool SampleRulesSeeded { get; set; }
        public List<RoutingRule> Rules { get; set; } = new List<RoutingRule>();
        public List<string> HiddenBrowserIds { get; set; } = new List<string>();
        public List<string> BrowserOrder { get; set; } = new List<string>();
        public List<BrowserEntry> CustomBrowsers { get; set; } = new List<BrowserEntry>();

        public AppConfig Normalize()
        {
            if (ConfigVersion <= 0) ConfigVersion = 1;
            if (ClipboardAutoCloseSeconds < 0) ClipboardAutoCloseSeconds = 0;
            OriginalDefaultProgId ??= DefaultOriginalProgId;
            Rules ??= new List<RoutingRule>();
            Rules.RemoveAll(r => r == null || !Core.RuleMatcher.HasValidTokens(r.Pattern));
            var seenRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Rules)
            {
                if (string.IsNullOrWhiteSpace(r.Id) || !seenRuleIds.Add(r.Id))
                {
                    r.Id = $"rule:{Guid.NewGuid():N}";
                    seenRuleIds.Add(r.Id);
                }

                r.Name = (r.Name ?? string.Empty).Trim();
                r.Pattern = (r.Pattern ?? string.Empty).Trim();
            }

            HiddenBrowserIds ??= new List<string>();
            BrowserOrder ??= new List<string>();
            CustomBrowsers ??= new List<BrowserEntry>();
            CustomBrowsers.RemoveAll(c => c == null || string.IsNullOrWhiteSpace(c.ExecutablePath));
            var seenCustomIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in CustomBrowsers)
            {
                c.Kind = BrowserKind.Custom;
                if (string.IsNullOrWhiteSpace(c.Id) || !seenCustomIds.Add(c.Id))
                {
                    c.Id = BrowserEntry.MakeId(BrowserKind.Custom, c.ExecutablePath, c.ArgumentsTemplate);
                    if (!seenCustomIds.Add(c.Id))
                    {
                        c.Id = $"custom:{Guid.NewGuid():N}";
                        seenCustomIds.Add(c.Id);
                    }
                }
            }
            return this;
        }
    }
}
