const elements = {
  platformBadge: document.querySelector("#platformBadge"),
  runningBadge: document.querySelector("#runningBadge"),
  versionValue: document.querySelector("#versionValue"),
  installValue: document.querySelector("#installValue"),
  developerModeValue: document.querySelector("#developerModeValue"),
  configValue: document.querySelector("#configValue"),
  shortcutText: document.querySelector("#shortcutText"),
  errorPanel: document.querySelector("#errorPanel"),
  logList: document.querySelector("#logList"),
  busyIndicator: document.querySelector("#busyIndicator"),
  toast: document.querySelector("#toast"),
};

let sessionToken = "";
let busy = false;
let pollTimer;

function setBadge(element, text, kind) {
  element.textContent = text;
  element.className = `badge ${kind}`;
}

function setBusy(value) {
  busy = value;
  elements.busyIndicator.classList.toggle("hidden", !value);
  document.querySelectorAll("button[data-action]").forEach((button) => {
    button.disabled = value && button.dataset.action !== "refresh";
  });
}

function showToast(message) {
  elements.toast.textContent = message;
  elements.toast.classList.remove("hidden");
  window.clearTimeout(showToast.timer);
  showToast.timer = window.setTimeout(() => elements.toast.classList.add("hidden"), 3200);
}

function formatTime(timestamp) {
  return new Intl.DateTimeFormat("zh-CN", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
  }).format(new Date(timestamp));
}

function renderLogs(logs = []) {
  const wasNearBottom = elements.logList.scrollTop + elements.logList.clientHeight >= elements.logList.scrollHeight - 30;
  elements.logList.replaceChildren();
  if (!logs.length) {
    const empty = document.createElement("div");
    empty.textContent = "等待操作…";
    empty.className = "log-entry";
    elements.logList.append(empty);
    return;
  }

  for (const entry of logs.slice().reverse()) {
    const row = document.createElement("div");
    row.className = `log-entry ${entry.level}`;
    const time = document.createElement("time");
    time.textContent = formatTime(entry.timestamp);
    const level = document.createElement("span");
    level.className = "log-level";
    level.textContent = entry.level;
    const message = document.createElement("span");
    message.textContent = entry.message;
    row.append(time, level, message);
    elements.logList.append(row);
  }
  if (wasNearBottom) elements.logList.scrollTop = elements.logList.scrollHeight;
}

function renderStatus(status) {
  setBusy(status.actionInProgress || busy);
  renderLogs(status.logs);

  if (!status.ok) {
    elements.errorPanel.textContent = status.error || "无法检测 Claude。";
    elements.errorPanel.classList.remove("hidden");
    setBadge(elements.platformBadge, status.platform || "未知平台", "warning");
    setBadge(elements.runningBadge, "不可用", "warning");
    return;
  }

  elements.errorPanel.classList.add("hidden");
  const installation = status.installation;
  setBadge(elements.platformBadge, installation.platformLabel, "neutral");
  setBadge(elements.runningBadge, status.running ? "Claude 运行中" : "Claude 未运行", status.running ? "good" : "warning");
  elements.versionValue.textContent = installation.version || "未知";
  elements.installValue.textContent = installation.installLocation || installation.appPath || installation.executablePath;
  elements.configValue.textContent = installation.configPath;
  elements.developerModeValue.textContent = status.developerModeEnabled ? "已启用" : "未启用";
  elements.developerModeValue.style.color = status.developerModeEnabled ? "var(--green)" : "#976021";
  elements.shortcutText.textContent = installation.platform === "macos" ? "⌘ + ⌥ + I" : "Ctrl + Alt + I";
}

async function fetchJson(url, options) {
  const response = await fetch(url, options);
  const body = await response.json();
  if (!response.ok) throw new Error(body.error || `HTTP ${response.status}`);
  return body;
}

async function refreshStatus(force = false) {
  const status = await fetchJson(`/api/status${force ? "?force=1" : ""}`);
  renderStatus(status);
  return status;
}

async function performAction(action) {
  if (busy) return;
  if (action === "refresh") {
    await refreshStatus(true);
    showToast("状态已刷新");
    return;
  }
  if (action === "enable-restart") {
    const confirmed = window.confirm("此操作会重启 Claude，正在执行的 Claude Code / Cowork 任务会中断。继续吗？");
    if (!confirmed) return;
  }

  setBusy(true);
  try {
    const status = await fetchJson("/api/action", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Claude-Plus-Plus-Token": sessionToken,
      },
      body: JSON.stringify({ action }),
    });
    renderStatus(status);
    showToast("操作完成");
  } catch (error) {
    showToast(error.message);
    await refreshStatus(true).catch(() => undefined);
  } finally {
    setBusy(false);
  }
}

document.addEventListener("click", (event) => {
  const button = event.target.closest("button[data-action]");
  if (button) performAction(button.dataset.action);
});

async function boot() {
  try {
    sessionToken = (await fetchJson("/api/session")).token;
    await refreshStatus(true);
    pollTimer = window.setInterval(() => {
      if (!busy && document.visibilityState === "visible") refreshStatus().catch(() => undefined);
    }, 1800);
  } catch (error) {
    elements.errorPanel.textContent = error.message;
    elements.errorPanel.classList.remove("hidden");
  }
}

window.addEventListener("beforeunload", () => window.clearInterval(pollTimer));
boot();
