using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace SbiAuthenticationBridge.Host.Http.Routing;

/// <summary>
/// SBI HTTP 协议公共设置：JSON、CORS、请求体与响应格式
/// </summary>
internal static class SbiHttpProtocol
{
    /// <summary>
    /// CORS 策略名称
    /// </summary>
    public const string CorsPolicyName = "sbi";

    /// <summary>
    /// M-JPEG 流的响应内容类型
    /// </summary>
    public const string MjpegContentType = "multipart/x-mixed-replace; boundary=--sbi-frame";

    private static readonly string[] AllowedMethods =
    [
        "MOSIPDISC",
        "MOSIPDINFO",
        "CAPTURE",
        "RCAPTURE",
        "STREAM",
        "POST",
        "OPTIONS",
    ];

    /// <summary>
    /// 协议 JSON 选项
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// 注册 SBI CORS 策略
    /// </summary>
    /// <param name="services">服务集合</param>
    public static void AddCorsPolicy(IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy => policy
                .AllowAnyOrigin()
                .WithMethods(AllowedMethods)
                .WithHeaders("Content-Type", "Authorization"));
        });
    }

    /// <summary>
    /// 浏览器本地网络访问预检时补响应头
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="next">后续中间件</param>
    public static Task HandlePrivateNetworkPreflightAsync(HttpContext context, Func<Task> next)
    {
        if (context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
        {
            context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        }

        return next();
    }

    /// <summary>
    /// 读取 JSON 请求体
    /// </summary>
    /// <typeparam name="T">请求类型</typeparam>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="required">是否允许空请求体</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>反序列化结果；允许空请求体且无内容时为 <see langword="null"/></returns>
    /// <exception cref="BadHttpRequestException">请求体为空（required 为 true）或不是合法 JSON</exception>
    public static async Task<T?> ReadJsonAsync<T>(
        HttpContext context,
        bool required,
        CancellationToken cancellationToken)
        where T : class
    {
        string json;
        using (var reader = new StreamReader(
                   context.Request.Body,
                   Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: false,
                   leaveOpen: true))
        {
            json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            if (required)
            {
                throw new BadHttpRequestException("请求体不能为空。");
            }

            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(json, JsonOptions);

            if (value is null && required)
            {
                throw new BadHttpRequestException("请求体不能为空。");
            }

            return value;
        }
        catch (JsonException ex)
        {
            throw new BadHttpRequestException("请求体不是合法的 JSON。", ex);
        }
    }

    /// <summary>
    /// 返回 JSON 响应
    /// </summary>
    /// <typeparam name="T">响应类型</typeparam>
    /// <param name="value">响应内容</param>
    /// <param name="statusCode">HTTP 状态码</param>
    public static IResult Json<T>(T value, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(value, JsonOptions, statusCode: statusCode);
}
