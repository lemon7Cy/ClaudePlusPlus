# Claude++

Claude++ 是面向官方 Claude Desktop 的 Windows 开发者启动器，提供两种调试方式：

1. **官方内置 DevTools**：写入 Claude 官方 `developer_settings.json` 开关，通过 `Ctrl+Alt+I` 打开完整 Electron DevTools。
2. **实验性浏览器 CDP**：从经过严格版本校验的 Claude 创建可写副本，启动真实 Chromium CDP，并生成普通浏览器可访问的 inspector 地址。

DevTools 包含 Elements、Console、Sources、Network、Performance、Application 等完整面板。

## 浏览器 CDP 效果

成功启动后会得到真实 Claude renderer target，例如：

```text
http://127.0.0.1:9344/devtools/inspector.html?ws=127.0.0.1:9344/devtools/page/<targetId>
```

这不是 Claude UI 镜像，也不是重新实现的 WebUI。`/json/list` 返回的页面直接指向 Claude 自己的：

```text
file:///.../resources/app.asar/.vite/renderer/main_window/index.html
```

## Windows 使用

需要 Microsoft Store / MSIX 版 Claude Desktop 和 .NET 9 Desktop Runtime。

1. 运行 `ClaudePlusPlus.exe`。
2. 若要查看完整官方功能，点击“启用并重启”，再使用内置 DevTools。
3. 若要获得浏览器 URL，点击“准备并启动 CDP”。
4. 首次运行会把当前 Claude `app` 目录复制到 `%LOCALAPPDATA%\ClaudePlusPlus\runtime`，约占用 630 MB。
5. 启动器会显示并自动打开 inspector 地址，也可点击“复制网址”。

实验性副本使用独立资料目录 `%LOCALAPPDATA%\ClaudePlusPlus\profiles`，不会占用或修改官方 Claude 的登录资料。

## 实现原理

Claude Desktop `1.30096.1` 会在主进程启动早期检测 `remote-debugging-port` 和 `remote-debugging-pipe`。没有短时效 `CLAUDE_CDP_AUTH` Ed25519 签名时，程序会直接退出。

Claude++ 仅在可写副本中执行两个固定、可验证的修改：

- 对 `app.asar` 中唯一的 CDP 退出 guard 做等长字节替换，不改变 ASAR 文件布局。
- 在副本 `Claude.exe` 的 Electron v1 fuse wire 中关闭 `EnableEmbeddedAsarIntegrityValidation`，使已修改的副本 ASAR 可以加载。

官方 WindowsApps 安装目录不会被写入。补丁前会校验产品版本和官方 `app.asar` SHA-256；更新后只要版本或哈希不匹配，就会拒绝盲目应用补丁。

当前已验证清单：

| Claude 应用版本 | 官方 `app.asar` SHA-256 |
| --- | --- |
| `1.30096.1` | `122359D81D2FBAAA5E88B5E5BFFE9CB78DD8FAF1F5E38B125DBDA5E9E28AB91B` |

## 已知边界

- CDP 只绑定 `127.0.0.1`，不会监听局域网网卡。
- 副本不具备原 Microsoft Store 包身份，因此少数明确要求 MSIX 身份的 Cowork / 系统集成功能可能显示为不可用。
- 需要研究完整 MSIX 功能时，使用 Claude++ 保留的“官方内置 DevTools”模式。
- Claude 更新后需要为新版本重新确认 guard、Electron fuse 和哈希，再添加新的补丁清单。
- 副本 `Claude.exe` 的原厂代码签名会因 fuse 修改而失效；原安装文件不受影响。

## 命令行冒烟验证

源码构建后可运行：

```powershell
dotnet .\bin\Release\net9.0-windows\ClaudePlusPlus.dll --cdp-smoke-test
```

命令会准备或复用副本、启动 CDP，并输出 target、WebSocket 和 inspector URL。

底层诊断脚本：

```powershell
.\scripts\Prepare-ClaudeCdpRuntime.ps1 `
  -SourceAppDirectory 'C:\Program Files\WindowsApps\Claude_1.30096.1.0_x64__pzs8sxrjxfjjc\app' `
  -Version '1.30096.1' `
  -ExpectedAsarSha256 '122359D81D2FBAAA5E88B5E5BFFE9CB78DD8FAF1F5E38B125DBDA5E9E28AB91B' `
  -Port 9344
```

## 构建

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

输出：`publish\ClaudePlusPlus.exe`

## 安全与恢复

- 官方 `app.asar` 和 `Claude.exe` 始终只读。
- 所有实验文件都位于 `%LOCALAPPDATA%\ClaudePlusPlus`，删除该目录即可清理实验副本和独立资料。
- 启动器只复用 URL 指向已校验运行目录的 CDP target，不会误连其他本机调试端口。
- 官方内置模式只会修改 `developer_settings.json` 的 `allowDevTools` 字段，并保留其他字段和首次备份。

## License

[MIT](LICENSE)
