using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 会临时切「界面语言」的测试必须放进同一个集合串行跑。
/// 原因：语言是全局状态（<c>Loc.IsEnglish</c> 读 <c>Configurations.Instance.Settings.Language</c>），
/// xUnit 默认按类并行，而英文守卫测试要把语言切成 English ——
/// 并行时其余断言中文文案的测试会随机看到英文（2026-10-03 实测：单跑全绿、全量跑 7 条红）。
/// <para>
/// 并行已在程序集级关掉（见 <c>Properties/AssemblyInfo.cs</c>，整套测试约 2 秒，串行代价可忽略）；
/// 本集合作为第二道保险：即便将来有人重新打开并行，语言敏感的类也仍然串行。
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class LanguageSensitiveCollection
{
    public const string Name = "语言敏感（串行）";
}
