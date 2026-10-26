using System.Text.Json;
using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// 请求/响应 DTO 的日志序列化
/// </summary>
internal static class SbiLogBody
{
    /// <summary>
    /// 单条日志正文的最大长度
    /// </summary>
    private const int MaxLength = 4096;

    /// <summary>
    /// 截断标记
    /// </summary>
    private const string TruncatedSuffix = "...(truncated)";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// 将 DTO 序列化为日志文本
    /// </summary>
    /// <param name="value">待序列化的值</param>
    /// <returns>JSON 文本；超长时截断</returns>
    public static string Serialize(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        var json = JsonSerializer.Serialize(value, value.GetType(), Options);
        return json.Length <= MaxLength ? json : json[..MaxLength] + TruncatedSuffix;
    }
}
