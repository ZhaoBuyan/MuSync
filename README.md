# MuSync

> 把「**音乐软件 + 任意程序**正在干什么」同步到你的 Steam 状态。
> （名字源自最初的目标 **Mu**sic **Sync**，现已不止音乐 😉）

好友视角实际效果：

![Steam 好友列表效果](docs/steam-preview.png)

显示形态完全由你定义（模板引擎 + 积木编辑器），例如：

```
非 Steam 游戏中
正在听：稻香 - 周杰伦 [#####-----] 2:30/4:15      ← 听歌时
正在听：稻香 - 周杰伦 [❤️❤️❤️❤️❤️❤️💕💕💕💕] 2:30/4:15   ← 进度条样式 / 格数随你定，emoji 直接粘贴
VS Code ‖ 正在听：稻香 - 周杰伦                    ← 一边写代码一边听歌（组合模板）
卡拉彼丘                                           ← 在玩非 Steam 启动的游戏
```

## ✨ 特性

### 🎮 程序同步
- **任意程序 → Steam**：游戏（卡拉彼丘等）、工作软件（VS Code）、任何你想展示的东西
- **智能分类**：内置约 50 个常见程序字典 + 全屏检测，自动判别游戏 / 工作 / 媒体 / 社交等（可手动改）
- **自动发现**：新程序首次出现即自动进入列表待确认；也可「添加当前前台程序」一键录入
- **双模式**：`前台时显示`（切走就撤）/ `运行即显示`（挂机也能持续显示）
- **忽略名单**：系统进程、音乐播放器自动排除；「忽略」类程序在前台时不影响当前状态

### 🎵 多播放器音乐同步
- 支持 网易云音乐 / QQ 音乐 / LX Music（洛雪）
- 多播放器自动仲裁：**正在播放的优先**；优先级顺序可在设置中调整，即时生效
- 音乐 / 程序两套**独立开关**；「暂停时自动隐藏」→ 好友看不到你在挂机

### 🎨 显示自定义（拉满）
- **积木编辑器**：像搭积木一样拼状态文本——歌名 / 歌手 / 进度条 / 程序名 / 分隔符 / 自定义文字，
  无限块自由排序，实时预览
- **模板预设**：一键切换「简洁 / 带前缀（正在玩·正在听）/ 只要名字」
- **进度条样式**：`█░` `▰▱` `●○` `#-` …… 预设 + 任意字符，**格数 1~50 可调**；
  emoji 直接粘贴就能用：`[❤️❤️❤️❤️❤️❤️💕💕💕💕] 1:59/3:32`
- 组合显示、分隔符均可自定义；文本上限 128 字节智能截断

### 📡 Steam 同步（稳字当头）
- SteamKit2 直连，无需 Steam 客户端在线；断线自动重连（指数退避）+ 令牌自动重登
- **连接自愈**：TCP 失败自动切换 WebSocket (443)；服务器列表本地持久化（官方接口不可达也能连）；
  服务器「劝退」时自动换节点重试（最多 4 轮）
- **真实游戏保护**：检测到你在玩真实 Steam 游戏时自动暂停同步，退出即恢复
- 令牌 DPAPI 加密落盘；网络波动不会误清登录态
- **同步频率档位**：快速 0.25s / 标准 0.5s（推荐）/ 省流 1s

### 🪟 桌面体验
- 首页双面板：**音乐**（动态跟随当前播放器）+ **程序**，实时显示同步状态
- 托盘常驻：悬停显示当前状态、右键「暂停同步」一键隐身、开机自启
- 双击歌曲名打开歌曲页；关键日志本地可查（`%LocalAppData%\MuSync\logs\`）
- **单窗设置**：常规 / 同步 / 显示 / 程序 / 诊断 / 关于 六页平铺，一窗到底不再套娃
- **检查更新**：启动静默检查，新版亮红点（主窗口「设置」按钮 / 关于页 / 托盘菜单）；
  更新窗口可直接下载，完成后「退出并打开文件夹」，拖拽替换即可
- **诊断页**：「打开日志文件夹」一键直达——遇到问题时把最新日志发给开发者即可

## 📥 使用

1. 从 [Releases](https://github.com/ZhaoBuyan/MuSync/releases) 下载 **MuSync.exe**
   （免安装单文件，**无需安装任何运行时**，双击即用）
2. 首次启动登录 Steam（支持手机令牌 / 邮箱验证码）。若遇到「异常登录」提示，
   按登录窗里的指引操作：**手机 Steam App → 选择「Steam 客户端」→ 确认实际所在地**
3. 打开音乐播放器即可自动同步

> **洛雪音乐用户请注意**：请在 洛雪音乐 → 设置 → 开放API 中「启用开放API服务」，
> 并允许来自局域网的访问。
4. 想让好友看到你在用什么程序：设置 → 同步 → 打开「同步非游戏应用」
5. 升级：程序会自动检查更新（新版时「设置」按钮亮红点）；更新窗口可直接下载，
   之后「退出并打开文件夹」拖拽替换即可

> 与 Steam 客户端同账号共存不会被顶下线（与 ArchiSteamFarm 同为 SteamKit 会话）；
> Steam 账号管理里会出现名为 `MuSync` 的设备会话，属正常现象。

> **版本怎么选**：普通用户推荐 **MuSync.exe**（免安装单文件，内置运行时，双击即用）；
> **MuSync-lite.exe** 体积更小，但需已安装 [.NET 9 桌面运行时](https://dotnet.microsoft.com/download/dotnet/9.0)。

## ❓ 常见问题

- **登录反复失败 / 提示“服务器繁忙”（TryAnotherCM）**
  Steam 对“新设备 + 网络环境变化”的风控行为，通常几分钟后自行恢复（新版会自动换节点重试，最多 4 轮）。
  若频繁遇到：关闭 **Steam++（Watt Toolkit）** 等 Steam 网络工具后再试；加速器固定同一节点。
- **好友列表里看不到我，但主页显示是好友**
  Steam 客户端好友列表的缓存问题：右键好友列表刷新，或重启 Steam 客户端即可；与 MuSync 无关。
- **洛雪音乐不识别**
  请在 洛雪音乐 → 设置 → 开放API 中「启用开放API服务」，并允许来自局域网的访问。
- **升级后设置和登录会丢吗**
  不会。配置与登录令牌存放在 `%LocalAppData%\MuSync\config.json`，与程序文件分离，
  替换 exe 后自动沿用（令牌 DPAPI 加密，同一台电脑同一用户无需重新登录）。
- **配置或登录态异常**
  配置读取失败时程序会自动备份（`config.json.bak`）并尽量保留其余设置；日志位于
  `%LocalAppData%\MuSync\logs`（设置 → 诊断 → 「打开日志文件夹」一键直达）。

## 🔨 构建与测试

```powershell
dotnet build MuSync.sln -c Release     # 编译
dotnet test  MuSync.sln                # 单元测试（75 个）

# 免安装单文件发布（内置 .NET 运行时）
dotnet publish MuSync.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

CI（`main` push / `v*` tag）自动构建双产物：
`MuSync.exe`（免安装）+ `MuSync-lite.exe`（轻量版，需 .NET 9 运行时）。

## 🔀 与上游项目的关系

MuSync 的播放器逆向实现立于一条开源谱系之上（均为 MIT 协议）：

```
MuSync（本项目）
 └─ wuyan1337/yySync ............... 直接基础（Steam 同步版）
     └─ kriYamiHikari/Music-DiscordRPC
         └─ Kxnrl/NetEase-Cloud-Music-DiscordRPC
             └─ Copyright (c) 2018 Kyle 的原始项目
```

仅播放器内存逆向实现（网易云特征码 / QQ 音乐偏移 / LX 接口）传承自该谱系；
连接层、状态仲裁、显示引擎、程序同步、配置与安全体系均为本项目独立实现。
相对 yySync 的全部改动见 [CHANGELOG.md](CHANGELOG.md)。

## 🗂️ 项目结构

```
MuSync.csproj                  # 主程序（WinForms）
Program.cs                     # 入口 / 托盘 / 更新检查
MainForm / SettingsForm        # 主界面 / 设置窗口（单窗多页）
FormatBlockEditorForm          # 积木编辑器
UpdateForm                     # 更新窗口（下载 / 跳转发布页）
SteamLoginForm                 # Steam 登录窗口
Players/                       # 播放器读取（网易云 / QQ / LX Music）
Utils/                         # 前台检测 / 分类器 / 模板编解码 / 更新 / 日志 / DPAPI 等
Models/                        # 数据模型
Win32Api/                      # Win32 / 进程内存访问
tests/MuSync.Tests/            # xUnit 单元测试（75 个）
reference-yySync/              # 上游参考源码（MIT，不参与编译，仅对照）
```

## 🔐 安全与隐私

把账号授权给 MuSync 前请先阅读 [SECURITY.md](SECURITY.md)：
登录流程、读取范围、网络流量、风险坦白与撤销方法，全部明档交代。

## 🙏 致谢与许可

MuSync 立于一条开源谱系之上（见上文「与上游项目的关系」），感谢每一位铺路者：

- [wuyan1337/yySync](https://github.com/wuyan1337/yySync) —— 直接基础
- [kriYamiHikari/Music-DiscordRPC](https://github.com/kriYamiHikari/Music-DiscordRPC)
- [Kxnrl/NetEase-Cloud-Music-DiscordRPC](https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC)
- [SteamRE/SteamKit](https://github.com/SteamRE/SteamKit) —— Steam 网络协议支持

本项目以 MIT 协议发布，见 [LICENSE](LICENSE)。
第三方许可全文见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
