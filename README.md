<div align="center">
  <img src="src/Assets/LinkToBrowser.svg" width="96" height="96" alt="LinkToBrowser Logo" />
  <h1>LinkToBrowser</h1>
  <p>
    <strong>Windows 11 链接分流路由、浏览器选择器与剪贴板拦截工具</strong><br/>
    <sub>将外部点击的 HTTP / HTTPS 链接按规则自动分流到指定浏览器、最近聚焦窗口、指纹浏览器或剪贴板</sub>
  </p>
  <p>
    <strong>简体中文</strong> | <a href="README.en.md">English</a>
    &nbsp;·&nbsp;
    <a href="https://github.com/D-ruihao/LinkToBrowser/releases">下载 Release</a>
  </p>
</div>

---

## 核心功能

Windows 11 系统默认应用只能固定绑定单个浏览器，且无法将外部软件点击的链接直接打开到运行中的独立 / 指纹浏览器窗口。将 `LinkToBrowser` 设为 `HTTP` / `HTTPS` 默认应用后，可按以下方式自动路由每个链接：

- **按链接分流规则**：支持 **域名**（含子域名通配与端口）、**URL 前缀**、**通配符**（`*` / `?`），按自上而下优先级匹配；命中后可转交指定浏览器、Chromium 多账号 Profile、自定义 `.exe`、运行中的独立窗口，或执行剪贴板 / 弹窗 / 最近聚焦 / 原生默认。内置默认停用的示例模板与实时 URL 测试框，按住 `Alt` / `Shift` 修饰键可临时改弹选择窗。
- **五种全局模式**（未命中规则时生效）：
  1. **最近聚焦的浏览器**：按窗口活跃顺序自动打开到最近使用或点击聚焦的浏览器（含已最小化窗口），支持无可用窗口时的回退策略。
  2. **弹出浏览器选择界面**：在光标附近弹出选择窗，支持 `1–9` 快选、搜索过滤、`↑/↓` + `Enter` 打开与 `Ctrl+C` 复制。
  3. **默认指定浏览器**：静默使用选定的浏览器、多账号 Profile、活跃独立窗口或自定义 `.exe` 打开。
  4. **复制到剪贴板**：拦截链接并复制，支持可选确认浮窗与自动关闭倒计时。
  5. **Windows 原生**：静默转发给接管前记录的系统原默认浏览器。
- **独立 / 指纹浏览器直跳（`DevToolsActivePort` / CDP）**：开启后可发现暴露本地 `DevToolsActivePort` 的隔离 Chromium 窗口，在规则、最近聚焦、选择弹窗及默认指定模式下直接向目标窗口新建标签页。*兼容性说明：目前仅在比特浏览器（BitBrowser）完成实机验证，其它开放标准 `DevToolsActivePort` 的隔离浏览器视自身配置兼容；不可用时自动安全回退并复制到剪贴板。*
- **配置备份与 CLI 接口**：支持完整配置或仅规则的 JSON 导入 / 导出（导入前自动备份为 `config.bak.json`，支持合并或覆盖）；提供无窗口命令行与 UTF-8 `--json` 输出，便于脚本与自动化调用。
- **绿色便携与用户级注册**：基于 Windows 11 自带 `.NET Framework 4.8`，无需管理员权限即可完成 `HKCU` 注册与清理；配置 `config.json` 与日志 `logs\` 默认存放在程序同级目录，支持中英双语实时切换与深浅色 Mica 主题自适应。

---

## 界面预览

| 设置主面板（常规 / 模式） | 分流规则与测试预览 |
| :---: | :---: |
| ![Settings Window](docs/screenshots/settings-v211.png) | ![Rules Page](docs/screenshots/rules-v211.png) |

| 浏览器选择弹窗 | 剪贴板确认浮窗 |
| :---: | :---: |
| ![Picker Window](docs/screenshots/picker-v21.png) | ![Clipboard Popup](docs/screenshots/clipboard-v21.png) |

---

## 快速开始

1. 从 [Releases](https://github.com/D-ruihao/LinkToBrowser/releases) 下载 `LinkToBrowser-portable.zip` 并解压到固定目录（如 `C:\Tools\LinkToBrowser\`）。
2. 运行 `LinkToBrowser.exe`，点击 **「立即注册」**（或执行 `--register`）。
3. 点击 **「打开默认应用设置」**，将 `LinkToBrowser` 设为 `HTTP` 和 `HTTPS` 默认应用即可。

**还原与卸载**：在设置中点击 **「还原默认…」**（或执行 `--unregister`），在 Windows 默认应用里改回原浏览器并清理 `HKCU` 注册项，随后删除目录即可。

---

## 命令行接口（CLI）

```powershell
# 查看命令、状态与浏览器列表
LinkToBrowser.exe --help --json
LinkToBrowser.exe --status --include-running --json
LinkToBrowser.exe --list-browsers --include-running --json

# 切换模式 / 管理规则 / 模拟路由测试
LinkToBrowser.exe --set-mode recent --fallback clipboard --json
LinkToBrowser.exe --rules add --name "GitHub" --kind domain --pattern "github.com, *.github.com" --action open --target "Edge" --json
LinkToBrowser.exe --test-url "https://gist.github.com/D-ruihao" --json

# 定向打开链接 / 备份与恢复（支持 --config-dir 指定隔离目录）
LinkToBrowser.exe --browser "Edge" "https://example.com" --json
LinkToBrowser.exe --export ".\backup.json" --rules-only --json
LinkToBrowser.exe --import ".\backup.json" --json
```

常规参数：无参数或 `--settings`（打开设置）、`--register` / `--unregister`（注册/清理）、`--no-ui <url>`（静默复制，成功返回 `0`）。  
*提示：因程序为无黑框 GUI 子系统，在 PowerShell 中查看或解析 stdout 请配合管道（如 `| ConvertFrom-Json` 或 `| Out-String`）；子进程重定向标准输出可直接读取 UTF-8 JSON。*

---

## 从源码构建

需要 Windows 11 与 .NET 7+ SDK：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

产物输出至 `dist\LinkToBrowser\` 与 `dist\LinkToBrowser-portable.zip`。

## 开源协议

[MIT](LICENSE)
