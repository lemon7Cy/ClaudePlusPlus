# Claude++

Claude++ 是一个在本地浏览器中运行的 Claude Desktop 开发者控制面板。它可以识别官方客户端、启用官方 Developer Mode、启动或重启 Claude，并打开独立 DevTools 窗口。

项目只监听 `127.0.0.1`，不修改 `app.asar`，不绕过 Anthropic 的调试授权。

## 快速开始

### Windows

需要 [Node.js 20+](https://nodejs.org/)和 Microsoft Store / MSIX 版 Claude Desktop。

1. 双击 `Start-ClaudePlusPlus-Web.cmd`。
2. 默认浏览器会自动打开 `http://127.0.0.1:9344`。
3. 点击“启用并打开 DevTools”，或在 Claude 中按 `Ctrl+Alt+I`。

### macOS

需要 Node.js 20+ 和安装在 `/Applications` 或 `~/Applications` 的 `Claude.app`。

```bash
./start.command
```

也可以运行：

```bash
npm start
```

首次让 Claude++ 自动发送 `Cmd+Option+I` 时，macOS 可能会要求给终端或 Node 开启“辅助功能”权限。未授权时，仍可以在 Claude 窗口内手动按快捷键。

## 页面能力

- 检测 Claude 版本、安装位置和运行状态。
- 读取和更新官方 `developer_settings.json`，保留其他已有字段。
- 启动、唤醒或重启官方 Claude Desktop。
- 打开或聚焦 Claude 内置 DevTools。
- 显示本地操作日志和调试边界。
- 重复启动时复用已运行的本地服务，不会再启一个实例。

## 调试边界

Claude Desktop 有两种不同的调试入口：

1. **内置 DevTools**：由 `developer_settings.json` 中的 `allowDevTools` 控制，无需 token。Claude++ 使用的是这个官方入口。
2. **外部 CDP 端口**：Claude `1.30096.1` 会验证短时效 Anthropic 签名。本项目不包含 Anthropic 私钥，也不伪造、破解或绕过该签名。

因此，浏览器页面是启动和诊断面板；完整的 Elements、Console、Sources 和 Network 由 Claude 官方独立 DevTools 窗口提供。它不会生成 CodexPlusPlus 那种外部 inspector URL。

## 本地开发

浏览器版只使用 Node.js 内置模块，没有 npm 运行时依赖：

```bash
npm run check
npm run dev
```

默认端口是 `9344`。如需更换：

```powershell
$env:CLAUDE_PLUS_PLUS_PORT=9444
npm start
```

```bash
CLAUDE_PLUS_PLUS_PORT=9444 npm start
```

## Windows 桌面版

仓库仍保留原生 WinForms 启动器，作为 Windows 备用入口。构建需要 .NET 9 SDK：

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## 安全设计

- HTTP 服务只绑定 `127.0.0.1`。
- 写操作需要运行时随机 session token，不开放 CORS。
- Windows 上只会停止安装路径精确匹配的 Claude 进程。
- 不修改 Claude 安装目录，不注入 DLL 或替换官方资源。

## 许可证

[MIT](LICENSE)

本项目是 clean-room 实现，只参考了“外部启动器 + 调试面板”的通用产品形态，没有复制 CodexPlusPlus 源码或注入脚本。
