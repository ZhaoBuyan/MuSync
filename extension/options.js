// 扩展设置页：端口在这里改（存 chrome.storage.local），改完不用重载扩展。
// background.js 会监听 storage 变化，下一次上报就用新端口。

const DEFAULT_PORT = 47564;

const portInput = document.getElementById("port");
const statusEl = document.getElementById("status");

function setStatus(text, ok) {
  statusEl.textContent = text;
  statusEl.className = "status " + (ok === true ? "ok" : ok === false ? "bad" : "");
}

function readPort() {
  const value = Number(portInput.value);
  if (!Number.isInteger(value) || value < 1 || value > 65535) return null;
  return value;
}

chrome.storage.local.get({ port: DEFAULT_PORT }, (value) => {
  portInput.value = value.port;
});

document.getElementById("save").addEventListener("click", () => {
  const port = readPort();
  if (port === null) {
    setStatus("端口要在 1–65535 之间", false);
    return;
  }
  chrome.storage.local.set({ port }, () => {
    setStatus(`已保存：${port}（下次上报即生效）`, true);
  });
});

document.getElementById("test").addEventListener("click", async () => {
  const port = readPort();
  if (port === null) {
    setStatus("端口要在 1–65535 之间", false);
    return;
  }
  setStatus("正在连接…");
  try {
    const response = await fetch(`http://127.0.0.1:${port}/status`, { cache: "no-store" });
    if (!response.ok) {
      setStatus(`MuSync 有响应但状态异常：HTTP ${response.status}`, false);
      return;
    }
    const data = await response.json();
    if (data && data.listening) {
      const extra = data.data && data.data.title ? `，最近收到「${data.data.title}」` : "，还没有收到播放数据";
      setStatus(`已连通 127.0.0.1:${port}${extra}`, true);
    } else {
      setStatus(`端口 ${port} 能连上，但 MuSync 未在监听（检查设置里的开关）`, false);
    }
  } catch {
    setStatus(`连不上 127.0.0.1:${port} —— MuSync 没开、端口不一致，或扩展增强未启用`, false);
  }
});
