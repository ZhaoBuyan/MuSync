using System.Text.Json;

namespace MuSync.Utils;

/// <summary>
/// 配置读写的 JSON 选项（惰性单例）。
/// ⚠️ 关键约束：Configurations 实例在类型初始化期间就会读取/写入配置，
/// 因此选项不能声明成"按静态字段顺序初始化"的字段（那样构造期间它还是 null，
/// 读取会退化为默认转换器 → 读不了枚举字符串 → 每次启动"配置解析失败"）。
/// 惰性创建保证任何时机访问都就绪。
/// </summary>
internal static class ConfigJson
{
    private static JsonSerializerOptions? _options;

    public static JsonSerializerOptions Options => _options ??= new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new FlexibleEnumConverterFactory() }
    };
}
