<div align="center">
  <img src="src/Assets/LinkToBrowser.svg" width="96" height="96" alt="LinkToBrowser Logo" />
  <h1>LinkToBrowser</h1>
  <p>
    <strong>Windows 11 默认浏览器链接路由、选择器与剪贴板工具</strong><br/>
    <sub>在多个浏览器、多账号配置与剪贴板工作流之间自由切换链接打开方式</sub>
  </p>
  <p>
    <strong>简体中文</strong> | <a href="README.en.md">English</a>
    &nbsp;·&nbsp;
    <a href="https://github.com/D-ruihao/LinkToBrowser/releases">下载 Release</a>
  </p>
</div>

---

## 核心功能

- **绿色便携、免安装**
  - 基于 Windows 11 自带 `.NET Framework 4.8` 运行，下载解压即可使用，无需额外安装运行时。
  - 配置 `config.json` 与日志 `logs\` 默认保存在程序同级目录（在无写入权限的系统目录下自动回退至 `%LOCALAPPDATA%\LinkToBrowser`）。
- **四种链接处理模式**
  1. **Windows 原生**：静默转发给启用前记录的系统原默认浏览器，支持随时改回系统默认。
  2. **默认指定浏览器**：静默使用选定的浏览器或自定义可执行文件打开链接。
  3. **弹出浏览器选择界面**：点击链接时在光标附近弹出选择窗口，支持 `1–9` 数字快选、实时过滤、`↑/↓` + `Enter` 打开以及 `Ctrl+C` 复制。
  4. **默认复制到剪贴板**：将链接复制到剪贴板，支持可选确认浮窗与自动关闭倒计时（悬停或聚焦链接框时自动暂停）。
- **多账号与自定义启动支持**
  - 自动发现已安装的浏览器及 Chromium 多用户配置（Chrome / Edge Profile），并支持添加自定义 `.exe` 与 `{url}` 启动参数模板。
  - 支持配置全局修饰键（`Alt` 或 `Shift`），在其他模式下按住该键点击链接时临时弹出选择窗口。
- **用户级注册、易于恢复**
  - 全部注册表操作仅写入当前用户（`HKCU`），无需管理员权限，支持随时一键清理注册项并打开系统设置还原默认浏览器。
- **双语与主题自适应**
  - 支持 **简体中文 / English / 跟随系统** 实时切换，支持深色、浅色与跟随 Windows 11 系统主题。

---

## 界面预览

| 设置主面板 | 浏览器选择弹窗 |
| :---: | :---: |
| ![Settings Window](docs/screenshots/settings-en.png) | ![Picker Window](docs/screenshots/picker-en.png) |

| 剪贴板确认浮窗 |
| :---: |
| ![Clipboard Popup](docs/screenshots/clipboard-en.png) |

---

## 快速开始

1. 前往 [Releases](https://github.com/D-ruihao/LinkToBrowser/releases) 下载 `LinkToBrowser-portable.zip` 并解压到固定目录（例如 `C:\Tools\LinkToBrowser\`）。
2. 运行 `LinkToBrowser.exe` 打开设置窗口（可在设置中切换界面语言为 **简体中文** 或 **English**）。
3. 点击 **「立即注册」**（或使用 `--register` 参数），再点击 **「打开默认应用设置」**，在 Windows 设置中将 `LinkToBrowser` 设为 `HTTP` / `HTTPS` 默认应用即可。

### 命令行参数

- 无参数或 `--settings`：打开设置窗口
- `--register`：注册当前位置的 `LinkToBrowser.exe` 到 `HKCU` 并记录原默认浏览器 `ProgId`
- `--unregister`：移除 `HKCU` 下的 `LinkToBrowser` 注册项
- `--no-ui <url>`：静默复制链接到剪贴板（成功返回 `0`，失败返回 `2`）

### 还原与卸载

在设置中点击 **「还原默认…」**（或运行 `LinkToBrowser.exe --unregister` 后打开 Windows 默认应用设置），改回原浏览器并清理 `HKCU` 注册项，随后删除程序目录即可。

---

## 从源码构建

需要 Windows 11 与 .NET SDK（.NET 7+ SDK）。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

输出便携目录 `dist\LinkToBrowser\` 与压缩包 `dist\LinkToBrowser-portable.zip`。

## 开源协议

[MIT](LICENSE)
