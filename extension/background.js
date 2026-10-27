// MuSync 浏览器桥：把播放状态转发到本机 MuSync。
//
// 端口在扩展的**设置页**里改（扩展详情 → 扩展选项），存 chrome.storage.local；
// 这里的 DEFAULT_PORT 只是"没设置过"时的兜底值，平时不用动。
//
// 为什么在后台（service worker）发请求：那里有 host_permissions，
// 不受页面 CORS 限制；放在 content script 里发会被网页自己的跨域策略拦掉。

const DEFAULT_PORT = 47564;

let musyncPort = DEFAULT_PORT;

chrome.storage.local.get({ port: DEFAULT_PORT }, (value) => {
  if (value && value.port) musyncPort = value.port;
});

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && changes.port) {
    musyncPort = changes.port.newValue || DEFAULT_PORT;
  }
});

chrome.runtime.onMessage.addListener((message, _sender, _sendResponse) => {
  if (!message || message.type !== "musync-media" || !message.payload) return;
  const url = `http://127.0.0.1:${musyncPort}/media`;
  fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(message.payload),
  }).catch(() => {
    // MuSync 没开 / 端口不对，静默忽略：这个扩展不该影响网页本身
  });
});
