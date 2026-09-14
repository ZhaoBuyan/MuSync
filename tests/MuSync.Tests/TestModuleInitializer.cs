using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace MuSync.Tests;

/// <summary>
/// 测试隔离：让日志写入临时目录，避免测试运行污染用户的真实日志目录
/// （Logger 读取 MUSYNC_LOG_DIR 环境变量作为日志目录覆盖）。
/// </summary>
internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var logDir = Path.Combine(
            Path.GetTempPath(), "musync-test-logs", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MUSYNC_LOG_DIR", logDir);
    }
}
