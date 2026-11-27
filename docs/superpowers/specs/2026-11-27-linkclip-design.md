# LinkClip v2 设计文档（精简定稿）

- **日期**：2026-11-27
- **项目目录**：`d:\AntigravityWork\projects\LinkClip`
- **平台**：仅 Windows 11
- **定位**：全便携免安装（打包文件夹 < 20 MB，依赖系统自带 .NET Framework 4.8，配置与日志全部存于程序同目录）、Win11 原生设置风格、4 种全局链接处理模式、面向 GitHub 开源的多浏览器 / 指纹浏览器链接分流与剪贴板工具。
- **原则**：小项目，只写必要代码；几行到几十行、不引入新生命周期或并发问题的小功能可以加；不做过度防御。

---

## 1. 背景

用户有多个常规浏览器和指纹浏览器（如比特浏览器），点外部链接常常进不了想要的浏览器；现有工具识别不了指纹浏览器，界面也不合心意（BrowserPicker 已评估过，放弃）。v1 LinkClip 把所有链接截进剪贴板，解决了燃眉之急；v2 要把它做成有界面、可快速切换处理方式的开源小工具。

借鉴：BrowserPicker（数字键快选、弹窗复制链接、Chromium 多配置发现）、BrowserSelector（自定义 exe + `{url}` 参数模板）、Browser Tamer（追踪参数清理）。

---

## 2. 目标 / 非目标 / 反目标

### 目标

1. **4 种全局模式，在设置页一键切换、立即生效**
   - `NativePassthrough`（Windows 原生）：静默转给“原系统默认浏览器”。只保存它的 `ProgId`，每次运行时去注册表查当前的启动命令；查不到就降级为弹窗并提示。设置页另有「彻底交还系统默认」：打开 Windows「默认应用」设置页，并可勾选清理 LinkClip 的 HKCU 注册项。
   - `DefaultBrowser`（默认指定浏览器）：静默启动用户选定的浏览器。选定时把该浏览器的路径和参数**缓存进配置**，点链接时不重新扫描。
   - `PickerPopup`（弹出选择窗）：在光标所在屏幕、光标附近弹出；数字键 1–9 快选、打字过滤、↑↓ + Enter、Ctrl+C 复制链接、⚙ 打开设置、Esc 关闭。
   - `ClipboardOnly`（复制到剪贴板）：3 秒重试写入剪贴板（失败还原原内容）；可选弹出确认窗，确认窗支持 N 秒后自动关闭（0 = 不自动关闭），并有「用浏览器打开…」。
2. **浏览器发现**：扫描注册表 `StartMenuInternet`（HKCU / HKLM / WOW6432Node），只保留声明了 http/https 关联的项，按“路径 + 参数”去重；可选发现 Chromium 多用户配置（Chrome / Edge / Brave / Vivaldi）；支持手动添加任意 exe + 参数模板（覆盖指纹浏览器内核和绿色版浏览器）。
3. **小功能**：主题（跟随系统 / 深色 / 浅色）、追踪参数清理（默认关）、修饰键临时弹窗（None / Alt / Shift，默认 None）、设置窗口单实例、清空日志。
4. **全便携免安装与默认零后台**：
   - 以便携压缩包（`LinkClip-portable.zip`）形式发布，解压即用，总体积 < 20 MB（不强求 Costura 单文件打包）。
   - `config.json` 与 `logs\` 全部优先保存在 `LinkClip.exe` 同目录（目录不可写时回退至 `%LOCALAPPDATA%\LinkClip`）。
   - 关闭设置窗口即彻底退出，无后台。

### 非目标

- 托盘常驻、开机自启；域名规则引擎（以及为它预留的空接口）；直接调用指纹浏览器的本地 API。
- Win10 兼容；直接修改 `UserChoice`；捆绑 SetUserFTA。

### 反目标

- URL 原样进入剪贴板，也原样交给浏览器：端口、`#`、查询串、`%XX` 编码都不改动；只剥掉 `linkclip.url:` 前缀。
- 链接不能丢：启动浏览器失败时（exe 不存在、启动异常、命令行超长），一律降级为“复制到剪贴板 + 确认窗提示”。
- 不能启动自身（防止死循环）；注册表只写 HKCU。
- 不动 `C:\Tools\LinkClip\`；未经允许不做任何 Git 操作。

---

## 3. 技术栈

- SDK 风格 `.csproj`，`net48`，`LangVersion 11`（本机 .NET SDK 7.0.410），`UseWPF` + `UseWindowsForms`（只用来调剪贴板 API 和取屏幕信息）。
- `WPF-UI 3.0.5`（Win11 Fluent 控件、Mica、主题）；`Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3`；通过 `scripts/build.ps1` 输出 `dist\LinkClip\` 便携目录并打为 `dist\LinkClip-portable.zip`。
- JSON 用系统自带的 `JavaScriptSerializer`（`System.Web.Extensions`）。
- `NuGet.Config` 把包缓存放在 `.\.nuget\packages`（不占 C 盘）。
- `app.manifest` 声明按显示器 DPI 适配（PerMonitorV2）。
- 测试：xUnit，只测纯函数和分发决策。

---

## 4. 关键机制

### 4.1 入口：`Program.Main` 快慢分流

```text
解析参数：非开关参数用空格拼回 URL（沿用 v1 行为）
  --register / --unregister           → 注册/反注册（--register 时顺便记录原默认浏览器快照）→ 退出
  无参数 / --settings                  → 单实例打开设置窗口
  只有不认识的 '-' 开关                → 静默退出
  URL 不合法（非 http/https、含控制字符）→ 记日志，退出码 2
有 URL：
  清理追踪参数（若开启）
  --no-ui                             → 只复制到剪贴板，退出码 0/2（优先级最高）
  修饰键被按住（若配置）               → 本次按 PickerPopup 处理
  NativePassthrough                   → 按 ProgId 查命令并启动；查不到 → 弹窗 + 提示
  DefaultBrowser                      → 用缓存的路径和参数启动；没设置 → 弹窗 + 提示
  ClipboardOnly                       → 复制；开了弹窗或复制失败 → 确认窗
  PickerPopup                         → 选择窗
启动失败                               → 复制 + 确认窗（显示失败原因）
```

静默路径不加载 WPF：UI 启动放在一个单独的非内联方法里。点链接时**只读配置、不写配置**。

### 4.2 参数拼接（`BrowserLauncher.BuildArguments`）

- 模板里的 `%1`、`%L` 统一视作 `{url}`。
- `"{url}"` 替换成 `"<url>"`；没加引号的 `{url}` 替换成 `"<url>"`；模板里没有占位符时，在末尾追加 `"<url>"`。
- 只在这一步把 URL 里的 `"` 编码成 `%22`（防止引号逃逸）。
- 拼好的命令行超过 32000 字符，视为启动失败。
- 目标 exe 的完整路径等于当前 exe 时拒绝启动。

### 4.3 浏览器发现

- `StartMenuInternet\<Key>`：读 `Capabilities\URLAssociations\https`（没有就读 `http`）得到 `ProgId`，再去 `Classes\<ProgId>\shell\open\command`（先 HKCU，后 HKLM）取启动命令；读不到关联或命令的项跳过（`Quark.mp4` 这类伪条目也就自然被过滤掉）。名称取 `Capabilities\ApplicationName`，没有就用键的默认值，再没有就用键名。`ProgId` 为 `LinkClip.URL` 或路径指向自身的项排除。
- 命令拆分（`CommandLineHelper.TrySplitCommand`）：以引号开头时取引号里的内容作为 exe；不加引号时，在空白处从长到短截取，找到磁盘上存在的 exe（兼容 `C:\Program Files\...\iexplore.exe`、`G:\Quark\quark.exe --brand-quark`）。
- 浏览器 ID：`<kind>:<小写 exe 路径>|<参数>`。
- Chromium 多配置：在已发现的条目里找 `chrome.exe` / `msedge.exe` / `brave.exe` / `vivaldi.exe`，读对应 `User Data\Local State` 里的 `profile.info_cache`；有 2 个以上配置时，为每个配置生成一条 `--profile-directory="<目录>" {url}` 条目，名称形如「Google Chrome · 工作」。
- 排序与隐藏：`BrowserOrder` 里列出的 ID 排在前面，其余按名称排序；`HiddenBrowserIds` 只影响选择窗，不影响默认浏览器。
- 图标：只在界面层用系统 API 取 exe 的关联图标，失败显示默认图标。

### 4.4 系统注册与快照（`SystemRegistration`）

- 注册：写 `HKCU\Software\Classes\LinkClip.URL`、`HKCU\Software\LinkClip\Capabilities`、`HKCU\Software\RegisteredApplications\LinkClip`。
- 快照：在打开设置窗口和 `--register` 时执行。读 `UserChoice` 的 https `ProgId`（没有就读 http）；不是 `LinkClip.URL`、并且能查到启动命令时，写入 `OriginalDefaultProgId`。如果本机当前默认已经是 LinkClip（本机现状就是这样），设置页允许从已发现的系统浏览器中选一个，把它的 `ProgId` 作为透传目标。
- 状态检测：http/https 当前的 `ProgId`、注册命令是否指向当前 exe。点链接时如果发现注册路径和当前 exe 不一致，写一条 WARN 日志。

### 4.5 配置读写（`ConfigService`）

- 读：exe 同目录有 `config.json` 就读它，否则读 `%LOCALAPPDATA%\LinkClip\config.json`，都没有就用默认值；文件损坏时也用默认值。读的时候不做写入探测。
- 写：优先写 exe 同目录，写入失败（无权限）就改写 `%LOCALAPPDATA%`。先写 `.tmp`，目标文件已存在时用原子替换（`File.Replace`），不存在时用 `File.Move`。

---

## 5. 界面（WPF-UI）

- **设置窗口**（`FluentWindow` + Mica，约 820×580，参照 Windows Terminal 设置页）
  - 左侧导航 3 页：
    - **常规**：注册状态卡片（含「注册 / 更新路径」「打开系统默认应用」按钮）；4 种模式单选卡片（「Windows 原生」显示/选择透传目标并带「彻底交还系统默认」入口；「默认指定浏览器」带下拉框；「剪贴板」带“显示确认窗”开关和自动关闭秒数）；其余开关：修饰键、Chromium 多配置、追踪参数清理、主题。
    - **浏览器**：添加自定义、重新扫描；列表每项有图标、名称、来源标签、上移/下移、显示/隐藏、测试启动，自定义项另有编辑和删除。
    - **关于**：配置文件路径、日志目录、清空日志、版本号、MIT 许可。
  - 改动即时保存；单实例（互斥锁 + 前置已有窗口）。
- **选择窗**：无边框圆角、置顶，宽约 440；顶部显示域名和完整 URL，带复制、⚙ 按钮；过滤框；带 1–9 序号的列表；可显示降级提示条；列表为空时提示，并提供「复制链接」「打开设置」。
- **剪贴板确认窗**：宽约 520；成功/失败状态、只读 URL（默认全选）、「再复制一次」「用浏览器打开…」「打开日志目录」「⚙」「关闭 (Esc)」。自动关闭：复制失败时不自动关闭；鼠标悬停或文本框获得焦点时暂停倒计时。
- **自定义浏览器对话框**：名称、exe（文件选择）、参数模板（提示 `{url}`，提供两个预设）、测试启动。

---

## 6. 目录结构

```text
LinkClip/
├── LinkClip.sln  LinkClip.csproj  NuGet.Config  FodyWeavers.xml  app.manifest
├── README.md  LICENSE  .gitignore
├── scripts/build.ps1
├── docs/superpowers/{specs,plans}/
├── src/
│   ├── Program.cs  App.xaml(.cs)
│   ├── Models/   AppConfig.cs  BrowserEntry.cs
│   ├── Core/     CliParser.cs  UrlService.cs  CommandLineHelper.cs  BrowserLauncher.cs
│   │             ConfigService.cs  LogService.cs  ClipboardService.cs
│   │             BrowserCatalog.cs  SystemRegistration.cs  LinkDispatcher.cs
│   └── Views/    SettingsWindow  PickerWindow  ClipboardWindow  CustomBrowserDialog  (.xaml/.cs)
│                 UiHelpers.cs（图标、光标附近定位、单实例）
└── tests/LinkClip.Tests/
```

项目目录内 v1 遗留的 `LinkClip.cs`、`install.reg`、`install.ps1`、`fix-defaults.ps1` 以及旧的 `LinkClip.exe`，在 v2 构建并冒烟验证通过后删除（`C:\Tools\LinkClip\` 保持不动）。
