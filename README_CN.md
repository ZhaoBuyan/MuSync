# MuSync

**中文** ｜ [English](README_EN.md)

作者：[ZhaoBuyan](https://github.com/ZhaoBuyan)

MuSync 是一个 Windows 桌面工具：把你正在使用的音乐播放器和任意程序的运行状态，同步到 Steam 的个人状态中。好友在你的 Steam 好友列表里就能看到你正在听什么、用什么程序。

好友视角的实际效果：

![Steam 好友列表效果](docs/steam-preview.png)

显示的文本完全由你定义（模板引擎 + 积木编辑器）。例如：

```
非 Steam 游戏中
正在听：稻香 - 周杰伦 [#####-----] 2:30/4:15      ← 听歌时
正在听：稻香 - 周杰伦 [❤️❤️❤️❤️❤️❤️💕💕💕💕] 2:30/4:15   ← 进度条样式 / 格数随你定，emoji 直接粘贴
VS Code ‖ 正在听：稻香 - 周杰伦                    ← 一边写代码一边听歌（组合模板）
卡拉彼丘                                           ← 在玩非 Steam 启动的游戏
```

## 功能

### 程序同步

- 支持把任意程序的运行状态同步到 Steam：游戏、工作软件（如 VS Code）、其他应用
- 内置约 50 个常见程序的识别字典，并支持全屏检测，自动判断游戏 / 工作 / 媒体 / 社交等分类（可手动调整）
- 新出现的程序会自动进入列表等待确认；也可以从设置中手动添加当前前台程序
- 两种显示方式：`前台时显示`（切走即隐藏）/ `运行即显示`（挂机也持续显示）
- 系统进程与音乐播放器默认排除；分类为「忽略」的程序不会影响当前状态

### 音乐同步

- 支持网易云音乐 / QQ 音乐 / LX Music（洛雪）/ 酷狗音乐
- 多播放器同时运行时自动仲裁：正在播放的优先；播放器优先级可在设置中调整，立即生效
- 音乐与程序同步有各自独立的开关；开启「暂停时隐藏」后，挂着暂停的播放器不会出现在好友列表里

### 显示自定义

- 积木编辑器：以积木块的方式编辑状态文本——歌名 / 歌手 / 进度条 / 程序名 / 分隔符 / 自定义文字，可自由排序、实时预览
- 模板预设：一键切换「简洁 / 带前缀（正在玩·正在听）/ 只要名字」
- 进度条样式：预设样式 + 任意字符，格数 1~50 可调；emoji 直接粘贴即可使用，例如 `[❤️❤️❤️❤️❤️❤️💕💕💕💕] 1:59/3:32`
- 界面外观：标题 / 歌名颜色、字体、背景色、背景图（拉伸 / 适应 / 平铺 / 居中）均可自定义，一键恢复默认
- 组合显示与分隔符均可自定义；文本超过 Steam 上限时按 128 字节智能截断

### Steam 连接

- 基于 SteamKit2 直接连接 Steam 网络，无需 Steam 客户端在线
- 断线自动重连（指数退避），恢复后自动用保存的令牌重新登录
- TCP 连接失败时自动切换 WebSocket（443 端口）；服务器列表本地持久化，官方接口不可达时也能连接；服务器「劝退」时自动更换节点重试（最多 4 轮）
- 检测到你在玩真正的 Steam 游戏时自动暂停同步，退出游戏后恢复
- 登录令牌使用 Windows DPAPI 加密存储；网络波动不会导致登录态被清除
- 同步频率可选：快速 0.25s / 标准 0.5s（推荐）/ 省流 1s

### 桌面与设置

- 主界面两个面板：「音乐」（跟随当前播放器）与「程序」，实时显示同步状态；双击歌曲标题可在浏览器中打开歌曲页面
- 托盘常驻：悬停显示当前状态，右键可「暂停同步」临时隐身，支持开机自启
- 设置窗口为单窗多页：常规 / 同步 / 显示 / 程序 / 诊断 / 关于
- 检查更新：启动后自动检查 + 每 6 小时复查；有新版本时「设置」按钮与托盘菜单出现提示；更新窗口内可直接下载——完整版 / lite 版「退出并打开文件夹」手动替换，安装器版「立即更新」一键自动完成
- 诊断页提供「打开日志文件夹」；日志保存在 `%LocalAppData%\MuSync\logs\`

## 使用

1. 从 [Releases](https://github.com/ZhaoBuyan/MuSync/releases) 下载 **MuSync.exe**（完整版，推荐——免安装单文件，双击即用）；也可以选择 **MuSync-Setup.exe**（安装器版，装好后新版本可一键自动更新）
2. 首次启动登录 Steam（支持手机令牌 / 邮箱验证码）。若遇到「异常登录」提示，按登录窗口中的指引操作：手机 Steam App → 选择「Steam 客户端」→ 确认实际所在地
3. 打开音乐播放器即可自动同步
4. 想让好友看到你在用什么程序：设置 → 同步 → 打开「同步非游戏应用」
5. 升级：程序会自动检查更新——完整版 / lite 版下载后点「退出并打开文件夹」拖拽替换；安装器版点「立即更新」自动完成

> **洛雪音乐用户注意**：请在 洛雪音乐 → 设置 → 开放API 中「启用开放API服务」，并允许来自局域网的访问。

> **与 Steam 客户端共存**：MuSync 与 Steam 客户端同账号登录不会互相顶下线（同属 SteamKit 会话）。Steam 账号管理中会出现名为 MuSync 的设备会话，属正常现象。

> **版本选择**：推荐 **MuSync.exe**（完整版）——免安装单文件，双击即用；**MuSync-Setup.exe**（安装器版）适合想省事的用户——装好后新版本可一键自动更新，无需手动替换文件；**MuSync-lite.exe** 体积更小，但需要已安装 [.NET 9 桌面运行时](https://dotnet.microsoft.com/download/dotnet/9.0)。三种版本功能一致，配置与登录通用。

## 常见问题

- **登录反复失败 / 提示"服务器繁忙"（TryAnotherCM）**
  Steam 对"新设备 + 网络环境变化"的风控行为，通常几分钟后自行恢复（程序会自动更换节点重试，最多 4 轮）。若频繁遇到：关闭 Steam++（Watt Toolkit）等 Steam 网络工具后再试；使用加速器时尽量固定同一节点。

- **好友有可能看不到你**
  极少数情况下，Steam 客户端的好友列表会“卡掉”个别好友的条目，导致对方看不到你（主页查看仍是好友）。这大概率是 Steam 客户端自身的原因，右键刷新或重启 Steam 客户端即可恢复。

- **洛雪音乐不识别**
  请在 洛雪音乐 → 设置 → 开放API 中「启用开放API服务」，并允许来自局域网的访问。

- **升级后设置和登录会丢吗**
  不会。配置与登录令牌存放在 `%LocalAppData%\MuSync\config.json`，与程序文件分离，替换程序后自动沿用（令牌经 DPAPI 加密，同一台电脑同一用户无需重新登录）。

- **配置或登录态异常**
  配置读取失败时程序会自动备份（`config.json.bak`）并尽量保留其余设置。日志位于 `%LocalAppData%\MuSync\logs`（设置 → 诊断 → 「打开日志文件夹」一键直达）。

## 构建与测试

```powershell
dotnet build MuSync.sln -c Release     # 编译
dotnet test  MuSync.sln                # 单元测试（87 个）

# 免安装单文件发布（内置 .NET 运行时）
dotnet publish MuSync.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# 安装器（需已安装 Inno Setup 6.5+）
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

CI（`main` push / `v*` tag）自动构建三个产物：`MuSync.exe`（免安装）、`MuSync-lite.exe`（轻量版，需 .NET 9 运行时）、`MuSync-Setup.exe`（安装器版）。

## 与上游项目的关系

MuSync 的播放器内存读取实现传承自一条开源谱系（均为 MIT 协议）：

```
MuSync（本项目）
 └─ wuyan1337/yySync ............... 直接基础（Steam 同步版）
     └─ kriYamiHikari/Music-DiscordRPC
         └─ Kxnrl/NetEase-Cloud-Music-DiscordRPC
             └─ Copyright (c) 2018 Kyle 的原始项目
```

仅播放器内存逆向实现（网易云特征码 / QQ 音乐偏移 / LX 接口）来自该谱系；连接层、状态仲裁、显示引擎、程序同步、配置与安全体系均为本项目独立实现。相对 yySync 的全部改动见 [CHANGELOG.md](CHANGELOG.md)。

## 项目结构

```
MuSync.csproj                  # 主程序（WinForms）
Program.cs                     # 入口 / 托盘 / 更新检查
MainForm / SettingsForm        # 主界面 / 设置窗口（单窗多页）
FormatBlockEditorForm          # 积木编辑器
UpdateForm                     # 更新窗口（下载 / 跳转发布页）
SteamLoginForm                 # Steam 登录窗口
Players/                       # 播放器读取（网易云 / QQ / LX Music / 酷狗）
Utils/                         # 前台检测 / 分类器 / 模板编解码 / 更新 / 日志 / DPAPI 等
Models/                        # 数据模型
Win32Api/                      # Win32 / 进程内存访问
installer/                     # Inno Setup 安装器脚本与本地构建
tests/MuSync.Tests/            # xUnit 单元测试（87 个）
reference-yySync/              # 上游参考源码（MIT，不参与编译，仅对照）
```

## 安全与隐私

把 Steam 账号授权给 MuSync 之前，建议先阅读 [SECURITY.md](SECURITY.md)：登录流程、读取范围、网络流量、风险说明与撤销方法，全部如实交代。

## 许可与致谢

MuSync 基于一条开源谱系构建（见上文「与上游项目的关系」），感谢每一位铺路者：

- [wuyan1337/yySync](https://github.com/wuyan1337/yySync) —— 直接基础
- [kriYamiHikari/Music-DiscordRPC](https://github.com/kriYamiHikari/Music-DiscordRPC)
- [Kxnrl/NetEase-Cloud-Music-DiscordRPC](https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC)
- [SteamRE/SteamKit](https://github.com/SteamRE/SteamKit) —— Steam 网络协议支持

本项目以 MIT 协议发布，见 [LICENSE](LICENSE)。第三方许可全文见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
