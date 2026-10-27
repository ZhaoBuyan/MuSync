// MuSync 浏览器桥：把播放状态转发到本机 MuSync。
// 只在扩展后台（service worker）里发起请求 —— 那里有 host_permissions，不受页面 CORS 限制。
// 端口必须与 MuSync「设置 → 同步 → 浏览器扩展增强」里的端口一致。

const MUSYNC_PORT = 47564;

chrome.runtime.onMessage.addListener((message, _sender, _sendResponse) => {
  if (!message || message.type !== "musync-media" || !message.payload) return;
  const url = `http://127.0.0.1:${MUSYNC_PORT}/media`;
  fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(message.payload),
  }).catch(() => {
    // MuSync 没开 / 端口不对，静默忽略：这个扩展不该影响网页本身
  });
});
