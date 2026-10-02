using Xunit;

// 关闭测试类并行：本套件里有测试会临时改全局界面语言（英文文案守卫）与全局配置单例，
// 而 xUnit 默认按类并行 —— 并行时会随机互相污染（2026-10-03 实测：单跑全绿、全量跑 7 条红）。
// 整套测试约 2 秒，串行执行的代价可以忽略，换来稳定可复现的结果。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
