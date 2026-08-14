# Claude++

Claude++ 是面向官方 Claude Desktop 的 clean-room 开发者启动器。它使用 Claude 官方 `developer_settings.json` 开关启用 Developer Mode，并打开完整的 Electron DevTools。

DevTools 包含：

- Elements
- Console
- Sources
- Network
- Performance
- Application

## Windows 使用

需要 Microsoft Store / MSIX 版 Claude Desktop 和 .NET 9 Desktop Runtime。

1. 运行 `ClaudePlusPlus.exe`。
2. 点击“启用并重启”。
3. Claude 重启后会打开独立 DevTools 窗口。
4. 之后可在 Claude 中按 `Ctrl+Alt+I` 打开或聚焦 DevTools。

## 为什么不是浏览器 URL

Codex++ 可以启动 Codex App 时附加：

```text
--remote-debugging-port=9229
```

然后使用 Chromium 自带的：

```text
http://127.0.0.1:9229/devtools/inspector.html?ws=...
```

Claude Desktop `1.30096.1` 对 `remote-debugging-port` 和 `remote-debugging-pipe` 增加了 `CLAUDE_CDP_AUTH` 签名校验。授权数据包含短时效时间戳、用户数据目录和 Anthropic Ed25519 签名。

本项目不包含 Anthropic 私钥，也不修改 `app.asar`、伪造授权或绕过签名。因此无法在普通浏览器中生成与 Codex++ 相同的 inspector URL。

Claude 官方内置 DevTools 使用 Electron 内部调试通道，不需要外部 CDP token，并能完整查看源码、DOM、网络请求和运行时事件。

## 适合的调研方式

- 在 Sources 中使用 `Ctrl+Shift+F` 搜索 UI 文案、IPC 命令和功能开关。
- 对压缩 JavaScript 使用 Pretty print（`{}`）后下断点。
- 在 Network 中开启 Preserve log，观察 Claude API、SSE、MCP 和附件请求。
- 在 Console 中观察前端事件、IPC 报错和功能开关。
- 在 Elements 中检查 Cowork / Code 页面的 DOM、样式和可访问性。

## 构建

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

输出：`publish\ClaudePlusPlus.exe`

## 安全边界

- 不修改或替换官方 `app.asar`。
- 不向 Claude 安装目录写入 DLL 或脚本。
- 只会重启执行路径与已检测 Claude 安装精确匹配的进程。
- 保留 `developer_settings.json` 中的其他现有字段。

## License

[MIT](LICENSE)
