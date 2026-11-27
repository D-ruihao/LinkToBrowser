# LinkToBrowser

全便携、免安装、Windows 11 原生设置风格的链接分流与剪贴板拦截工具，面向多浏览器、Chromium 多账号以及指纹浏览器用户。

## 核心特点

- **全便携设计（配置与日志全部随文件夹带走）**：
  - 配置文件 `config.json` 与运行日志 `logs\links.log`、`logs\args.log` 全部保存在程序所在目录内（仅在放到无权限系统目录时回退到 `%LOCALAPPDATA%\LinkToBrowser`）。
  - 整文件夹拷贝即可迁移全部自定义浏览器和配置；关闭设置窗口即彻底退出，零后台常驻。
- **4 种全局处理模式一键切换**：在设置面板里一秒切换，满足不同时段的使用习惯。
- **免安装开箱即用**：基于 Windows 11 系统自带 `.NET Framework 4.8` 与 `WPF-UI`，下载解压便携包 `LinkToBrowser-portable.zip` 即可运行，无需安装额外运行时。
- **支持指纹浏览器与自定义参数**：除自动扫描系统注册表浏览器（自动过滤媒体文件伪关联）与 Chromium 多用户配置外，支持添加任意绿色版/指纹浏览器内核 `.exe` 与 `{url}` 启动参数模板。
- **0 风险、可随时还原系统默认**：所有注册均局限在 `HKCU`，绝不硬改受保护的 `UserChoice` 哈希；启用前自动记录原默认浏览器 `ProgId`，支持一键静默透传或彻底打开系统设置还原并清理注册表。
- **静默快路径**：在「Windows 原生」「默认指定浏览器」「无弹窗剪贴板」以及 `--no-ui` 路径下直接转发，不加载 WPF 界面开销。

## 四种全局链接处理模式

1. **Windows 原生**：静默转给启用前记录的系统原默认浏览器（每次运行时动态查注册表命令）。
2. **默认指定浏览器**：静默使用你在 LinkToBrowser 中指定的常规浏览器或自定义 `.exe` + 参数打开。
3. **弹出浏览器选择界面**：在光标附近弹出紧凑选择卡片，支持 `1–9` 数字快选、实时打字筛选、`↑/↓` + `Enter`、`Ctrl+C` 复制链接。
4. **默认复制到剪贴板**：3 秒重试防占用写入剪贴板（失败自动还原剪贴板原内容），可配确认弹窗与自动关闭倒计时（悬停或聚焦链接框自动暂停），并可一键临时「用浏览器打开…」。

## 快速使用

1. 下载并解压 `LinkToBrowser-portable.zip` 到任意固定目录（例如 `C:\Tools\LinkToBrowser\`）。
2. 双击 `LinkToBrowser.exe` 打开设置窗口（配置 `config.json` 与日志 `logs\` 均保存在该目录）。
3. 点击 **「立即注册」**（或在命令行运行 `LinkToBrowser.exe --register`，无需管理员权限），再点击 **「打开默认应用设置」**，搜索 `LinkToBrowser` 并设为默认即可。

## 命令行参数

- 无参数或 `--settings`：单实例打开设置窗口
- `--register`：将当前位置的 `LinkToBrowser.exe` 注册到 `HKCU` 并尝试记录原系统默认 `ProgId`
- `--unregister`：清理 `HKCU` 下的 LinkToBrowser 注册项
- `--no-ui <url>`：强制静默复制链接到剪贴板（成功退出码 `0`，失败 `2`）
- `-Embedding` / `--version` 等：静默退出且不改动剪贴板

## 彻底恢复系统默认状态

在设置窗口点击 **「彻底交还…」**（或运行 `LinkToBrowser.exe --unregister` 后打开 Windows 设置），在系统「默认应用」中选回原来的浏览器，并按提示选择清理 `HKCU` 注册项即可完全恢复，无任何残留风险。

## 本地构建与打包

依赖环境：Windows 11 + .NET SDK（如 .NET 7+ SDK）。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

脚本会运行单元测试，并生成便携目录 `dist\LinkToBrowser\` 与发布压缩包 `dist\LinkToBrowser-portable.zip`。NuGet 包缓存配置在项目目录 `.\.nuget\packages` 下。

## 开源协议

[MIT](LICENSE)
