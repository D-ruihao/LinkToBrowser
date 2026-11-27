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
        public List<string> HiddenBrowserIds { get; set; } = new List<string>();
        public List<string> BrowserOrder { get; set; } = new List<string>();
        public List<BrowserEntry> CustomBrowsers { get; set; } = new List<BrowserEntry>();

        public AppConfig Normalize()
        {
            if (ConfigVersion <= 0) ConfigVersion = 1;
            if (ClipboardAutoCloseSeconds < 0) ClipboardAutoCloseSeconds = 0;
            OriginalDefaultProgId ??= DefaultOriginalProgId;
            HiddenBrowserIds ??= new List<string>();
            BrowserOrder ??= new List<string>();
            CustomBrowsers ??= new List<BrowserEntry>();
            return this;
        }
    }
}
