# 安全与隐私说明 / Security & privacy

**中文** ｜ [English](#security--privacy)

> 这份文档写给每一位准备把 Steam 账号授权给 MuSync 的人：
> 它拿了你的账号做什么、读了什么、发了什么、**不做什么**、怎么验证、怎么撤销。
> 全部有一说一，不回避风险。

## 一、登录：你的账号密码经历了什么

1. 你在登录框输入账号密码（支持 Steam 手机令牌 / 邮箱验证码，与浏览器登录流程一致）
2. MuSync 通过 **Steam 官方网络协议**（SteamKit2 开源库）**直连 Steam 官方服务器**发起登录。
   密码不经过任何第三方 —— **本项目没有作者服务器，也没有任何中间服务**
3. Steam 官方验证通过后，下发一枚 **refresh token（登录令牌）**
4. 密码使命结束：**仅登录瞬间存在于内存，不写盘、不缓存**
5. 令牌保存在本机 `%LocalAppData%\MuSync\config.json`，使用 **Windows DPAPI 加密**
   （密钥绑定你的 Windows 账号，文件被拷到其他电脑/用户无法解密，等于没有）
6. 登录框里**不勾选"记住我"则不保存令牌**，每次启动手动登录

## 二、令牌是什么、能干什么（坦白讲）

- refresh token ≈ "这台设备免密重新登录的凭证"，效果相当于在 Steam 账号的**设备列表**里新增一台设备
- 它在该会话内具备此账号的网络通信能力。MuSync 的代码**只实现了两个动作**：
  登录保持在线、发送"正在玩 XX"状态文本（代码全部开源可审计：
  `SteamSessionManager.cs` / `SteamStatusManager.cs`）
- 任何持有令牌的软件理论上都能做更多 —— 所以我们给所有人的建议：
  **只对开源、可审查的软件授权登录**；并且随时可以在 Steam 设备列表里看到并移除 "MuSync"

## 三、程序读了什么（本机数据，仅此五类）

| 读取对象 | 具体内容 | 为什么需要 |
|---|---|---|
| 网易云 / QQ 音乐 / 酷狗音乐进程内存 | 歌名、歌手、播放进度等 | 这些播放器没有开放接口，只能读内存获取播放状态。**只读，不写入、不注入、不修改** |
| LX Music 本地接口 | 播放状态（本机 HTTP） | 播放器官方提供的开放 API |
| Windows 系统媒体控件（SMTC） | 曲目、歌手、专辑、播放状态与进度 | 千千静听（社区重制版）主动把播放信息发布到这里，读系统接口即可，**不读它的进程内存** |
| 当前前台窗口 | 进程名、窗口标题、是否全屏 | 判断"你正在使用哪个程序"，仅此三项 |
| 自身运行统计 | 内存占用等 | 设置里的性能面板，纯本地展示 |

**明确不做**：不读键盘输入、不截屏、不注入、不读浏览器/聊天记录、不扫描硬盘文件、
不读取上述之外的其他程序数据。

## 四、发了什么（网络流量：三类主动 + 一类被动）

**没有作者服务器，没有任何统计、上报、遥测。**

1. **Steam 官方服务器**：登录、保持在线、推送"正在玩"状态文本 —— 除此之外不发任何东西
2. **音乐封面 CDN**：从网易云 / QQ 音乐提供的图片地址下载封面用于界面显示（只下载图片，不发送个人信息）
3. **GitHub Releases**：只在"检查更新"时查询最新版本并下载更新包（见第九节）

**自定义数据源**（如心率）：按你填写的地址请求，目标由你指定，作者不参与。

**被动的一类**：若开启「浏览器扩展增强」，MuSync 会**在本机**监听 127.0.0.1 的一个端口，接收浏览器扩展发来的播放状态 —— 不对外网开放，也不会因这条链路主动外连（见第十节）。

## 五、账号会不会受影响（包括风险坦白）

- **顶号问题**：不会。Steam 允许多设备会话共存（PC 客户端 + 手机 App + 本工具），互不干扰；
  本工具也不需要 Steam 客户端保持在线
- **封号风险（必须坦白）**：这种"第三方客户端保持在线"的方式与 ArchiSteamFarm 等工具
  机制相同、已被广泛使用十余年，目前没有公开的同类工具封号案例；但 Valve 未官方背书
  第三方登录，**无法给出 100% 零风险保证**，请知晓后自行权衡
- **好友能看到什么**：只有你设置的"正在玩"状态文本（歌名/程序名），看不到任何其他信息
- **杀毒软件提示**：读取其他进程内存的行为可能触发个别杀软误报，属正常现象，代码开源可查；
  也可以关闭音乐同步只使用"程序同步"（不读取任何播放器内存）

## 六、你可以自己验证（三分钟）

1. **看网络**：打开"资源监视器"（任务管理器 → 性能 → 打开资源监视器）→ 网络 →
   观察 MuSync.exe 的连接，只会出现 Steam 服务器与音乐平台图片 CDN
2. **看日志**：`%LocalAppData%\MuSync\logs\` 里有逐条操作日志（连接/登录/状态推送）
3. **看源码**：https://github.com/ZhaoBuyan/MuSync —— 登录、推送、加密逻辑全部开源

## 七、如何撤销授权（随时、彻底）

1. **程序内**：设置 → 退出登录（立即清空本机令牌）
2. **Steam 侧（最彻底）**：Steam 网页 → 账号明细 → 已授权设备 → 移除名为 **MuSync** 的设备
   （移除后本地令牌立刻失效）
3. **完全清除**：卸载程序 + 删除 `%LocalAppData%\MuSync` 目录，一切数据（含加密令牌与日志）清空

## 八、你的数据在哪里

- **只在本机**：`%LocalAppData%\MuSync\`（config.json 为加密存储、logs 为运行日志）
- 不上云、不做任何备份同步；删除该目录 = 抹掉一切痕迹

## 九、更新包是怎么验的（v0.4.11 起）

自动更新是**唯一会下载并运行可执行文件**的功能，所以这条链路的校验单独交代：

| 环节 | 做法 |
|---|---|
| 来源 | 只从 GitHub Releases（HTTPS）取，地址由 GitHub API 返回 |
| 完整性 | 校验 GitHub API 给出的 `sha256:` 摘要；不一致**直接丢弃**并报错 |
| 来源真实性 | 校验 Authenticode 数字签名：**签名无效 = 硬拒绝**（文件被动过 / 证书不受信任） |
| 未签名怎么办 | 目前的发行包**尚未做代码签名**，因此出现"无签名"时只提示并**要求你明确确认**才继续 |
| 落地位置 | `%LocalAppData%\MuSync\updates\`（**你的用户目录**，不是共享的 Temp），先写 `.part`，校验通过才改名 |
| 安装 | 静默运行安装器，全程有 UAC 提示；安装完成后自动重启 |

**坦白讲**：SHA-256 摘要和更新包走同一条 TLS 连接，所以"摘要对得上"只能证明**传输没坏**，
不能证明来源。真正防得住伪造的是数字签名 —— 而本项目目前没有代码签名证书，
所以最终仍依赖 GitHub 的 HTTPS 与你自己的判断。发布页同时提供 `SHA256SUMS.txt`，
你可以手工核对（见「六、你可以自己验证」）。

## 十、浏览器扩展与本机端口（v0.4.13 起，默认关闭）

为了救"网页根本不把播放状态交给 Windows"的站点，MuSync 可以开一个**只监听本机**的端口，
由浏览器扩展把播放状态发进来：

- 地址：`http://127.0.0.1:47564/media`（**只绑 127.0.0.1**，路由器/局域网/外网都访问不到）
- MuSync **不主动外连**；扩展也只往本机发，不向任何服务器上报
- 上报内容仅：标题 / 作者 / 专辑 / 播放位置 / 总时长 / 是否在播放 / 站点域名
- 默认**关闭**；不勾选时端口根本不打开

**必须坦白的一点**：这个端口**没有身份验证**。也就是说，**你机器上任何程序**（包括恶意软件）
都可以向它 POST 一个标题，从而让你 Steam 上的"正在玩"显示成任意文本。这是"显示类数据"的
可接受取舍 —— 它改不了你的账号、看不到你的密码、也不触发任何下载或执行。
如果你在意这一点：**不勾选「浏览器扩展增强」，端口就不会打开**。

**可以加锁**（v0.4.14 起）：设置里有个「共享令牌」，填了之后扩展必须在 `X-MuSync-Token` 头里带上同一个值，
否则一律 401 拒绝。扩展设置页（扩展详情 → 扩展选项）里有对应的「共享令牌」输入框，两端填一致即可。
令牌按常量时间比较，不泄露前缀长度；留空 = 不校验（默认，向后兼容）。

## 十一、点链接时只按 http(s) 打开

界面里有些链接来自**不完全可信的来源**（例如歌曲页地址是从播放器进程内存里读出来的字符串）。
从 v0.4.14 起，所有"用默认程序打开"的地址都会先做校验：**只有 http/https 绝对地址才会交给系统**，
其余（本地路径、UNC 共享、`file:`、自定义协议）一律拒绝并记一条日志。

原因很直接：Windows 的 Shell 会按协议分派 —— 把 `C:\...\x.exe` 或 `\\host\share\x.exe`
交给它，结果不是"打开网页"而是"执行程序"。

有任何想确认的细节，欢迎在 GitHub 仓库提 Issue，作者会公开回答。

---

# Security & Privacy

[中文](#安全与隐私说明--security--privacy) ｜ **English**

> This document is for anyone about to hand their Steam account to MuSync. It covers what MuSync
> does with your account, what it reads, what it sends, **what it never does**, how to verify all of
> it, and how to revoke access. No dodging the risks.

## 1. Signing in: what happens to your username and password

1. You type your account name and password into the sign-in box (Steam mobile authenticator / email
   code are supported, same flow as signing in through a browser).
2. MuSync signs in by talking **directly to Valve's servers** over the **official Steam networking
   protocol** (the open-source SteamKit2 library). Your password never passes through a third party —
   **this project has no author-run server and no intermediary service of any kind.**
3. Once Valve's servers accept the sign-in, they issue a **refresh token**.
4. The password's job is done: **it exists in memory only during sign-in and is never written to disk
   or cached.**
5. The token is stored on this machine in `%LocalAppData%\MuSync\config.json`, encrypted with **Windows
   DPAPI** (the key is tied to your Windows account, so a copy of the file on another PC or user
   account cannot be decrypted — it is useless there).
6. If you leave **"Remember me" unchecked**, the token is not saved at all and you sign in again on
   every launch.

## 2. What the token is and what it can do (plainly)

- A refresh token is roughly "this device's credential for signing in again without a password"; the
  effect is that a new device shows up in your Steam account's **authorized devices** list.
- Within that session it carries this account's network capabilities. MuSync's code **implements
  exactly two actions**: staying signed in, and setting the "playing X" status text (all of it is
  open source and auditable: `SteamSessionManager.cs` / `SteamStatusManager.cs`).
- In theory any software holding the token could do more — which is why our advice to everyone is:
  **only grant sign-in to open-source, auditable software**, and remove "MuSync" from your Steam
  authorized devices whenever you like.

## 3. What MuSync reads (local data — these five categories, nothing else)

| What | Details | Why it is needed |
|---|---|---|
| Process memory of NetEase Cloud Music / QQ Music / KuGou Music | Track title, artist, playback position, etc. | These players expose no API, so playback state has to be read from memory. **Read-only: no writes, no injection, no modification.** |
| LX Music local API | Playback state (local HTTP) | The open API the player itself provides. |
| Windows system media transport controls (SMTC) | Track, artist, album, playback state and position | TTPlayer (community rebuild) publishes its playback info there, so reading the system interface is enough — **its process memory is never read.** |
| Current foreground window | Process name, window title, fullscreen or not | To tell which app you are using — those three fields only. |
| Its own runtime statistics | Memory usage, etc. | The performance panel in Settings; displayed locally only. |

**Explicitly not done**: no keystroke capture, no screenshots, no injection, no reading browser or
chat history, no scanning files on disk, and no reading other programs' data beyond the above.

## 4. What it sends (network traffic: three active paths and one passive)

**No author-run server. No statistics, no reporting, no telemetry of any kind.**

1. **Valve's servers**: signing in, staying online, pushing the "playing X" status text — nothing else.
2. **Album art CDNs**: album covers are downloaded from image URLs provided by NetEase Cloud Music /
   QQ Music for display (images only; no personal information is sent).
3. **GitHub Releases**: only when you run "Check for updates" — to look up the latest version and
   download the update package (see section 9).

**Custom data sources** (e.g. heart rate): MuSync requests the address you entered. You choose the
destination; the author is not involved.

**The passive path**: if you enable "browser extension enhancement", MuSync listens on a port on
**127.0.0.1 only** and receives playback state from the browser extension — it is not reachable from
outside the machine, and this path never initiates an outbound connection (see section 10).

## 5. Will your account be affected (risks included)

- **Session conflicts**: no. Steam allows multiple device sessions side by side (PC client + phone app
  + this tool) without interfering with each other, and this tool does not require the Steam client to
  be running.
- **Ban risk (stated plainly)**: "a third-party client staying online" is the same mechanism used by
  tools such as ArchiSteamFarm, which have been widely used for over a decade, and there is no public
  case of a ban from a comparable tool. But Valve does not officially endorse third-party sign-in, so
  **no 100% risk-free guarantee can be given** — please weigh that yourself.
- **What friends can see**: only the "playing X" status text you configure (track or app name).
  Nothing else.
- **Antivirus warnings**: reading another process's memory can trigger false positives in some
  antivirus products. That is expected, and the code is open for review. You can also turn music sync
  off and use only "app sync", which reads no player memory at all.

## 6. Verify it yourself (three minutes)

1. **Watch the network**: open Resource Monitor (Task Manager → Performance → Open Resource Monitor)
   → Network, and watch MuSync.exe's connections. You will only see Steam servers and the music
   platform image CDNs.
2. **Read the logs**: `%LocalAppData%\MuSync\logs\` contains a line-by-line log (connections,
   sign-in, status pushes).
3. **Read the source**: https://github.com/ZhaoBuyan/MuSync — sign-in, status pushing and encryption
   are all open source.

## 7. How to revoke access (anytime, completely)

1. **In the app**: Settings → Sign out (clears the local token immediately).
2. **On Steam's side (most thorough)**: Steam website → Account details → Authorized devices → remove
   the device named **MuSync** (the local token stops working right after).
3. **Full cleanup**: uninstall the program and delete `%LocalAppData%\MuSync`; every piece of data
   (encrypted token and logs included) is gone.

## 8. Where your data lives

- **On this machine only**: `%LocalAppData%\MuSync\` (`config.json` is encrypted storage, `logs` holds
  runtime logs).
- No cloud, no backup sync. Deleting that folder erases every trace.

## 9. How update packages are verified (since v0.4.11)

Auto-update is the **only** feature that downloads and runs an executable, so its verification chain is
documented separately:

| Step | What happens |
|---|---|
| Source | GitHub Releases over HTTPS only; the URL comes from the GitHub API. |
| Integrity | The `sha256:` digest returned by the GitHub API is checked; a mismatch is **discarded** with an error. |
| Authenticity | The Authenticode signature is checked: **an invalid signature is a hard rejection** (the file was tampered with, or the certificate is untrusted). |
| What if unsigned | Release packages are **not code-signed yet**, so an unsigned package only warns and **requires your explicit confirmation** before continuing. |
| Download location | `%LocalAppData%\MuSync\updates\` (**your user profile**, not the shared Temp), written as `.part` first and renamed only after verification passes. |
| Install | The installer runs silently with the usual UAC prompt; the app restarts when it finishes. |

**Frankly**: the SHA-256 digest travels over the same TLS connection as the package, so a matching
digest only proves **the transfer was intact**, not where the file came from. What actually resists
forgery is the digital signature — and this project has no code-signing certificate yet, so the final
protection is GitHub's HTTPS plus your own judgement. Each release page also publishes
`SHA256SUMS.txt` so you can verify by hand (see section 6).

## 10. The browser extension and the local port (since v0.4.13, off by default)

For sites that never hand playback state to Windows at all, MuSync can open a port that **listens on
this machine only**, so the browser extension can push playback state in:

- Address: `http://127.0.0.1:47564/media` (**bound to 127.0.0.1 only** — unreachable from the router,
  the LAN or the internet).
- MuSync **never initiates outbound connections** on this path, and the extension only sends to this
  machine; nothing is reported to any server.
- The payload is only: title / artists / album / position / duration / playing state / site domain.
- **Off by default**; if you do not tick the option, the port is never opened.

**One thing that must be said plainly**: this port has **no authentication**. That means **any program
on your machine** (malware included) can POST a title to it and make your Steam status show arbitrary
text. That is an accepted trade-off for display-only data — it cannot touch your account, cannot see
your password, and cannot trigger any download or execution. If that bothers you: **leave "browser
extension enhancement" unticked and the port is never opened.**

**It can be locked** (since v0.4.14): Settings has a "shared token" field. Once set, the extension must
send the same value in the `X-MuSync-Token` header, otherwise every request gets a 401. The extension's
options page has a matching "shared token" field — fill in the same value on both sides. Tokens are
compared in constant time, so no prefix length leaks; leaving it empty means no check (the default,
backwards compatible).

## 11. Links only open over http(s)

Some links in the interface come from **not fully trusted sources** (a song page URL, for example, is a
string read out of a player's process memory). Since v0.4.14 every "open with the default program"
address is validated first: **only absolute http/https URLs are handed to the system**; anything else
(local paths, UNC shares, `file:`, custom protocols) is refused and logged.

The reason is simple: the Windows Shell dispatches by protocol. Hand it `C:\...\x.exe` or
`\\host\share\x.exe` and the result is not "open a web page" but "run a program".

If you want to confirm any detail, open an issue in the GitHub repository — the author answers in
public.
