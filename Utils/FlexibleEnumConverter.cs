using System;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace MuSync.Utils;

/// <summary>
/// 宽容枚举转换器工厂：用于配置文件读取。
/// 接受字符串（忽略大小写）、数字、数字字符串、未知值（回退默认），
/// 写入统一为字符串。目的：任何格式差异都不再让整份配置被判"损坏"而重置。
/// </summary>
internal sealed class FlexibleEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(FlexibleEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

internal sealed class FlexibleEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override bool HandleNull => true;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var text = reader.GetString();
                if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse<T>(text, ignoreCase: true, out var parsed))
                {
                    return parsed;
                }
                if (long.TryParse(text, out var numeric))
                {
                    return (T)Enum.ToObject(typeof(T), numeric);
                }
                return default;

            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var value))
                {
                    return (T)Enum.ToObject(typeof(T), value);
                }
                return default;

            default:
                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
