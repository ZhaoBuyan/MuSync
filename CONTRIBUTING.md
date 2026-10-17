# 参与贡献

欢迎帮忙——不管是提一个界面想法，还是直接改代码。这份文档的目标是让你**十分钟内把项目跑起来**，并且少踩几个容易浪费时间的坑。

> 如果你只想提想法（不需要跑代码）：直接去 [Issues](https://github.com/ZhaoBuyan/MuSync/issues/new/choose)，
> 选「界面改版提案」或「Bug 反馈」，附一张改版图或描述就行。

---

## 一、环境要求

| 需要 | 说明 |
|---|---|
| **Windows 10 / 11** | 项目是 WinForms，只能跑在 Windows 上 |
| **.NET 10 SDK** | 目标框架 `net10.0-windows10.0.19041.0`；低于 10 的 SDK 编译不过 |
| Git | 常规用法 |

不需要 Steam 账号、不需要安装任何音乐播放器——**测试全部离线**，不碰真 Steam 登录。

---

## 二、三步跑起来

```powershell
git clone https://github.com/ZhaoBuyan/MuSync.git
cd MuSync

# 1. 编译（必须 0 警告——项目开了 TreatWarningsAsErrors，有警告就是编译失败）
dotnet build MuSync.sln -c Release

# 2. 跑全部测试（应当是 427 passed / 0 failed）
dotnet test MuSync.sln

# 3. 直接运行看效果
dotnet run --project MuSync.csproj -c Debug
```

**预期**：编译输出 `已成功生成`（或 `Build succeeded`）且**没有任何 warning**；
测试输出 `已通过! - 失败: 0，通过: 427`。

---

## 三、几个会浪费时间的坑

### 1. 第三方杀软可能删掉测试程序集

症状：编译成功但测试跑不起来、或提示找不到 `MuSync.Tests.dll`；有时 427 条里随机几条失败。

原因：部分国产杀软（如某些安全软件）把 xUnit 测试宿主的行为误判为可疑，会**直接删除** `MuSync.Tests.dll`。
这不是代码问题。处置：把仓库目录或 `tests/MuSync.Tests/bin` 加入杀软白名单，然后重新编译。

### 2. `bin\Release` 的 exe 被运行中的程序锁住

症状：Release 编译报 `文件被占用 / MSB3021 / MSB3027`。

原因：MuSync 正在运行，而它就是从 `bin\Release` 里启动的。

处置：先退出 MuSync 再编译；或者改构建 Debug（`-c Debug`），两者输出目录不同，互不影响。

### 3. 警告即错误

项目开了 `TreatWarningsAsErrors`。几条容易踩的：

- 新增 `PackageReference` 时若该包是框架内置的，会报「此包自动可用，无需显式引用」——不要为了消警告去关掉这个开关
- 可空引用类型（`nullable`）相关的警告一律要处理，不要用 `!` 硬压

### 4. 编译出的产物版本号

版本号在 `MuSync.csproj` 的 `<Version>` 里。**改版本号属于发版动作**，普通 PR 不需要动它。

---

## 四、改代码的约定

**动手前请读 [`docs/STANDARDS.md`](docs/STANDARDS.md)**，它规定了注释口径、文档放置、测试要求与发版流程。要点：

- **注释写约束，不写开发过程。** 写"为什么必须这样"（尤其是那些踩过坑才发现的约束），不要写"我改了什么"——那属于提交记录。
- **行为改动必须带测试。** 修 bug 尽量先写一条能复现的测试。
- **不要引入新依赖**除非确实必要；这个项目刻意保持依赖极少（目前只有 SteamKit2）。
- 代码注释与文档使用中文；标识符用英文。

### 界面（UI）重构的约定

界面目前能用、但不够好看，**信息层次**尤其需要改进。欢迎改，但方向上有明确取舍：

**要这样**：

- 用 **Windows 原生窗口与控件**（`GroupBox`、`TabControl`、`TableLayoutPanel`、原生字体与间距）
- 靠**左导航、分组、间距、字号层级**把信息摆清楚
- 保持现有的功能与快捷键不变，只改呈现

**不要这样**：

- 不要自绘边框 / 圆角 / 阴影，不要引入第三方 UI 库（如 Material、Antd 之类）来换观感
- 不要把设置窗口重写成"卡片流"式布局
- 不要为了好看去掉功能入口（尤其是诊断类入口，它们是排障用的）

改之前建议先在 Issue 里贴一张改版图或布局说明，避免做完被退回。

---

## 五、提交 Pull Request

1. **一个 PR 做一件事。** 修 bug 和重构界面不要混在一个 PR 里。
2. **PR 描述里写清三件事**：
   - 改了什么
   - 为什么（如果是修 bug，附复现步骤）
   - 怎么验证的（跑了什么测试、手动点了哪些路径）
3. **提交前自检**：

   ```powershell
   dotnet build MuSync.sln -c Release    # 0 警告
   dotnet test  MuSync.sln               # 427 passed, 0 failed
   ```

4. **PR 里不要改**：版本号（`<Version>`）、`CHANGELOG.md` 的已发布段落、`LICENSE`、`THIRD-PARTY-NOTICES.md`。
   这些由维护者在发版时统一处理。

### 关于会被怎么处理

这是**单人维护的项目**：PR 会被认真看，但可能出现这些情况，先说明以免误会——

- 维护者会直接在你的 PR 上改代码或调整措辞（风格统一优先）
- 你提的功能可能被推迟：这个项目的优先级是 **① 不能有 bug ② 优化 ③ 加新功能**
- 界面改动可能被要求缩小范围，只保留信息层次那部分

---

## 六、想帮忙但不确定做什么？

这些方向都欢迎，且不需要先问：

- **界面**：贴一张改版图或布局意见（成本最低、价值最高）
- **文档**：README 的错别字、看不懂的段落、缺失的截图
- **问题复现**：如果你遇到 bug，附上 设置 → 诊断 → 「复制诊断信息」的内容，比自己修更有用
- **新的播放器来源**：只要能读到进度信息就能接，先开 Issue 讨论可行性

---

## 七、相关文档

| 文档 | 内容 |
|---|---|
| [`README_CN.md`](README_CN.md) | 功能说明、快速开始、常见问题 |
| [`docs/STANDARDS.md`](docs/STANDARDS.md) | 开发规范：注释、文档、测试、发版 |
| [`CHANGELOG.md`](CHANGELOG.md) | 每个版本改了什么 |
| [`SECURITY.md`](SECURITY.md) | 安全问题怎么报 |
| [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) | 第三方组件与许可 |

---

## 附：English speakers

This project's working language is Chinese (comments, docs, commit messages), and the maintainer is a solo developer.
If you'd like to contribute, please **open an issue first** describing what you want to do — most code areas need
context that isn't obvious from the source alone (for example why a player is read through process memory versus
the system media controls, or why the main loop must never block). The build and test steps in section 2 apply as-is.
