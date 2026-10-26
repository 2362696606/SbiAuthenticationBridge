using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Host.Http.Routing;

/// <summary>
/// 将宿主内异常转换为 SBI 错误报文
/// </summary>
internal sealed class SbiExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SbiExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// 创建异常处理中间件
    /// </summary>
    /// <param name="next">后续中间件</param>
    /// <param name="logger">日志</param>
    public SbiExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<SbiExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// 执行请求并映射异常
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("请求已取消：{Method} {Path}", context.Request.Method, context.Request.Path);
        }
        catch (SbiException ex)
        {
            _logger.LogWarning(
                ex,
                "SBI 业务错误：{Method} {Path}，错误码 {ErrorCode}",
                context.Request.Method,
                context.Request.Path,
                ex.Error.ErrorCode);

            await WriteErrorAsync(context, StatusCodes.Status200OK, ex.Error).ConfigureAwait(false);
        }
        catch (BadHttpRequestException ex)
        {
            _logger.LogWarning(ex, "请求格式错误：{Method} {Path}", context.Request.Method, context.Request.Path);

            await WriteErrorAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    SbiErrorHelper.Custom(500, "Invalid request body"))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未处理异常：{Method} {Path}", context.Request.Method, context.Request.Path);

            await WriteErrorAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    SbiErrorHelper.Custom(500, "Internal server error"))
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 写出 SBI 错误报文；响应已开始时不再写入
    /// </summary>
    private static async Task WriteErrorAsync(HttpContext context, int statusCode, SbiError error)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response
            .WriteAsJsonAsync(new ErrorResponse { Error = error }, SbiHttpProtocol.JsonOptions)
            .ConfigureAwait(false);
    }
}
