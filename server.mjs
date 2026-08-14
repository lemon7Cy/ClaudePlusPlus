import { createServer } from "node:http";
import { execFile } from "node:child_process";
import { randomBytes } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import { homedir, platform as operatingSystem } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const rootDirectory = path.dirname(fileURLToPath(import.meta.url));
const publicDirectory = path.join(rootDirectory, "public");
const windowsScript = path.join(rootDirectory, "scripts", "windows.ps1");
const applicationId = "claude-plus-plus";
const applicationVersion = "0.1.0";
const host = "127.0.0.1";
const requestedPort = Number(process.env.CLAUDE_PLUS_PLUS_PORT || "9344");
const shouldOpenBrowser = process.argv.includes("--open");
const sessionToken = randomBytes(24).toString("base64url");
const logs = [];

if (!Number.isInteger(requestedPort) || requestedPort < 1 || requestedPort > 65_535) {
  throw new Error(`CLAUDE_PLUS_PLUS_PORT 无效：${process.env.CLAUDE_PLUS_PLUS_PORT}`);
}

let installationCache;
let statusCache;
let statusCacheAt = 0;
let actionInProgress = false;

function addLog(message, level = "info") {
  logs.push({
    timestamp: new Date().toISOString(),
    level,
    message,
  });
  if (logs.length > 160) logs.splice(0, logs.length - 160);
}

function powershellPath() {
  return path.join(
    process.env.WINDIR || "C:\\Windows",
    "System32",
    "WindowsPowerShell",
    "v1.0",
    "powershell.exe",
  );
}

async function runFile(file, args, options = {}) {
  const result = await execFileAsync(file, args, {
    cwd: rootDirectory,
    windowsHide: true,
    maxBuffer: 4 * 1024 * 1024,
    timeout: 30_000,
    ...options,
  });
  return {
    stdout: result.stdout.trim(),
    stderr: result.stderr.trim(),
  };
}

async function detectWindowsInstallation() {
  const command = [
    "Get-AppxPackage -Name Claude",
    "Sort-Object Version -Descending",
    "Select-Object -First 1 Name,PackageFamilyName,PackageFullName,InstallLocation,@{n='Version';e={$_.Version.ToString()}}",
    "ConvertTo-Json -Compress",
  ].join(" | ");
  const { stdout } = await runFile(powershellPath(), [
    "-NoProfile",
    "-NonInteractive",
    "-Command",
    command,
  ]);
  if (!stdout) throw new Error("未检测到 Microsoft Store / MSIX 版 Claude。请先安装 Claude。 ");

  const packageInfo = JSON.parse(stdout);
  const userDataPath = path.join(process.env.LOCALAPPDATA, "Claude-3p");
  return {
    platform: "windows",
    platformLabel: "Windows",
    version: packageInfo.Version,
    packageFullName: packageInfo.PackageFullName,
    packageFamilyName: packageInfo.PackageFamilyName,
    appUserModelId: `${packageInfo.PackageFamilyName}!Claude`,
    installLocation: packageInfo.InstallLocation,
    executablePath: path.join(packageInfo.InstallLocation, "app", "Claude.exe"),
    userDataPath,
    configPath: path.join(userDataPath, "developer_settings.json"),
  };
}

async function detectMacInstallation() {
  const candidates = [
    "/Applications/Claude.app",
    path.join(homedir(), "Applications", "Claude.app"),
  ];
  const appPath = candidates.find(existsSync);
  if (!appPath) throw new Error("未在 /Applications 或 ~/Applications 中找到 Claude.app。");

  let version = "unknown";
  try {
    ({ stdout: version } = await runFile("/usr/bin/plutil", [
      "-extract",
      "CFBundleShortVersionString",
      "raw",
      path.join(appPath, "Contents", "Info.plist"),
    ]));
  } catch {
    // Version is informational; launching still works without it.
  }

  const userDataPath = path.join(homedir(), "Library", "Application Support", "Claude");
  return {
    platform: "macos",
    platformLabel: "macOS",
    version,
    appPath,
    executablePath: path.join(appPath, "Contents", "MacOS", "Claude"),
    userDataPath,
    configPath: path.join(userDataPath, "developer_settings.json"),
  };
}

async function detectInstallation(force = false) {
  if (installationCache && !force) return installationCache;
  const platform = operatingSystem();
  if (platform === "win32") installationCache = await detectWindowsInstallation();
  else if (platform === "darwin") installationCache = await detectMacInstallation();
  else throw new Error(`当前仅支持 Windows 和 macOS，检测到 ${platform}。`);
  return installationCache;
}

async function readDeveloperConfig(installation) {
  try {
    const raw = await readFile(installation.configPath, "utf8");
    const value = JSON.parse(raw);
    return value && typeof value === "object" && !Array.isArray(value) ? value : {};
  } catch (error) {
    if (error?.code === "ENOENT") return {};
    throw new Error(`无法读取 developer_settings.json：${error.message}`);
  }
}

async function setDeveloperMode(installation, enabled) {
  const config = await readDeveloperConfig(installation);
  config.allowDevTools = enabled;
  await mkdir(installation.userDataPath, { recursive: true });
  const temporaryPath = `${installation.configPath}.tmp.${process.pid}.${Date.now()}`;
  await writeFile(temporaryPath, `${JSON.stringify(config, null, 2)}\n`, "utf8");
  await rename(temporaryPath, installation.configPath);
  addLog(`已设置 allowDevTools=${enabled}：${installation.configPath}`);
}

async function windowsAction(action, installation) {
  return runFile(powershellPath(), [
    "-NoProfile",
    "-NonInteractive",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    windowsScript,
    "-Action",
    action,
    "-AppUserModelId",
    installation.appUserModelId,
    "-ExecutablePath",
    installation.executablePath,
  ], { timeout: 45_000 });
}

async function isClaudeRunning(installation) {
  if (installation.platform === "windows") {
    const command = [
      `$expected=[IO.Path]::GetFullPath('${installation.executablePath.replaceAll("'", "''")}')`,
      "$found=Get-Process Claude -ErrorAction SilentlyContinue | Where-Object { try { [IO.Path]::GetFullPath($_.Path) -ieq $expected } catch { $false } } | Select-Object -First 1",
      "if($found){'true'}else{'false'}",
    ].join("; ");
    const { stdout } = await runFile(powershellPath(), ["-NoProfile", "-NonInteractive", "-Command", command]);
    return stdout === "true";
  }

  try {
    await runFile("/usr/bin/pgrep", ["-x", "Claude"]);
    return true;
  } catch {
    return false;
  }
}

async function launchClaude(installation) {
  if (installation.platform === "windows") {
    await windowsAction("Launch", installation);
  } else {
    await runFile("/usr/bin/open", [installation.appPath]);
  }
  addLog("已启动或唤醒 Claude。");
}

async function stopClaude(installation) {
  if (installation.platform === "windows") {
    await windowsAction("Stop", installation);
  } else {
    try {
      await runFile("/usr/bin/osascript", ["-e", 'tell application "Claude" to quit']);
    } catch {
      // Claude may already be closed.
    }
  }
  addLog("已请求关闭 Claude。", "warning");
}

async function waitForClaude(installation, timeoutMs = 18_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await isClaudeRunning(installation)) return;
    await new Promise((resolve) => setTimeout(resolve, 450));
  }
  throw new Error("Claude 在等待时间内没有启动完成。");
}

async function openClaudeDevTools(installation) {
  if (installation.platform === "windows") {
    await windowsAction("OpenDevTools", installation);
  } else {
    await runFile("/usr/bin/osascript", [
      "-e",
      'tell application "Claude" to activate',
      "-e",
      'tell application "System Events" to keystroke "i" using {command down, option down}',
    ]);
  }
  addLog("已调用 Claude 官方 DevTools 快捷键。 ");
}

async function openConfigFolder(installation) {
  await mkdir(installation.userDataPath, { recursive: true });
  if (installation.platform === "windows") {
    await runFile("explorer.exe", ["/select,", installation.configPath]).catch(() =>
      runFile("explorer.exe", [installation.userDataPath]),
    );
  } else {
    await runFile("/usr/bin/open", ["-R", installation.configPath]).catch(() =>
      runFile("/usr/bin/open", [installation.userDataPath]),
    );
  }
  addLog("已打开开发者配置位置。");
}

async function buildStatus(force = false) {
  if (!force && statusCache && Date.now() - statusCacheAt < 900) return statusCache;
  try {
    const installation = await detectInstallation(force);
    const developerConfig = await readDeveloperConfig(installation);
    statusCache = {
      ok: true,
      installation,
      developerModeEnabled: developerConfig.allowDevTools === true,
      running: await isClaudeRunning(installation),
      actionInProgress,
      logs,
      externalCdp: {
        available: false,
        reason: "Claude 外部 CDP 需要 Anthropic 签名授权；本项目使用官方内置 Developer Mode。",
      },
    };
  } catch (error) {
    statusCache = {
      ok: false,
      error: error.message,
      platform: operatingSystem(),
      actionInProgress,
      logs,
    };
  }
  statusCacheAt = Date.now();
  return statusCache;
}

async function performAction(action) {
  if (actionInProgress) throw new Error("已有操作正在执行，请稍候。 ");
  actionInProgress = true;
  statusCacheAt = 0;
  try {
    const installation = await detectInstallation();
    switch (action) {
      case "launch":
        await launchClaude(installation);
        break;
      case "enable-restart":
        await setDeveloperMode(installation, true);
        await stopClaude(installation);
        await new Promise((resolve) => setTimeout(resolve, 900));
        await launchClaude(installation);
        await waitForClaude(installation);
        await new Promise((resolve) => setTimeout(resolve, 1_800));
        await openClaudeDevTools(installation);
        break;
      case "open-devtools": {
        const config = await readDeveloperConfig(installation);
        if (config.allowDevTools !== true) throw new Error("请先启用 Developer Mode。");
        if (!(await isClaudeRunning(installation))) {
          await launchClaude(installation);
          await waitForClaude(installation);
          await new Promise((resolve) => setTimeout(resolve, 1_500));
        }
        await openClaudeDevTools(installation);
        break;
      }
      case "disable":
        await setDeveloperMode(installation, false);
        addLog("重启 Claude 后，Developer Mode 将完全关闭。", "warning");
        break;
      case "show-config":
        await openConfigFolder(installation);
        break;
      default:
        throw new Error(`未知操作：${action}`);
    }
    return await buildStatus(true);
  } finally {
    actionInProgress = false;
    statusCacheAt = 0;
  }
}

function writeJson(response, statusCode, value) {
  response.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
  });
  response.end(JSON.stringify(value));
}

async function readJsonBody(request) {
  const chunks = [];
  let length = 0;
  for await (const chunk of request) {
    length += chunk.length;
    if (length > 64 * 1024) throw new Error("请求体过大。");
    chunks.push(chunk);
  }
  if (!chunks.length) return {};
  return JSON.parse(Buffer.concat(chunks).toString("utf8"));
}

const mimeTypes = new Map([
  [".html", "text/html; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".svg", "image/svg+xml"],
  [".png", "image/png"],
]);

async function serveStatic(requestPath, response) {
  const relativePath = requestPath === "/" ? "index.html" : decodeURIComponent(requestPath.slice(1));
  const resolvedPath = path.resolve(publicDirectory, relativePath);
  if (!resolvedPath.startsWith(`${path.resolve(publicDirectory)}${path.sep}`) && resolvedPath !== path.join(publicDirectory, "index.html")) {
    writeJson(response, 403, { error: "Forbidden" });
    return;
  }
  try {
    const content = await readFile(resolvedPath);
    response.writeHead(200, {
      "Content-Type": mimeTypes.get(path.extname(resolvedPath)) || "application/octet-stream",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
      "Content-Security-Policy": "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'",
      "Referrer-Policy": "no-referrer",
    });
    response.end(content);
  } catch (error) {
    writeJson(response, error?.code === "ENOENT" ? 404 : 500, { error: error.message });
  }
}

const server = createServer(async (request, response) => {
  try {
    const url = new URL(request.url, `http://${host}`);
    if (request.method === "GET" && url.pathname === "/api/meta") {
      writeJson(response, 200, { app: applicationId, version: applicationVersion });
      return;
    }
    if (request.method === "GET" && url.pathname === "/api/session") {
      writeJson(response, 200, { token: sessionToken });
      return;
    }
    if (request.method === "GET" && url.pathname === "/api/status") {
      writeJson(response, 200, await buildStatus(url.searchParams.get("force") === "1"));
      return;
    }
    if (request.method === "POST" && url.pathname === "/api/action") {
      if (request.headers["x-claude-plus-plus-token"] !== sessionToken) {
        writeJson(response, 403, { error: "Invalid session token" });
        return;
      }
      const body = await readJsonBody(request);
      writeJson(response, 200, await performAction(body.action));
      return;
    }
    if (request.method !== "GET") {
      writeJson(response, 405, { error: "Method not allowed" });
      return;
    }
    await serveStatic(url.pathname, response);
  } catch (error) {
    addLog(error.message, "error");
    writeJson(response, 500, { error: error.message });
  }
});

async function openBrowser(url) {
  if (operatingSystem() === "win32") {
    await runFile(powershellPath(), ["-NoProfile", "-NonInteractive", "-Command", `Start-Process '${url}'`]);
  } else if (operatingSystem() === "darwin") {
    await runFile("/usr/bin/open", [url]);
  }
}

async function handleListenError(error) {
  const url = `http://${host}:${requestedPort}`;
  if (error?.code === "EADDRINUSE") {
    try {
      const response = await fetch(`${url}/api/meta`, {
        signal: AbortSignal.timeout(1_500),
      });
      const metadata = await response.json();
      if (response.ok && metadata.app === applicationId) {
        console.log(`Claude++ Web Console 已在运行：${url}`);
        if (shouldOpenBrowser) await openBrowser(url);
        process.exit(0);
      }
    } catch {
      // The occupied port belongs to another application.
    }
    console.error(`端口 ${requestedPort} 已被其他程序占用。可设置 CLAUDE_PLUS_PLUS_PORT 更换端口。`);
  } else {
    console.error(`Claude++ Web Console 启动失败：${error.message}`);
  }
  process.exit(1);
}

server.on("error", (error) => void handleListenError(error));

server.listen(requestedPort, host, async () => {
  const address = server.address();
  const actualPort = typeof address === "object" && address ? address.port : requestedPort;
  const url = `http://${host}:${actualPort}`;
  addLog(`Claude++ Web Console 已启动：${url}`);
  console.log(`Claude++ Web Console: ${url}`);
  if (shouldOpenBrowser) {
    try {
      await openBrowser(url);
    } catch (error) {
      addLog(`无法自动打开浏览器：${error.message}`, "warning");
    }
  }
});

process.on("SIGINT", () => server.close(() => process.exit(0)));
process.on("SIGTERM", () => server.close(() => process.exit(0)));
