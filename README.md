# MuSync

> 把「**音乐软件 + 任意程序**正在干什么」同步到你的 Steam 状态。
> （名字源自最初的目标 **Mu**sic **Sync**，现已不止音乐 😉）

好友视角看到的例子：

```
非 Steam 游戏中
正在听：稻香 - 周杰伦 [#####-----] 2:30/4:15      ← 听歌时
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
- **进度条样式**：`█░` `▰▱` `●○` `#-` …… 预设 + 任意字符（emoji 也行）
- 组合显示、分隔符均可自定义；文本上限 128 字节智能截断

### 📡 Steam 同步（稳字当头）
- SteamKit2 直连，无需 Steam 客户端在线；断线自动重连（指数退避）+ 令牌自动重登
- **连接自愈**：TCP 失败自动切换 WebSocket (443)；服务器列表本地持久化（官方接口不可达也能连）；
  服务器「劝退」时自动换节点重试（最多 4 轮）
- **真实游戏保护**：检测到你在玩真实 Steam 游戏时自动暂停同步，退出即恢复
- 令牌 DPAPI 加密落盘；网络波动不会误清登录态

### 🪟 桌面体验
- 首页双面板：**音乐**（动态跟随当前播放器）+ **程序**，实时显示同步状态
- 托盘常驻：悬停显示当前状态、右键「暂停同步」一键隐身、开机自启
- 双击歌曲名打开歌曲页；关键日志本地可查（`%LocalAppData%\MuSync\logs\`）

## 📥 使用

1. 从 [Releases](https://github.com/ZhaoBuyan/MuSync/releases) 下载 **MuSync.exe**
   （免安装单文件，**无需安装任何运行时**，双击即用）
2. 首次启动登录 Steam（支持手机令牌 / 邮箱验证码）。若遇到「异常登录」提示，
   按登录窗里的指引操作：**手机 Steam App → 选择「Steam 客户端」→ 确认实际所在地**
3. 打开音乐播放器即可自动同步；LX Music 需在设置中启用「开放 API」
4. 想让好友看到你在用什么程序：设置 → 程序同步设置 → 打开「同步非游戏应用」

> 与 Steam 客户端同账号共存不会被顶下线（与 ArchiSteamFarm 同为 SteamKit 会话）；
> Steam 账号管理里会出现名为 `MuSync` 的设备会话，属正常现象。

## 🔨 构建与测试

```powershell
dotnet build MuSync.sln -c Release     # 编译
dotnet test  MuSync.sln                # 单元测试（35 个）

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
MainForm / SettingsForm        # 主界面 / 设置窗口
AppSyncSettingsForm            # 程序同步设置
FormatBlockEditorForm          # 积木编辑器
SteamLoginForm                 # Steam 登录窗口
Players/                       # 播放器读取（网易云 / QQ / LX Music）
Utils/                         # 前台检测 / 分类器 / 模板编解码 / 日志 / DPAPI 等
Models/                        # 数据模型
Win32Api/                      # Win32 / 进程内存访问
tests/MuSync.Tests/            # xUnit 单元测试（35 个）
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
