// MuSync 浏览器桥（页面侧）：每秒读一次当前页面里的媒体，上报给扩展后台 → MuSync。
//
// 为什么两路都读：
//   1) navigator.mediaSession.metadata —— 页面主动发布的元数据，标题通常最准（B站/YouTube 等）
//   2) <video>/<audio> 元素           —— 位置、总长、播放/暂停只能从这里拿
// 两者合起来才是完整的一条状态；系统媒体控件（SMTC）读不到的站点，往往也能靠这里救回来。

(() => {
  const REPORT_INTERVAL_MS = 1000;
  let lastSentAt = 0;

  function pickMediaElement() {
    const all = Array.from(document.querySelectorAll("video, audio"));
    if (all.length === 0) return null;
    // 优先"正在播放且有声"的；其次"播放过的"（暂停时仍要能显示）；最后才取第一个
    const playing = all.filter((el) => !el.paused && !el.ended);
    if (playing.length > 0) {
      return playing.reduce((a, b) => ((b.currentTime || 0) >= (a.currentTime || 0) ? b : a));
    }
    const started = all.filter((el) => (el.currentTime || 0) > 0 && !el.ended);
    if (started.length > 0) {
      return started.reduce((a, b) => ((b.currentTime || 0) >= (a.currentTime || 0) ? b : a));
    }
    return null;
  }

  function readMetadata() {
    try {
      const meta = navigator.mediaSession && navigator.mediaSession.metadata;
      if (meta && (meta.title || meta.artist || meta.album)) {
        return {
          title: (meta.title || "").trim(),
          artist: (meta.artist || "").trim(),
          album: (meta.album || "").trim(),
        };
      }
    } catch {
      // 某些页面会替换 mediaSession，读失败就当没有
    }
    return { title: "", artist: "", album: "" };
  }

  function buildPayload() {
    const el = pickMediaElement();
    if (!el) return null;

    const meta = readMetadata();
    const title = meta.title || (document.title || "").trim();
    if (!title) return null;

    const duration = Number.isFinite(el.duration) && el.duration > 0 ? el.duration : 0;
    const position = Number.isFinite(el.currentTime) && el.currentTime > 0 ? el.currentTime : 0;
    const playing = !el.paused && !el.ended;

    return {
      title,
      artist: meta.artist,
      album: meta.album,
      position,
      duration,
      playing,
      site: location.hostname || "",
    };
  }

  function tick() {
    const now = Date.now();
    if (now - lastSentAt < REPORT_INTERVAL_MS) return;
    let payload = null;
    try {
      payload = buildPayload();
    } catch {
      return;
    }
    if (!payload) return;   // 没有媒体（或已结束）就不上报，MuSync 侧会因超时自动清掉
    lastSentAt = now;
    try {
      chrome.runtime.sendMessage({ type: "musync-media", payload });
    } catch {
      // 扩展被重新加载时 sendMessage 会抛，忽略即可
    }
  }

  setInterval(tick, REPORT_INTERVAL_MS);
  // 切歌/换页时尽快补一拍，不必等下一次定时
  document.addEventListener("visibilitychange", tick, { passive: true });
  tick();
})();
