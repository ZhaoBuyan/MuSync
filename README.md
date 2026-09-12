# MuSync

> 把「音乐软件正在播什么」同步到你的 Steam 状态 —— **Mu**sic **Sync**（音乐软件状态同步 Steam）。

登录 Steam 后，好友看到你的状态是「正在玩」，内容是当前播放的歌曲、歌手与实时进度：
`稻香 - 周杰伦 [#####-----] 2:30/4:15`

## ✨ 特性

- 🎵 **多播放器**：网易云音乐 / QQ 音乐 / LX Music（洛雪）
  - 多个播放器同时运行时自动仲裁：**正在播放的优先**，关闭其一不影响另一个的状态
- 📡 **Steam 同步**：SteamKit2 直连，无需 Steam 客户端在线
  - 断线自动重连 + 令牌自动重新登录（指数退避）
  - 检测到你在玩真实 Steam 游戏时自动暂停音乐同步，退出游戏自动恢复
- 🎨 **可自定义**：歌手名 / 进度条 / 前缀文案 / 超长时优先保留哪部分，设置内实时预览
- 🪟 **托盘常驻**：开机自启、关闭最小化到托盘、启动隐藏到托盘
- 🛡️ **隐私**：Steam 登录令牌以 Windows DPAPI 加密落盘（非明文）
- 📋 **日志**：关键事件写入 `%LocalAppData%\MuSync\logs\`（按天滚动），出问题可查
- 📊 设置内性能监控面板：进程内存 / GC / 缓存占用（每 2 秒自动刷新）

## 📥 使用

1. 安装 [.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0)
2. 运行 `MuSync.exe`，首次启动会弹出 Steam 登录（支持手机令牌 / 邮箱验证码，登录后自动保存会话）
3. 打开音乐播放器即可自动同步；LX Music 需在设置中启用「开放 API」（设置 → 开放API → 启用服务）

> 与 Steam 客户端同账号共存不会被顶下线（与 ArchiSteamFarm 同为 SteamKit 会话）；
> 首次登录后，Steam 账号管理里会出现名为 `MuSync` 的设备会话，属正常现象。

## 🔨 构建与测试

```powershell
dotnet build MuSync.sln -c Release     # 编译
dotnet test MuSync.sln                  # 运行单元测试
```

产物：`bin/Release/net9.0-windows10.0.19041.0/MuSync.exe`

GitHub Actions（`main` 分支 push/PR）会自动构建并上传单文件 Release 产物。

## 🔀 与 yySync 的关系

MuSync 基于 [wuyan1337/yySync](https://github.com/wuyan1337/yySync)（MIT）「半新写」：
播放器内存逆向实现沿用原项目成果，状态仲裁、断线重连、日志与安全体系均为本项目独立实现。
相对 yySync 的全部改动见 [CHANGELOG.md](CHANGELOG.md)（0.1.0）。

## 🗂️ 项目结构

```
MuSync.csproj             # 主程序（WinForms 单工程）
tests/MuSync.Tests/       # xUnit 单元测试
reference-yySync/         # 上游参考源码（MIT，不参与编译，仅对照）
Utils/TokenProtector.cs   # DPAPI 令牌加密
```

## 🔐 安全与隐私

把账号授权给 MuSync 前请先阅读 [SECURITY.md](SECURITY.md)：
登录流程、读取范围、网络流量、风险坦白与撤销方法，全部明档交代。

## 🙏 致谢与许可

MuSync 是 [wuyan1337/yySync](https://github.com/wuyan1337/yySync) 的「半新写」重构版：
播放器内存逆向实现沿用原项目成果（MIT），代码结构、状态仲裁、重连与日志体系为本项目独立实现。
第三方许可与版权声明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

本项目以 MIT 协议发布，见 [LICENSE](LICENSE)。
