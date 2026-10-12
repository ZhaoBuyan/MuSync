# 事故档案：测试程序集被误判为 Backdoor/Steam

| 项 | 值 |
|---|---|
| 日期 | 2026-10-12 |
| 现象 | 构建报 `无法复制 MuSync.Tests.dll，因为找不到该文件`；该文件反复凭空消失 |
| 判定方 | 第三方杀软「文件实时监控」 |
| 判定名 | `Backdoor/Steam`（病毒ID `7DB2D7E27DAE4B3D`） |
| 结论 | **误报**。测试程序集不含任何 Steam 登录 / 令牌代码 |
| 处置 | 代码侧清掉无意义的可执行包装并加守卫；运行环境侧由使用者自行加排除目录 |

---

## 1. 现象

- 构建 / 测试报错：`无法复制 MuSync.Tests.dll，因为找不到该文件`。
- `tests/MuSync.Tests/bin/**/MuSync.Tests.dll` 与 `obj/**/MuSync.Tests.dll` **凭空消失**，
  同目录的 `.pdb`、`.deps.json`、`SteamKit2.dll` 却都还在。
- 时好时坏：有一次跑绿，随后文件消失，下一次构建立刻失败。

## 2. 证据

### 2.1 消失的只有这一个文件

| 路径 | `MuSync.Tests.dll` | 同目录 `.pdb` |
|---|---|---|
| `tests/MuSync.Tests/obj/Release/net10.0-.../` | 存在（83968 字节） | 存在 |
| `tests/MuSync.Tests/bin/Release/net10.0-.../` | **缺失** | 存在 |
| `tests/MuSync.Tests/bin/Debug/net10.0-.../` | **缺失** | 存在 |
| `bin/**/MuSync.dll`、`MuSync.exe`、`SteamKit2.dll` | 完好 | — |

### 2.2 杀软日志

同一天 11:12–11:21 共 9 条记录，全部指向同一文件、同一判定：

| 时间 | 路径 | 操作类型 | 触发进程 |
|---|---|---|---|
| 11:12:27 | `obj/Release/.../MuSync.Tests.dll` | 修改 | Roslyn `VBCSCompiler` |
| 11:12:27 | `bin/Release/.../MuSync.Tests.dll` | 修改 | `dotnet test -c Release` |
| 11:12:32 | `obj/Debug/.../MuSync.Tests.dll` | 修改 | Roslyn |
| 11:12:32 | `bin/Debug/.../MuSync.Tests.dll` | 修改 | `dotnet test -c Debug` |
| 11:12:39 / :49 / :58、11:13:12 | `obj/Debug/.../MuSync.Tests.dll` | 修改 | Roslyn |
| 11:21:06 | `bin/Release/.../MuSync.Tests.dll` | **执行** | `testhost.exe` |

**判读**：编译写盘、复制到 bin、被测试宿主加载——三个动作都会被判定并删文件。

### 2.3 隔离实验

对同一份已被判定过的 `MuSync.Tests.dll` 做复制测试：

| 实验 | 操作 | 结果 |
|---|---|---|
| 1 | 原样复制到临时目录 | 被删 |
| 2 | 改名后复制 | 被删 |
| 3 | 换到另一目录 | 被删 |
| 4 | 复制主程序集 `MuSync.dll` 到同目录 | 存活 |
| 5 | 复制并**在末尾追加 8 字节**（哈希完全不同） | 被删 |

**实验 5 是关键**：改变文件哈希无法躲过删除 → 判定依据是内容 / 行为本身，不是文件哈希。
因此**改名、换目录、改哈希一律无效**；唯一有效手段是让该路径不被监控。

### 2.4 内容鉴定

对 `MuSync.Tests.dll`（84 KB）与 `MuSync.dll`（543 KB）做字符串对照：

| 关键字 | `MuSync.Tests.dll` | `MuSync.dll` |
|---|---|---|
| `SteamKit2` / `SteamClient` / `LogOnDetails` / `SteamSessionManager` | 无 | 有 |
| `TokenProtector` / `SteamStatusManager` | 仅出现引用名 | 有实现 |

测试程序集里**没有 SteamKit2、没有登录 / 令牌实现**，只有 xunit 测试代码 + 对主程序集类型名的引用。
真正的 Steam 代码在主程序集里，而它被放过了 —— 判定与"谁真的含 Steam 代码"无关。

## 3. 根因与触发时机

1. 杀软按内容 / 行为把测试程序集判为 `Backdoor/Steam`，在写盘、复制、加载三个环节删除文件。
2. 为什么以前能构建：该杀软的病毒库与程序本体在 **2026-10-10 23:21** 完成一次更新；
   10-11 编出的文件内容还没踩上规则，10-12 改了代码后重新编译，一落盘即中招。
   **判据是内容，与构建频率、命令条数、Debug/Release 无关。**

## 4. 被推翻的旧结论

| 旧说法 | 实测结论 |
|---|---|
| "一次调用里连续跑多条 build/test 会触发" | 不成立。单次编译、首次写盘一样中招 |
| "短时间反复改写同一个 dll 会触发" | 不成立。判据是内容 |
| "只跑 Release、别跑 Debug" | 不成立。两侧都被删 |
| "给 obj/bin 加排除就好" | 方向对，但范围要覆盖 `obj` 与 `bin` 两处 |
| "改名 / 换目录能躲开" | 不成立 |
| "测试程序集里编进了 SteamKit2 代码" | 不成立。实测不含 SteamKit2 |
| "根因是测试工程继承了主项目的 `WinExe`" | 不成立。`OutputType` 由 `Microsoft.NET.Test.Sdk.targets` 强制为 `Exe`，项目里改不动 |

## 5. 代码侧处置：清掉无意义的可执行包装

`Microsoft.Common.CurrentVersion.targets` 的 `SetWin32ManifestProperties` 在
`ApplicationManifest` 为空且 `NoWin32Manifest != true` 时，会把 .NET Framework 自带的
`default.win32manifest` 嵌进程序集，于是测试程序集里出现了
`<assemblyIdentity version="1.0.0.0" name="MyApplication.app"/>` 与
`<requestedExecutionLevel level="asInvoker" uiAccess="false"/>`。

- 处置：`tests/MuSync.Tests/MuSync.Tests.csproj` 显式 `<NoWin32Manifest>true</NoWin32Manifest>`。
- 实测效果：测试程序集 83968 → **83456 字节**（−512），应用清单与 `RT_MANIFEST(24)` 资源全部消失。
- 守卫：`tests/MuSync.Tests/TestAssemblyPackagingGuardTests.cs`（文本扫描 + 直接解析 PE 资源目录两道）。
  守卫做过反向验证：把开关改回 `false` 重建，两道检查同时失败。

> **与误报的关系：未证实。** 反证是"改名 / 换目录 / 改哈希照样被删"，说明判据不止一处特征。
> 清的是一层本来就没有意义的包装（测试 DLL 声称自己是可执行程序），**不能声称它解决了误报**。

## 6. 遗留与可选处置

- **加目录排除**（推荐）：把仓库目录加入杀软的「文件实时监控 → 排除」。防护保持开启，只放过这一个目录。
- **上报误报**：附病毒ID `7DB2D7E27DAE4B3D` 与文件名，请厂商修正规则（周期不可控）。
- **临时关闭实时防护**：应急可用，但期间整机无防护。

在排除生效之前，本机**无法稳定执行单元测试**；改代码前请先确认测试能跑通。
