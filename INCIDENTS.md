
## 2026-10-28 每秒 5 次的隐藏异常（SteamKit2 内部，非本项目缺陷）

**现象：** `dotnet-counters` 显示 `Exception Count` 稳定 5 次/秒，但日志里一条 WARN/ERROR 都没有。

**排查过程：**
1. 先用 `PerformanceCounter`/`dotnet-counters` 确认线程池线程数恒为 5、队列长度 0、CPU 0.1~0.2% —— 不是线程泄漏或忙等。
2. 在 `Smtc`、`HttpDataSource`、`SteamStatusManager`、`RpcManager` 的候选 catch 点插临时打点：几乎都不触发 → 排除自家超时路径。
3. 加 `FirstChanceException` 探针（`Utils/ExceptionProbe.cs`），按「类型 + 抛出程序集 + 抛出方法」计数：
   - `TimeoutException | System.Threading.Tasks.Dataflow | Internal.Common.InitializeStackTrace` ×177/分钟
   - `TaskCanceledException | System.Net.Http | HttpConnectionPool.ConnectToTcpHostAsync` ×40/分钟
   - `TaskCanceledException / OperationCanceledException | CoreLib` ×90/分钟（上面两项的配对取消）
4. 核对 `SteamKit2.dll` 的引用列表：它引用 `System.Threading.Tasks.Dataflow` —— 本项目里唯一的 Dataflow 使用者。

**结论：** 占比最大的那一条来自 **SteamKit2 内部**（第三方库用 Dataflow 做消息管道并设了超时），
被 SK2 自己吃掉，不影响登录与状态推送（同期 `状态推送超时`/`更新状态失败` 计数均为 0，CPU 0.2%、线程 24、句柄 671 稳定）。
另一条（40/分钟）是配置里的数据源 `http://127.0.0.1:9000/heartrate` 没在监听时的 HttpClient 连接超时，属预期。

**没有改动 SteamKit2 的调用方式**（Sends 均正常）。只留下 `ExceptionProbe` 作为常驻诊断：
每分钟在「有异常时」写一行汇总，无异常则不写。
### 补充：最终根因与修复（同一份记录的可信结论）

上面的"排除自家路径"之后继续追，最后定位到**我们的调用方式触发 SK2 的低效实现**：

```
CallbackLoop
  → CallbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1))
    → SteamClient.WaitForCallback(timeout)
      → DataflowBlock.Receive(callbackQueue, timeout)   ← 队列空时**抛 TimeoutException**
        → SK2 自己 catch，当作"这一轮没有回调"
```

用 IL 反射核对过 SteamKit2 3.4.0：`SteamClient.callbackQueue` 是 `BufferBlock<CallbackMsg>`，
`WaitForCallback` 系列方法调用 `DataflowBlock.Receive/ReceiveAsync/TryReceive`，全项目只有它用 Dataflow。

**修复（提交 `0e6f21c`）**：改用 `RunWaitCallbackAsync(token)`（无超时、靠取消令牌退出）。

**实测对比：**
| | 每分钟异常 | `TimeoutException | Dataflow` |
|---|---|---|
| 改前 | 300~310 | 177 |
| 改后 | 130~137 | **0** |

剩余 130/分钟 = 数据源 `127.0.0.1:9000` 未监听时的 HttpClient 超时（40）+ SK2 连接取消的配对计数（90），都属预期。

**顺带收益**：回调（登录成功 / 断开）从"最多晚 1 秒"变成"立即处理"。

**不上游提 PR**：SK2 就是这样实现的（用超时异常表示"没有消息"），本地绕开即可。
