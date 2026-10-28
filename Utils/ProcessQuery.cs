using System;
using System.Diagnostics;

namespace MuSync.Utils;

/// <summary>
/// 进程查询的收尾工具。
/// <para>
/// 为什么需要：<c>Process.GetProcessesByName()</c> 返回的每个 <see cref="Process"/> 都占着一个操作系统句柄，
/// **必须逐个 Dispose**，否则要等 GC 终结器才释放。播放器/程序检测是每秒级的热路径，
/// 不释放会表现为"句柄缓慢增长"（长时间运行后尤其明显，Task Manager 里看得到）。
/// </para>
/// <para>
/// 注意 <c>Length &gt; 0</c> 这种写法最容易漏释放：连"只想知道有没有在跑"也要把结果释放掉。
/// </para>
/// </summary>
internal static class ProcessQuery
{
    /// <summary>指定名字的进程是否在运行（内部会释放所有查询结果）。</summary>
    public static bool IsRunning(string processName)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return false;
        }

        try
        {
            return processes.Length > 0;
        }
        finally
        {
            DisposeAll(processes);
        }
    }

    /// <summary>取指定名字的第一个进程，并把其余查询结果释放掉（调用方负责释放返回值）。</summary>
    public static Process? FirstRunning(string processName)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return null;
        }

        if (processes.Length == 0) return null;
        var first = processes[0];
        for (var i = 1; i < processes.Length; i++) processes[i].Dispose();
        return first;
    }

    /// <summary>把一组进程对象全部释放（忽略单个失败）。</summary>
    public static void DisposeAll(Process[] processes)
    {
        foreach (var process in processes)
        {
            try
            {
                process.Dispose();
            }
            catch
            {
                // 单个释放失败不影响其它
            }
        }
    }
}