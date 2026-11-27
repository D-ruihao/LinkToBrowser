using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using LinkToBrowser.Models;

namespace LinkToBrowser.Core
{
    /// <summary>
    /// Provides runtime localization (Simplified Chinese and English) with WPF ResourceDictionary synchronization.
    /// </summary>
    public static class L
    {
        private static readonly Dictionary<string, string> ZhCn = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Navigation & Common
            ["S_SettingsWindowTitle"] = "LinkToBrowser 设置",
            ["S_NavGeneral"] = "常规",
            ["S_NavBrowsers"] = "浏览器",
            ["S_NavAbout"] = "关于",
            ["S_OpenConfig"] = "打开配置文件",
            ["S_OpenLogs"] = "打开日志目录",
            ["S_SavedBadge"] = "已自动保存",

            // Registration card & banners
            ["S_RegCardTitle"] = "系统默认浏览器接管",
            ["S_RegAppDescription"] = "链接路由、浏览器选择器与剪贴板处理工具",
            ["S_RegBtnRegister"] = "立即注册",
            ["S_RegBtnUpdatePath"] = "更新注册路径",
            ["S_RegBtnOpenDefaultApps"] = "打开默认应用设置",
            ["S_RegSubCurrent"] = "当前注册命令：{0}",
            ["S_RegSubNotReg"] = "尚未在当前用户 (HKCU) 注册：{0}",
            ["S_BannerOkTitle"] = "已接管系统 http / https 默认链接",
            ["S_BannerOkMsg"] = "LinkToBrowser 正在正常运行，下方选择的模式将立即生效。",
            ["S_BannerMovedTitle"] = "已设为默认，但注册路径指向了其它位置",
            ["S_BannerMovedMsg"] = "你移动或新部署了 LinkToBrowser.exe，建议点击右侧「更新注册路径」。",
            ["S_BannerPendingTitle"] = "已清理 LinkToBrowser 注册项，但系统默认尚未改回",
            ["S_BannerPendingMsg"] = "请在打开的「Windows 默认应用」里把常用浏览器（如 Chrome / Edge）设为 http 和 https 默认，才算完成交还。",
            ["S_BannerRegOnlyTitle"] = "已完成注册，尚未设为系统默认应用",
            ["S_BannerRegOnlyMsg"] = "当前默认：http={0}，https={1}。点击右侧「打开默认应用设置」搜索 LinkToBrowser 设为默认。",
            ["S_BannerUnregTitle"] = "尚未注册到 Windows 默认应用列表",
            ["S_BannerUnregMsg"] = "请先点击「立即注册」（无需管理员权限），再到系统设置里设为默认。",

            // Global Mode
            ["S_GlobalModeHeader"] = "全局链接处理模式",
            ["S_ModeNativeTitle"] = "Windows 原生",
            ["S_ModeNativeDesc"] = "静默转发给系统原默认浏览器，可随时切换回系统设置",
            ["S_BtnRestoreDefault"] = "还原默认…",
            ["S_ModeDefaultTitle"] = "默认指定浏览器",
            ["S_ModeDefaultDesc"] = "静默使用下方选定的浏览器或自定义可执行文件打开链接",
            ["S_ModePickerTitle"] = "弹出浏览器选择界面",
            ["S_ModePickerDesc"] = "点击链接时在光标附近弹出选择窗口，支持 1–9 快选与筛选",
            ["S_ModeClipboardTitle"] = "默认复制到剪贴板",
            ["S_ModeClipboardDesc"] = "拦截链接并复制到剪贴板，按需弹出快捷确认窗口",
            ["S_ModeRecentTitle"] = "最近聚焦的浏览器",
            ["S_ModeRecentDesc"] = "自动跳转到最近一次使用或点击聚焦的浏览器窗口（含已最小化窗口）",
            ["S_FallbackPicker"] = "无窗口时：弹出选择",
            ["S_FallbackClipboard"] = "无窗口时：复制剪贴板",
            ["S_FallbackDefault"] = "无窗口时：指定浏览器",
            ["S_FallbackNative"] = "无窗口时：Windows 原生",
            ["S_EnableCdpTitle"] = "兼容调试端口直跳独立浏览器 (CDP)",
            ["S_EnableCdpDesc"] = "检测到浏览器窗口开启 DevToolsActivePort 时，直接向该窗口新建标签页",
            ["S_SelectOriginalBrowser"] = "(请选择原系统默认浏览器...)",

            // Behavior & Appearance
            ["S_BehaviorHeader"] = "行为与外观",
            ["S_ShowClipPopupTitle"] = "剪贴板模式显示确认弹窗",
            ["S_ShowClipPopupDesc"] = "关闭后将完全静默写入剪贴板，仅在复制失败时弹窗提示",
            ["S_AutoCloseTitle"] = "剪贴板确认窗自动关闭时间",
            ["S_AutoCloseDesc"] = "鼠标悬停或焦点在链接框时自动暂停倒计时（0 表示不自动关闭）",
            ["S_AutoClose0"] = "0 秒（不自动关闭）",
            ["S_AutoCloseN"] = "{0} 秒",
            ["S_ModifierTitle"] = "修饰键临时弹出选择窗",
            ["S_ModifierDesc"] = "在非弹窗模式下，按住该键点击链接将临时改弹浏览器选择窗",
            ["S_ModNone"] = "关闭",
            ["S_ModAlt"] = "按住 Alt 键",
            ["S_ModShift"] = "按住 Shift 键",
            ["S_ChromiumTitle"] = "发现 Chromium 多用户配置",
            ["S_ChromiumDesc"] = "自动列出 Chrome / Edge 等多账号 Profile 入口",
            ["S_StripTrackingTitle"] = "清理常见追踪参数",
            ["S_StripTrackingDesc"] = "移除查询串中的 utm_*、fbclid、gclid、msclkid、spm（保留 # 锚点与原编码）",
            ["S_ThemeTitle"] = "界面主题",
            ["S_ThemeDesc"] = "默认跟随 Windows 11 系统深浅色设置",
            ["S_ThemeSystem"] = "跟随系统",
            ["S_ThemeDark"] = "深色",
            ["S_ThemeLight"] = "浅色",
            ["S_LanguageTitle"] = "界面语言",
            ["S_LanguageDesc"] = "切换设置页、选择窗与剪贴板浮窗的显示语言",
            ["S_LangZhCn"] = "简体中文",
            ["S_LangEnUs"] = "English",
            ["S_LangSystem"] = "跟随系统",

            // Browsers Page
            ["S_BrowsersDesc"] = "调整浏览器显示顺序、隐藏不需要的条目，或添加自定义可执行文件与启动参数。",
            ["S_Rescan"] = "重新扫描",
            ["S_AddCustom"] = "添加自定义浏览器",
            ["S_KindCustom"] = "自定义",
            ["S_KindProfile"] = "多配置",
            ["S_KindSystem"] = "系统识别",
            ["S_KindRunning"] = "运行窗口",
            ["S_StatusHidden"] = "已隐藏",
            ["S_StatusVisible"] = "显示中",
            ["S_BtnTest"] = "测试",
            ["S_TipMoveUp"] = "上移",
            ["S_TipMoveDown"] = "下移",
            ["S_TipEdit"] = "编辑",
            ["S_TipDelete"] = "删除",

            // About & Diagnostics
            ["S_DiagAssocTitle"] = "系统当前关联诊断 (UserChoice)",
            ["S_BtnRefresh"] = "刷新",
            ["S_DiagConfigTitle"] = "配置文件位置",
            ["S_BtnOpen"] = "打开",
            ["S_DiagLogTitle"] = "运行日志目录",
            ["S_BtnOpenDir"] = "打开目录",
            ["S_BtnClearLogs"] = "清空日志",
            ["S_RestoreCardTitle"] = "还原系统默认浏览器",
            ["S_RestoreCardDesc"] = "打开 Windows 默认应用设置页选回原浏览器，并可清理当前用户的注册表关联",
            ["S_BtnRestoreClean"] = "还原并清理注册...",
            ["S_BtnGitHubRepo"] = "GitHub 主页",
            ["S_BtnCheckUpdates"] = "检查更新 (Releases)",
            ["S_AboutDesc"] = "轻量级 Windows 11 默认浏览器路由、选择器与剪贴板链接处理工具（MIT License）。",
            ["S_DiagText"] = "http UserChoice = {0}\nhttps UserChoice = {1}\nLinkToBrowser.URL 命令 -> {2}",
            ["S_NotRegistered"] = "(未注册)",
            ["S_Unset"] = "(未设置)",

            // MessageBoxes & Actions
            ["S_MsgRegFailed"] = "注册失败：{0}",
            ["S_MsgOpenDefaultFailed"] = "无法打开默认应用设置：{0}",
            ["S_RestoreHintNoTarget"] = "请在打开的「Windows 默认应用」设置中选回你常用的浏览器。",
            ["S_RestoreHintTarget"] = "建议在打开的「Windows 默认应用」设置中选回：{0}。",
            ["S_RestoreConfirmBody"] = "{0}\n\n是否同时清理 LinkToBrowser 在当前用户注册表 (HKCU) 下的注册项？\n\n选择「是」= 打开系统设置并清理注册项\n选择「否」= 仅打开系统设置，保留注册项\n选择「取消」= 暂不操作",
            ["S_RestoreConfirmTitle"] = "还原系统默认设置",
            ["S_MsgTestFailed"] = "测试启动失败：{0}",
            ["S_MsgOpenConfigFailed"] = "打开配置文件失败：{0}",
            ["S_MsgLogsCleared"] = "已清空运行日志。",

            // Dispatcher warnings
            ["S_WarnNoOriginal"] = "未配置原系统默认浏览器，请从下方选择或前往设置指定。",
            ["S_WarnNoCommand"] = "原默认浏览器 ({0}) 无命令行入口，请从下方选择。",
            ["S_WarnInvalidCommand"] = "原默认浏览器 ({0}) 启动程序不可用，请从下方选择。",
            ["S_WarnNoDefaultTarget"] = "尚未指定默认浏览器，请从下方选择。",
            ["S_WarnLaunchFallback"] = "启动浏览器失败，已复制到剪贴板：{0}",
            ["S_WarnCdpFallback"] = "向目标浏览器窗口发送新标签页失败，已复制到剪贴板：{0}",
            ["S_WarnIsolatedNotRunning"] = "目标隔离浏览器窗口（{0}）当前未运行或无法接入，已复制到剪贴板：{1}",
            ["S_WarnClipBusy"] = "剪贴板被占用，复制失败：URL 已写入 links.log，请手动从下方复制。",

            // Picker Window
            ["S_PickerWindowTitle"] = "选择浏览器",
            ["S_PickerTitleBar"] = "LinkToBrowser · 选择打开方式",
            ["S_PickerCopy"] = "复制",
            ["S_PickerCopied"] = "已复制",
            ["S_PickerFailed"] = "失败",
            ["S_PickerCopyTip"] = "复制链接到剪贴板 (Ctrl+C)",
            ["S_PickerSettingsTip"] = "打开 LinkToBrowser 设置",
            ["S_PickerSearchHint"] = "输入筛选浏览器，或直接按 1–9 快选...",
            ["S_PickerEmpty"] = "没有找到可用的浏览器条目",
            ["S_PickerCopyLink"] = "复制链接",
            ["S_PickerOpenSettings"] = "打开设置",

            // Clipboard Window
            ["S_ClipTitleBar"] = "LinkToBrowser · 链接已捕获",
            ["S_ClipCopyAgain"] = "再复制一次",
            ["S_ClipOpenBrowser"] = "用浏览器打开",
            ["S_ClipLogs"] = "日志目录",
            ["S_ClipSettingsTip"] = "打开设置",
            ["S_ClipClose"] = "关闭 (Esc)",
            ["S_ClipClosePaused"] = "关闭 (Esc) · 已暂停",
            ["S_ClipCloseCountdown"] = "关闭 (Esc) · {0}s",
            ["S_ClipWarnTitle"] = "已复制到剪贴板",
            ["S_ClipErrTitle"] = "操作失败",
            ["S_ClipOkTitle"] = "已复制到剪贴板",
            ["S_ClipOkMsg"] = "切换到目标浏览器按 Ctrl+V 即可粘贴打开。",
            ["S_ClipFailTitle"] = "剪贴板被占用，复制失败",
            ["S_ClipFailMsg"] = "URL 已写入 links.log，请从下方文本框手动复制。",
            ["S_ClipRetryFailMsg"] = "仍然失败，请手动选中文本框复制。",

            // Custom Browser Dialog
            ["S_CustomWinTitle"] = "添加 / 编辑自定义浏览器",
            ["S_CustomTitleBar"] = "自定义浏览器 / 启动参数",
            ["S_CustomNameLabel"] = "显示名称",
            ["S_CustomNameHint"] = "例如：Chrome 工作号 / Portable Browser",
            ["S_CustomExeLabel"] = "可执行文件路径 (.exe)",
            ["S_CustomExeHint"] = "选择或粘贴浏览器 .exe 路径",
            ["S_CustomBrowse"] = "浏览...",
            ["S_CustomArgsLabel"] = "启动参数模板（可选）",
            ["S_CustomArgsHint"] = "可用 {url} 占位符；留空或未写 {url} 时会在末尾自动追加引号包裹的链接。",
            ["S_CustomArgsPlaceholder"] = "例如：--profile-directory=\"Profile 1\" {url}",
            ["S_CustomPresetProfile"] = "预设: Chrome 多用户",
            ["S_CustomPresetIncognito"] = "预设: 隐身模式",
            ["S_CustomTestLaunch"] = "测试启动",
            ["S_CustomCancel"] = "取消",
            ["S_CustomSave"] = "保存",
            ["S_CustomFileFilter"] = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            ["S_CustomFileTitle"] = "选择浏览器可执行文件",
            ["S_CustomErrName"] = "请填写显示名称。",
            ["S_CustomErrExe"] = "可执行文件路径不存在。",
            ["S_CustomErrSelf"] = "不能将 LinkToBrowser 自身添加为目标浏览器。"
        };

        private static readonly Dictionary<string, string> EnUs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Navigation & Common
            ["S_SettingsWindowTitle"] = "LinkToBrowser Settings",
            ["S_NavGeneral"] = "General",
            ["S_NavBrowsers"] = "Browsers",
            ["S_NavAbout"] = "About",
            ["S_OpenConfig"] = "Open Config File",
            ["S_OpenLogs"] = "Open Logs Folder",
            ["S_SavedBadge"] = "Auto-saved",

            // Registration card & banners
            ["S_RegCardTitle"] = "System Default Browser Handler",
            ["S_RegAppDescription"] = "URL router, browser picker, and clipboard link handler",
            ["S_RegBtnRegister"] = "Register Now",
            ["S_RegBtnUpdatePath"] = "Update Path",
            ["S_RegBtnOpenDefaultApps"] = "Open Default Apps",
            ["S_RegSubCurrent"] = "Registered command: {0}",
            ["S_RegSubNotReg"] = "Not registered in HKCU yet: {0}",
            ["S_BannerOkTitle"] = "Handling http / https default links",
            ["S_BannerOkMsg"] = "LinkToBrowser is active. Mode changes below take effect immediately.",
            ["S_BannerMovedTitle"] = "Set as default, but registered path points elsewhere",
            ["S_BannerMovedMsg"] = "LinkToBrowser.exe was moved or redeployed. Click \"Update Path\" on the right.",
            ["S_BannerPendingTitle"] = "Registry cleaned, but system default not restored yet",
            ["S_BannerPendingMsg"] = "Please select your regular browser (e.g. Chrome / Edge) for http & https in Windows Default Apps to finish restoring.",
            ["S_BannerRegOnlyTitle"] = "Registered, not yet set as system default",
            ["S_BannerRegOnlyMsg"] = "Current default: http={0}, https={1}. Click \"Open Default Apps\" and set LinkToBrowser as default.",
            ["S_BannerUnregTitle"] = "Not registered in Windows Default Apps",
            ["S_BannerUnregMsg"] = "Click \"Register Now\" (no admin required), then set it as default in Windows Settings.",

            // Global Mode
            ["S_GlobalModeHeader"] = "Global Link Routing Mode",
            ["S_ModeNativeTitle"] = "Windows Native",
            ["S_ModeNativeDesc"] = "Silently forward links to your previous default browser",
            ["S_BtnRestoreDefault"] = "Restore Default…",
            ["S_ModeDefaultTitle"] = "Specific Browser",
            ["S_ModeDefaultDesc"] = "Silently open links in the selected browser or custom executable below",
            ["S_ModePickerTitle"] = "Browser Picker Popup",
            ["S_ModePickerDesc"] = "Show a quick picker near the cursor on link click, with 1–9 hotkeys and live filter",
            ["S_ModeClipboardTitle"] = "Copy to Clipboard",
            ["S_ModeClipboardDesc"] = "Intercept links and copy them to the clipboard with an optional popup",
            ["S_ModeRecentTitle"] = "Last Focused Browser",
            ["S_ModeRecentDesc"] = "Open links in the most recently focused browser window (including minimized windows)",
            ["S_FallbackPicker"] = "Fallback: Browser Picker",
            ["S_FallbackClipboard"] = "Fallback: Clipboard",
            ["S_FallbackDefault"] = "Fallback: Specific Browser",
            ["S_FallbackNative"] = "Fallback: Windows Native",
            ["S_EnableCdpTitle"] = "Use DevToolsActivePort (CDP) for isolated browsers",
            ["S_EnableCdpDesc"] = "Open new tabs directly via local DevToolsActivePort when present on the active window",
            ["S_SelectOriginalBrowser"] = "(Select original default browser...)",

            // Behavior & Appearance
            ["S_BehaviorHeader"] = "Behavior & Appearance",
            ["S_ShowClipPopupTitle"] = "Show confirmation popup in clipboard mode",
            ["S_ShowClipPopupDesc"] = "When off, copies silently and only pops up on failure",
            ["S_AutoCloseTitle"] = "Clipboard popup auto-close delay",
            ["S_AutoCloseDesc"] = "Pauses countdown on mouse hover or URL box focus (0 = stay open)",
            ["S_AutoClose0"] = "0s (Never)",
            ["S_AutoCloseN"] = "{0}s",
            ["S_ModifierTitle"] = "Modifier key for temporary picker",
            ["S_ModifierDesc"] = "Hold this key while clicking a link in non-picker modes to show the picker",
            ["S_ModNone"] = "Disabled",
            ["S_ModAlt"] = "Hold Alt",
            ["S_ModShift"] = "Hold Shift",
            ["S_ChromiumTitle"] = "Detect Chromium profiles",
            ["S_ChromiumDesc"] = "Automatically list Chrome / Edge user profiles as separate targets",
            ["S_StripTrackingTitle"] = "Strip common tracking parameters",
            ["S_StripTrackingDesc"] = "Remove utm_*, fbclid, gclid, msclkid, spm from query strings (keeps # fragments)",
            ["S_ThemeTitle"] = "Theme",
            ["S_ThemeDesc"] = "Follow Windows 11 light/dark mode by default",
            ["S_ThemeSystem"] = "Follow System",
            ["S_ThemeDark"] = "Dark",
            ["S_ThemeLight"] = "Light",
            ["S_LanguageTitle"] = "Language",
            ["S_LanguageDesc"] = "Switch display language for settings, picker, and clipboard popup",
            ["S_LangZhCn"] = "简体中文",
            ["S_LangEnUs"] = "English",
            ["S_LangSystem"] = "Follow System",

            // Browsers Page
            ["S_BrowsersDesc"] = "Reorder browsers, hide unwanted entries from the picker, or add custom executables and launch arguments.",
            ["S_Rescan"] = "Rescan",
            ["S_AddCustom"] = "Add Custom Browser",
            ["S_KindCustom"] = "Custom",
            ["S_KindProfile"] = "Profile",
            ["S_KindSystem"] = "System",
            ["S_KindRunning"] = "Active",
            ["S_StatusHidden"] = "Hidden",
            ["S_StatusVisible"] = "Visible",
            ["S_BtnTest"] = "Test",
            ["S_TipMoveUp"] = "Move up",
            ["S_TipMoveDown"] = "Move down",
            ["S_TipEdit"] = "Edit",
            ["S_TipDelete"] = "Delete",

            // About & Diagnostics
            ["S_DiagAssocTitle"] = "System Association Diagnostics (UserChoice)",
            ["S_BtnRefresh"] = "Refresh",
            ["S_DiagConfigTitle"] = "Config File Location",
            ["S_BtnOpen"] = "Open",
            ["S_DiagLogTitle"] = "Log Directory",
            ["S_BtnOpenDir"] = "Open Folder",
            ["S_BtnClearLogs"] = "Clear Logs",
            ["S_RestoreCardTitle"] = "Restore Windows Default State",
            ["S_RestoreCardDesc"] = "Open Windows Default Apps to switch back and optionally remove HKCU registry keys",
            ["S_BtnRestoreClean"] = "Restore & Clean...",
            ["S_BtnGitHubRepo"] = "GitHub Repo",
            ["S_BtnCheckUpdates"] = "Releases / Updates",
            ["S_AboutDesc"] = "A lightweight URL router, browser picker, and clipboard handler for Windows 11 (MIT License).",
            ["S_DiagText"] = "http UserChoice = {0}\nhttps UserChoice = {1}\nLinkToBrowser.URL command -> {2}",
            ["S_NotRegistered"] = "(Not registered)",
            ["S_Unset"] = "(Unset)",

            // MessageBoxes & Actions
            ["S_MsgRegFailed"] = "Registration failed: {0}",
            ["S_MsgOpenDefaultFailed"] = "Failed to open Default Apps settings: {0}",
            ["S_RestoreHintNoTarget"] = "Please select your preferred browser in Windows Default Apps.",
            ["S_RestoreHintTarget"] = "Recommended: select {0} in Windows Default Apps.",
            ["S_RestoreConfirmBody"] = "{0}\n\nAlso remove LinkToBrowser registry entries under HKCU?\n\nYes = Open Settings & clean registry\nNo = Open Settings only\nCancel = Do nothing",
            ["S_RestoreConfirmTitle"] = "Restore System Default",
            ["S_MsgTestFailed"] = "Test launch failed: {0}",
            ["S_MsgOpenConfigFailed"] = "Failed to open config file: {0}",
            ["S_MsgLogsCleared"] = "Logs cleared.",

            // Dispatcher warnings
            ["S_WarnNoOriginal"] = "Original default browser is not configured. Please choose one below or set it in Settings.",
            ["S_WarnNoCommand"] = "Original default browser ({0}) has no launch command. Please choose below.",
            ["S_WarnInvalidCommand"] = "Original default browser ({0}) executable is unavailable. Please choose below.",
            ["S_WarnNoDefaultTarget"] = "No default browser is selected. Please choose one below.",
            ["S_WarnLaunchFallback"] = "Failed to launch browser; copied URL to clipboard: {0}",
            ["S_WarnCdpFallback"] = "Failed to open tab in target browser window; copied URL to clipboard: {0}",
            ["S_WarnIsolatedNotRunning"] = "Target isolated browser ({0}) is not active; copied URL to clipboard: {1}",
            ["S_WarnClipBusy"] = "Clipboard is busy; copy failed. URL was saved to links.log; please copy manually below.",

            // Picker Window
            ["S_PickerWindowTitle"] = "Choose Browser",
            ["S_PickerTitleBar"] = "LinkToBrowser · Choose Browser",
            ["S_PickerCopy"] = "Copy",
            ["S_PickerCopied"] = "Copied",
            ["S_PickerFailed"] = "Failed",
            ["S_PickerCopyTip"] = "Copy URL to clipboard (Ctrl+C)",
            ["S_PickerSettingsTip"] = "Open LinkToBrowser Settings",
            ["S_PickerSearchHint"] = "Filter browsers, or press 1–9 to launch...",
            ["S_PickerEmpty"] = "No matching browsers found",
            ["S_PickerCopyLink"] = "Copy Link",
            ["S_PickerOpenSettings"] = "Open Settings",

            // Clipboard Window
            ["S_ClipTitleBar"] = "LinkToBrowser · Link Captured",
            ["S_ClipCopyAgain"] = "Copy Again",
            ["S_ClipOpenBrowser"] = "Open in Browser",
            ["S_ClipLogs"] = "Logs",
            ["S_ClipSettingsTip"] = "Open Settings",
            ["S_ClipClose"] = "Close (Esc)",
            ["S_ClipClosePaused"] = "Close (Esc) · Paused",
            ["S_ClipCloseCountdown"] = "Close (Esc) · {0}s",
            ["S_ClipWarnTitle"] = "Copied to clipboard",
            ["S_ClipErrTitle"] = "Action failed",
            ["S_ClipOkTitle"] = "Copied to clipboard",
            ["S_ClipOkMsg"] = "Switch to your target browser and press Ctrl+V to open.",
            ["S_ClipFailTitle"] = "Clipboard busy, copy failed",
            ["S_ClipFailMsg"] = "URL was saved to links.log. Please copy manually below.",
            ["S_ClipRetryFailMsg"] = "Still failed. Please select the text below and copy manually.",

            // Custom Browser Dialog
            ["S_CustomWinTitle"] = "Add / Edit Custom Browser",
            ["S_CustomTitleBar"] = "Custom Browser / Launch Arguments",
            ["S_CustomNameLabel"] = "Display Name",
            ["S_CustomNameHint"] = "e.g. Chrome Work / Portable Browser",
            ["S_CustomExeLabel"] = "Executable Path (.exe)",
            ["S_CustomExeHint"] = "Select or paste browser .exe path",
            ["S_CustomBrowse"] = "Browse...",
            ["S_CustomArgsLabel"] = "Launch Arguments Template (Optional)",
            ["S_CustomArgsHint"] = "Use {url} placeholder; if omitted, the quoted URL is appended at the end.",
            ["S_CustomArgsPlaceholder"] = "e.g. --profile-directory=\"Profile 1\" {url}",
            ["S_CustomPresetProfile"] = "Preset: Chrome Profile",
            ["S_CustomPresetIncognito"] = "Preset: Incognito",
            ["S_CustomTestLaunch"] = "Test Launch",
            ["S_CustomCancel"] = "Cancel",
            ["S_CustomSave"] = "Save",
            ["S_CustomFileFilter"] = "Executable (*.exe)|*.exe|All Files (*.*)|*.*",
            ["S_CustomFileTitle"] = "Select Browser Executable",
            ["S_CustomErrName"] = "Please enter a display name.",
            ["S_CustomErrExe"] = "Executable file does not exist.",
            ["S_CustomErrSelf"] = "Cannot add LinkToBrowser itself as a target browser."
        };

        private static ResourceDictionary _activeAppDict;

        public static bool IsEnglish { get; private set; }

        public static void Apply(LanguageMode mode, Window fallbackWindow = null)
        {
            IsEnglish = ResolveIsEnglish(mode);
            var source = IsEnglish ? EnUs : ZhCn;

            var targetResources = Application.Current?.Resources ?? fallbackWindow?.Resources;
            if (targetResources == null)
            {
                return;
            }

            var newDict = new ResourceDictionary();
            foreach (var kv in source)
            {
                newDict[kv.Key] = kv.Value;
            }

            if (Application.Current != null)
            {
                if (_activeAppDict != null)
                {
                    targetResources.MergedDictionaries.Remove(_activeAppDict);
                }

                targetResources.MergedDictionaries.Add(newDict);
                _activeAppDict = newDict;
            }
            else
            {
                targetResources.MergedDictionaries.Add(newDict);
            }
        }

        public static bool ResolveIsEnglish(LanguageMode mode)
        {
            return mode switch
            {
                LanguageMode.EnUs => true,
                LanguageMode.ZhCn => false,
                _ => !string.Equals(
                    CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                    "zh",
                    StringComparison.OrdinalIgnoreCase)
            };
        }

        public static string Get(string key)
        {
            var dict = IsEnglish ? EnUs : ZhCn;
            if (dict.TryGetValue(key, out var val))
            {
                return val;
            }

            return ZhCn.TryGetValue(key, out var fallback) ? fallback : key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }
    }
}
