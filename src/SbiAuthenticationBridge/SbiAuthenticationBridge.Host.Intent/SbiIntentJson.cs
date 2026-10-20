using System.Text.Json;
using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// Intent 宿主使用的 JSON 序列化配置
/// </summary>
internal static class SbiIntentJson
{
    /// <summary>
    /// 与 SBI 报文一致的序列化选项：属性名由 DTO 上的 JsonPropertyName 决定，
    /// 反序列化忽略大小写，序列化时省略 null 字段。
    /// </summary>
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 序列化为 UTF-8 JSON
    /// </summary>
    internal static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    /// <summary>
    /// 从 UTF-8 JSON 反序列化；空输入返回 <c>null</c>
    /// </summary>
    internal static T? Deserialize<T>(byte[]? payload) where T : class
    {
        return payload is { Length: > 0 } ? JsonSerializer.Deserialize<T>(payload, Options) : null;
    }
}
