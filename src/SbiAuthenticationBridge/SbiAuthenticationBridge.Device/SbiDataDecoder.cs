using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace SbiAuthenticationBridge.Device;

/// <summary>
/// 解码 SBI <c>data</c> JWT，为 CTK 要求的 <c>dataDecoded</c> 字段准备内容
/// </summary>
/// <remarks>
/// <para>
/// CTK 的 <c>AuthCaptureResponseSchema</c> 要求每个成功采集项同时包含
/// <c>data</c>（原始 JWT）和 <c>dataDecoded</c>（JWT payload 的 JSON 对象）。
/// </para>
/// <para>
/// 这里只读取 payload 并补上 <c>digitalIdDecoded</c>，不验证也不修改 JWT 签名；
/// 签名校验由 CTK 基于原始 <c>data</c> 完成。
/// </para>
/// </remarks>
internal static class SbiDataDecoder
{
    private static ILogger Logger => Log.ForContext("SourceContext", "FAP20");

    /// <summary>
    /// 解码 <c>data</c> JWT 的 payload
    /// </summary>
    /// <param name="dataJwt">设备返回的 JWT 字符串</param>
    /// <returns>payload JSON 对象；无法解码时返回 <c>null</c></returns>
    internal static JsonElement? TryDecode(string? dataJwt)
    {
        if (string.IsNullOrWhiteSpace(dataJwt))
        {
            return null;
        }

        try
        {
            var payload = DecodePayload(dataJwt)?.AsObject();
            if (payload is null)
            {
                return null;
            }

            // digitalId 本身也是一个 JWT；schema 要求额外提供其 payload。
            if (payload["digitalId"] is JsonValue digitalIdValue
                && digitalIdValue.TryGetValue<string>(out var digitalIdJwt))
            {
                var digitalIdPayload = DecodePayload(digitalIdJwt)?.AsObject();
                if (digitalIdPayload is not null)
                {
                    payload["digitalIdDecoded"] = digitalIdPayload;
                }
            }

            return JsonSerializer.SerializeToElement(payload);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "解析 SBI data JWT 失败，未生成 dataDecoded");
            return null;
        }
    }

    /// <summary>
    /// 解出 JWT 的 payload 节点
    /// </summary>
    private static JsonNode? DecodePayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        return JsonNode.Parse(json);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }
}
