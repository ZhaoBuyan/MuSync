// MuSync 浏览器桥（页面侧）：把当前页面里正在播放的音视频上报给扩展后台 → MuSync。
//
// 为什么两路都读：
//   1) navigator.mediaSession.metadata —— 页面主动发布的元数据，标题通常最准（B站/YouTube 等）
//   2) <video>/<audio> 元素           —— 位置、总长、播放/暂停只能从这里拿
// 两者合起来才是完整的一条状态；系统媒体控件（SMTC）读不到的站点，往往也能靠这里救回来。
//
// 为什么同时用"定时 + 事件"两条上报路径：
//   浏览器会把**后台标签页**的 setInterval 节流到 1 分钟一次（甚至更低）。
//   只听定时器的话，在后台放视频时 MuSync 会以为数据过期（>5 秒）并回落到系统接口 ——
//   而对"系统里根本没有会话"的站点，这就等于完全失效。
//   媒体元素自己的 timeupdate / play / pause 事件由播放驱动，不受标签页节流影响，
//   所以主路径改成事件驱动，定时器只作兜底。

(() => {
  const MIN_GAP_MS = 250;          // 任何两次上报之间的最小间隔（事件可能连发）
  const PROGRESS_GAP_MS = 900;     // 进度类事件（timeupdate）的间隔：位置上没必要更密
  const FALLBACK_INTERVAL_MS = 1000;

  let lastSentAt = 0;
  const bound = new WeakSet();

  // 本页面的稳定标识：同一标签即使标题/换集变化也保持不变，用来把"同站点同标题的两个标签"
  // 区分开。否则两个标签都开着同一个视频时，MuSync 会把它们当成同一个页面，播放状态与进度互相覆盖。
  const sourceId = (() => {
    try {
      if (typeof crypto !== "undefined" && crypto.randomUUID) return crypto.randomUUID();
    } catch {
      // 老浏览器没有 randomUUID，退回时间 + 随机数
    }
    return Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10);
  })();

  function pickMediaElement() {
    const all = Array.from(document.querySelectorAll("video, audio"));
    if (all.length === 0) return null;
    // 优先"正在播放"的；其次"播放过的"（暂停时仍要能显示）；广告/装饰性元素放最后
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
      // 页面是否在前台：MuSync 用它决定"前台新开的视频要不要接管后台挂着的直播"。
      // 两个都在后台时仍保持在位者，避免多标签每秒互相顶掉。
      visible: document.visibilityState === "visible",
      // 页面实例标识：同站点同标题的两个标签靠它区分（标题会重复，它不是身份）
      sourceId,
    };
  }

  /// gapMs：本次上报要求的最小间隔。状态变化用 MIN_GAP_MS，进度用 PROGRESS_GAP_MS。
  function report(gapMs) {
    const now = Date.now();
    if (now - lastSentAt < Math.max(MIN_GAP_MS, gapMs || 0)) return;

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

  function attach(el) {
    if (!el || bound.has(el)) return;
    bound.add(el);
    // 播放状态变化：立即上报
    for (const event of ["play", "pause", "ended", "seeked", "ratechange", "loadedmetadata"]) {
      el.addEventListener(event, () => report(0), { passive: true });
    }
    // 进度推进：由播放驱动，后台标签页也不被节流
    el.addEventListener("timeupdate", () => report(PROGRESS_GAP_MS), { passive: true });
  }

  function attachAll() {
    try {
      document.querySelectorAll("video, audio").forEach(attach);
    } catch {
      // 页面结构异常时忽略，定时器还会兜底
    }
  }

  // 新出现的媒体元素（SPA 换集、广告插入）也要挂上监听
  let attachPending = false;
  function scheduleAttachAll() {
    if (attachPending) return;
    attachPending = true;
    setTimeout(() => {
      attachPending = false;
      attachAll();
    }, 300);
  }

  try {
    new MutationObserver(scheduleAttachAll).observe(document.documentElement, {
      childList: true,
      subtree: true,
    });
  } catch {
    // 极端情况下 documentElement 还不可用：忽略，仍有定时器兜底
  }

  attachAll();
  document.addEventListener("visibilitychange", () => report(0), { passive: true });
  // 兜底：某些站点（如画中画、WebAudio）可能不触发 timeupdate
  setInterval(() => report(FALLBACK_INTERVAL_MS), FALLBACK_INTERVAL_MS);
  report(0);
})();
