# LinkClip v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Portable single-file (< 20 MB) Windows 11 link router with 4 instantly switchable global modes and a WPF-UI settings window.

**Architecture:** `Program.Main` parses args and runs `LinkDispatcher` using only config + registry (no WPF, no browser scan) for silent paths; WPF-UI is started in a separate non-inlined method only when a window is needed. Costura embeds dependencies into one `net48` exe.

**Tech Stack:** .NET SDK 7.0.410, `net48`, C# 11, WPF-UI 3.0.5, Fody 6.9.3 + Costura.Fody 6.2.0, Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3, `JavaScriptSerializer`, xUnit 2.6.6 (+ Microsoft.NET.Test.Sdk 17.8.0, xunit.runner.visualstudio 2.5.6).

**Spec:** `d:\AntigravityWork\projects\LinkClip\docs\superpowers\specs\2026-11-27-linkclip-design.md`

## Global Constraints

- Windows 11 only. Project root: `d:\AntigravityWork\projects\LinkClip`.
- C# 11 on `net48`: no `init` accessors, no primary constructors; use `{ get; set; }` and explicit constructors.
- `NuGet.Config`: source `https://api.nuget.org/v3/index.json`, `globalPackagesFolder` = `.nuget\packages`.
- Release output is one `LinkClip.exe` < 20 MB, runnable alone.
- Registry writes HKCU only; never write `UserChoice`. Never touch `C:\Tools\LinkClip\`. No git commands.
- URL preserved verbatim (port, `#`, query, `%XX`); only strip leading `linkclip.url:` (case-insensitive). `"`→`%22` only inside `BuildArguments`.
- Dispatch path reads config, never writes it.
- Keep code minimal: no interfaces/abstractions without a second implementation, except where a test needs a fake (launcher, clipboard, ProgId resolver, key state — pass as `Func<>`).

## Review Focus

1. Unquoted registry commands with spaces / trailing flags (`C:\Program Files\Internet Explorer\iexplore.exe`, `G:\Quark\quark.exe --brand-quark`) → correct exe/args split (Task 3 test).
2. Browser launch failure or command line > 32000 chars → link copied to clipboard and confirm window shown, never lost (Task 4 test).
3. `UserChoice` already `LinkClip.URL` (this machine) or ProgId with no command (AppX) → NativePassthrough degrades to picker with warning, never self-launches (Task 4 test).
4. URL with `"` → clipboard gets original text; browser args get `%22` (Task 2 + Task 3 tests).
5. Tracking cleanup must not touch `#fragment` or `%XX` encoding (Task 2 test).

---

### Task 1: Scaffolding, models, config, log

**Files:**
- Create: `NuGet.Config`, `LinkClip.sln`, `LinkClip.csproj`, `FodyWeavers.xml`, `app.manifest`, `.gitignore`
- Create: `src/Program.cs` (stub `Main` returning 0), `src/Models/AppConfig.cs`, `src/Models/BrowserEntry.cs`, `src/Core/ConfigService.cs`, `src/Core/LogService.cs`
- Create: `tests/LinkClip.Tests/LinkClip.Tests.csproj`
- Test: `tests/LinkClip.Tests/ConfigTests.cs`

**Interfaces (produces):**
- `enum GlobalMode { NativePassthrough, DefaultBrowser, PickerPopup, ClipboardOnly }`
- `enum ModifierKey { None, Alt, Shift }`; `enum ThemeMode { System, Dark, Light }`; `enum BrowserKind { Registry, ChromiumProfile, Custom }`
- `class BrowserEntry { string Id; string Name; string ExecutablePath; string ArgumentsTemplate; string ProgId; BrowserKind Kind; static string MakeId(BrowserKind kind, string exe, string args) }` → `$"{kind}:{exe.Trim().ToLowerInvariant()}|{(args ?? "").Trim()}"`. All as `{ get; set; }` properties.
- `class AppConfig` defaults: `GlobalMode=ClipboardOnly`, `DefaultBrowser=null` (cached `BrowserEntry`), `OriginalDefaultProgId=""`, `Modifier=None`, `ShowClipboardPopup=true`, `ClipboardAutoCloseSeconds=0`, `StripTrackingParams=false`, `ShowChromiumProfiles=true`, `Theme=System`, `HiddenBrowserIds`, `BrowserOrder` (`List<string>`), `CustomBrowsers` (`List<BrowserEntry>`).
- `class ConfigService(string exeDir, string localAppDataDir)` → `AppConfig Load()`, `void Save(AppConfig)`, `string ConfigPath { get; }` (path last loaded/saved).
- `class LogService(string logDir)` → `LogArgs(string[])`, `LogLink(string url, bool ok, string detail = null)`, `Clear()`, `string LogDir`. Formats: `yyyy-MM-dd HH:mm:ss\tARGS\t a | b` (args.log), `yyyy-MM-dd HH:mm:ss\tOK|FAIL\t<url>[\t<detail>]` (links.log).

**csproj essentials:** `WinExe`, `net48`, `LangVersion 11.0`, `UseWPF`, `UseWindowsForms`, `ApplicationManifest=app.manifest`, `StartupObject=LinkClip.Program`, `EnableDefaultItems=false` with explicit `<Compile Include="src\**\*.cs" />` and `<Page Include="src\**\*.xaml" />` (`App.xaml` included as `Page`, not `ApplicationDefinition`, to avoid duplicate `Main` CS0017; excludes `tests\**` and legacy root `LinkClip.cs`), `Reference Include="System.Web.Extensions"`, plus `PackageReference`s for `Microsoft.NETFramework.ReferenceAssemblies.net48` (`1.0.3`), `WPF-UI` (`3.0.5`), `Fody` (`6.9.3`, `PrivateAssets=all`), and `Costura.Fody` (`6.2.0`, `PrivateAssets=all`). Test project: `net48`, `Microsoft.NETFramework.ReferenceAssemblies.net48` (`1.0.3`), `Microsoft.NET.Test.Sdk` (`17.8.0`), `xunit` (`2.6.6`), `xunit.runner.visualstudio` (`2.5.6`), `ProjectReference` to `..\..\LinkClip.csproj`.

- [ ] **Step 1: Write scaffolding + failing tests**
  - `Load_NoFiles_ReturnsDefaults` → `GlobalMode == ClipboardOnly`.
  - `Save_FirstTime_WritesExeDirWithoutTmpLeft` → file at `exeDir\config.json`, no `.tmp`.
  - `SaveTwice_RoundTripsFields` → second save (File.Replace path) keeps `GlobalMode=PickerPopup`, one custom browser named `Bit Kernel`.
  - `Load_Corrupt_ReturnsDefaults` → `"{bad"` content gives defaults.
  - `Load_PrefersExeDirOverLocalAppData` → both exist with different modes; exe-dir mode wins.
  - `MakeId_NormalizesCaseAndWhitespace`.
- [ ] **Step 2:** `dotnet test tests\LinkClip.Tests` → FAIL (types missing). (First run restores packages from nuget.org.)
- [ ] **Step 3:** Implement. Save: try exe dir, on `UnauthorizedAccessException`/`IOException` use local app data dir; write `.tmp`, then `File.Replace` if target exists else `File.Move`.
- [ ] **Step 4:** `dotnet test LinkClip.sln --nologo` → PASS.

---

### Task 2: CLI parsing and URL handling

**Files:**
- Create: `src/Core/CliParser.cs`, `src/Core/UrlService.cs`
- Test: `tests/LinkClip.Tests/CliUrlTests.cs`

**Interfaces (produces):**
- `enum CliKind { OpenSettings, Register, Unregister, Ignore, HandleUrl }`; `class CliInvocation { CliKind Kind; string RawUrl; bool NoUi; }`
- `static CliInvocation CliParser.Parse(string[] args)` — known flags `--settings --register --unregister --no-ui`; other `-`-prefixed args ignored; remaining args joined with `" "` into `RawUrl`; no URL and only unknown flags → `Ignore`; empty → `OpenSettings`.
- `static bool UrlService.TryNormalize(string raw, out string url)` — trim whitespace and outer `"`, strip `linkclip.url:`, reject chars `< 0x20`, require `Uri.TryCreate` absolute with scheme http/https; return the trimmed raw string (not `Uri.ToString()`).
- `static string UrlService.StripTracking(string url)` — split off `#...` first; drop query keys `utm_*`, `fbclid`, `gclid`, `msclkid`, `spm` (case-insensitive); rejoin raw tokens; drop `?` if query empty.
- `static string UrlService.Host(string url)` — for display.

- [ ] **Step 1: Failing tests**
  - Parse theory: `[]`→OpenSettings; `--settings`; `--register`; `--unregister`; `-Embedding`→Ignore; `--version`→Ignore; `--no-ui https://a.test`→HandleUrl+NoUi; `https://a.test/x y` (two args)→RawUrl `https://a.test/x y`.
  - TryNormalize keeps: `LinkClip.URL:https://a.example/x?y=1&z=2`→`https://a.example/x?y=1&z=2`; `"http://127.0.0.1:12606/#/sessions?id=abc"`→unquoted; `https://e.com/p?q=%E4%B8%AD` unchanged; `https://e.com/a?x="q"` unchanged (quotes kept).
  - TryNormalize rejects: `file:///C:/x.exe`, `javascript:alert(1)`, `https://e.com/a\r\nb`, `""`.
  - `StripTracking("http://h:1/p?keep=%E4%B8%AD&utm_source=wx&fbclid=1#/s?utm_source=x")` == `"http://h:1/p?keep=%E4%B8%AD#/s?utm_source=x"`; `StripTracking("https://h/p?utm_a=1")` == `"https://h/p"`.
- [ ] **Step 2:** `dotnet test LinkClip.sln --filter CliUrlTests` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test LinkClip.sln --nologo` → PASS.

---

### Task 3: Command splitting, launcher, browser catalog

**Files:**
- Create: `src/Core/CommandLineHelper.cs`, `src/Core/BrowserLauncher.cs`, `src/Core/BrowserCatalog.cs`
- Test: `tests/LinkClip.Tests/BrowserTests.cs`

**Interfaces (produces):**
- `static bool CommandLineHelper.TrySplit(string command, Func<string,bool> fileExists, out string exe, out string args)` — quoted head → exe; unquoted → try whitespace cut points longest-first, first existing file wins. `args` has `%1`/`%L` replaced by `{url}`.
- `class BrowserLauncher(string selfExePath, Func<ProcessStartInfo,bool> start = null)`:
  - `static string BuildArguments(string template, string url)` — rules in spec §4.2.
  - `bool TryLaunch(string exe, string argsTemplate, string url, out string error)` — errors: self path, missing file, total length > 32000, start exception. Uses `UseShellExecute=false`.
- `class BrowserCatalog`:
  - `static List<BrowserEntry> DiscoverRegistry(string selfExePath)` — spec §4.3 (HKCU/HKLM/WOW6432Node `StartMenuInternet`, via URLAssociations ProgId → command).
  - `static string ResolveProgIdCommand(string progId)` — HKCU then HKLM `Software\Classes\<progId>\shell\open\command`; null if none.
  - `static List<BrowserEntry> ParseChromiumProfiles(string name, string exe, string localStateJson)` — only if ≥ 2 profiles; template `--profile-directory="<dir>" {url}`; name `"<name> · <profile name>"`.
  - `static List<BrowserEntry> DiscoverChromium(IEnumerable<BrowserEntry> registryEntries)` — maps exe file name (`chrome.exe`, `msedge.exe`, `brave.exe`, `vivaldi.exe`) to `%LOCALAPPDATA%` User Data dir.
  - `static List<BrowserEntry> Arrange(IEnumerable<BrowserEntry> all, AppConfig cfg, bool includeHidden)` — dedupe by Id, `BrowserOrder` first then name; filter `HiddenBrowserIds` unless `includeHidden`.
  - `static List<BrowserEntry> GetAll(AppConfig cfg, string selfExePath)` — registry + (chromium if enabled) + custom, arranged.

- [ ] **Step 1: Failing tests**
  - `TrySplit`: `C:\Program Files\Internet Explorer\iexplore.exe` → exe whole, args `""`; `G:\Quark\quark.exe --brand-quark "%1"` → exe `G:\Quark\quark.exe`, args `--brand-quark "{url}"`; `"C:\Chrome\chrome.exe" --single-argument %1` → args `--single-argument {url}` (fake `fileExists`).
  - `BuildArguments` theory: `("", u)`→`"u"`; `("--incognito {url}", u)`→`--incognito "u"`; `("--profile-directory=\"P 1\" \"{url}\"", u)`→`--profile-directory="P 1" "u"`; `("-osint -url \"%1\"", u)`→`-osint -url "u"`; url `https://a.test/?x="q"` → contains `%22q%22`, no raw `"` inside the URL.
  - `TryLaunch_Self_Blocked` (start func throws if called); `TryLaunch_TooLong_Fails` (40000-char URL); `TryLaunch_StartThrows_ReturnsFalse`.
  - `ParseChromiumProfiles`: 2 profiles → 2 entries incl. `--profile-directory="Profile 2" {url}` named `Google Chrome · JP`; 1 profile → empty.
  - `Arrange`: order list respected, hidden filtered, `includeHidden` keeps them.
- [ ] **Step 2:** `dotnet test LinkClip.sln --filter BrowserTests` → FAIL.
- [ ] **Step 3:** Implement. (`DiscoverRegistry` not unit-tested; verified in Task 5 smoke run on this machine: expect Chrome, Edge, Quark present, no `Quark.mp4`, no LinkClip.)
- [ ] **Step 4:** `dotnet test LinkClip.sln --nologo` → PASS.

---

### Task 4: Clipboard, registration, dispatcher, Program fast path

**Files:**
- Create: `src/Core/ClipboardService.cs`, `src/Core/SystemRegistration.cs`, `src/Core/LinkDispatcher.cs`
- Modify: `src/Program.cs`
- Test: `tests/LinkClip.Tests/DispatcherTests.cs`

**Interfaces (produces):**
- `static bool ClipboardService.CopyWithRetry(string text, int timeoutMs = 3000)` — port v1 logic (backup, `SetDataObject(text, true, 5, 60)`, verify, restore on failure).
- `class SystemRegistration(string selfExePath)` → `void Register()`, `void Unregister()`, `string GetUserChoice(string scheme)`, `string GetRegisteredCommand()`, `bool PathMatchesSelf()`, `bool TrySnapshot(AppConfig cfg)` (spec §4.4), `static void OpenDefaultAppsSettings()` (`ms-settings:defaultapps`).
- `enum DispatchKind { Done, ShowPicker, ShowClipboard }`
- `class DispatchResult { DispatchKind Kind; int ExitCode; string Url; bool CopyOk; string Warning; }`
- `class LinkDispatcher` (`AppConfig cfg`, `Func<string,string,string,(bool ok,string err)> launch`, `Func<string,bool> copy`, `Func<string,string> resolveProgId`, `Func<ModifierKey,bool> isKeyDown`, `Func<string,(bool ok,string exe,string args)> splitCommand`) — keep as delegates for testability; `DispatchResult Dispatch(CliInvocation inv)`. `splitCommand` wraps `CommandLineHelper.TrySplit` with `File.Exists`.
  - Order per spec §4.1. Launch failure → `copy(url)` then `ShowClipboard` with `Warning = err`. ClipboardOnly: show window if `ShowClipboardPopup || !copyOk`.
- `Program.Main` (`[STAThread]`): log args → parse → Register/Unregister/Ignore handled inline → `OpenSettings` or `Dispatch` result needing UI → `RunUi(...)` marked `[MethodImpl(MethodImplOptions.NoInlining)]` (stub returning 0 until Task 5). On URL path, if `GetRegisteredCommand()` is non-empty and `!PathMatchesSelf()`, append a `WARN` entry to `args.log`.

- [ ] **Step 1: Failing tests** (all with fakes, no registry/clipboard):
  - `NoUi_AnyMode_CopiesOnly_NeverLaunches` (theory over 4 modes).
  - `Native_ResolvesProgId_LaunchesSilently` → `Done`, launch called with exe from resolved command.
  - `Native_ProgIdEmptyOrUnresolvable_ShowsPickerWithWarning`.
  - `Native_ProgIdIsLinkClip_ShowsPickerWithWarning`.
  - `Default_CachedBrowser_LaunchesSilently`; `Default_NotSet_ShowsPickerWithWarning`.
  - `Default_LaunchFails_CopiesAndShowsClipboardWithWarning`.
  - `ModifierHeld_DefaultMode_ShowsPicker`; `ModifierNone_IgnoresKeyState`.
  - `Clipboard_PopupOff_CopyOk_Done`; `Clipboard_PopupOff_CopyFails_ShowsClipboard`.
  - `StripTrackingEnabled_UrlCleanedBeforeLaunch`.
  - `InvalidUrl_ExitCode2_NoAction`.
- [ ] **Step 2:** `dotnet test LinkClip.sln --filter DispatcherTests` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test LinkClip.sln --nologo` → PASS.

---

### Task 5: WPF-UI windows, build script, docs, cleanup, smoke test

**Files:**
- Create: `src/App.xaml(.cs)`, `src/Views/SettingsWindow.xaml(.cs)`, `src/Views/PickerWindow.xaml(.cs)`, `src/Views/ClipboardWindow.xaml(.cs)`, `src/Views/CustomBrowserDialog.xaml(.cs)`, `src/Views/UiHelpers.cs`
- Modify: `src/Program.cs` (`RunUi` creates `App`, opens window per result)
- Create: `scripts/build.ps1`, `LICENSE` (MIT), `README.md` (rewrite)
- Delete (project dir only, after smoke passes): `LinkClip.cs`, `install.reg`, `install.ps1`, `fix-defaults.ps1`, old root `LinkClip.exe`

**Interfaces:**
- Consumes everything from Tasks 1–4.
- `UiHelpers`: `ImageSource GetIcon(string exePath)` (`Icon.ExtractAssociatedIcon` + `CreateBitmapSourceFromHIcon`, cached, default on failure); `void PlaceNearCursor(Window w)` (cursor + `Screen.FromPoint().WorkingArea`, converted by window DPI, clamped); `bool TryActivateExistingSettings()` + `Mutex` named `Local\LinkClip.Settings`.
- `App`: merges `ThemesDictionary` + `ControlsDictionary`; applies `cfg.Theme` (System → `SystemThemeWatcher`).
- Windows per spec §5. Settings saves via `ConfigService.Save` on every change and calls `TrySnapshot` on open. Selecting a default browser stores a copy of the `BrowserEntry` into `cfg.DefaultBrowser`. ClipboardWindow auto-close: `DispatcherTimer`, skipped when copy failed, paused on `MouseEnter`/textbox focus, resumed on leave. Picker "Ctrl+C" and empty-list state per spec.
- `scripts/build.ps1`: `dotnet test -c Release`, then `dotnet build LinkClip.csproj -c Release`, copy `bin\Release\net48\LinkClip.exe` to `dist\LinkClip.exe`, print size in MB, fail if ≥ 20 MB.

- [ ] **Step 1:** Implement App + 4 windows + UiHelpers; wire `RunUi`.
- [ ] **Step 2:** Write `build.ps1`, `LICENSE`, `README.md` (positioning, 4 modes, `--register` / settings registration, how to restore system default, build instructions).
- [ ] **Step 3: Build** — `powershell -ExecutionPolicy Bypass -File scripts\build.ps1` → tests PASS, `dist\LinkClip.exe` < 20 MB.
- [ ] **Step 4: Standalone smoke** in `d:\AntigravityWork\scratch\linkclip_smoke\` (copy only `dist\LinkClip.exe`):
  - `-Embedding` and `--version` → exit 0, clipboard unchanged.
  - `--no-ui "LinkClip.URL:http://127.0.0.1:12606/#/sessions?id=abc&n=%E4%B8%AD"` → exit 0, clipboard exactly `http://127.0.0.1:12606/#/sessions?id=abc&n=%E4%B8%AD`.
  - Launch with no args → settings window opens (screenshot / visual check by user); Browser page lists Chrome, Edge, Quark; no `Quark.mp4`, no LinkClip. Second launch activates existing window.
  - Do **not** run `--register` from the smoke folder; confirm `HKCU\Software\Classes\LinkClip.URL\shell\open\command` still `"C:\Tools\LinkClip\LinkClip.exe" "%1"`.
  - Delete smoke folder.
- [ ] **Step 5:** Delete v1 leftovers listed above from project dir; rerun `scripts\build.ps1` → PASS.
