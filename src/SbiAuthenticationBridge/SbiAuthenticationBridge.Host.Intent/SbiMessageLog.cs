using Serilog;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// 把 SBI 请求/响应原始报文落盘，便于对照 CTK 用例排查
/// </summary>
/// <remarks>
/// 采集响应包含加密后的生物特征数据，报文文件应按敏感数据处理。
/// Android 应把 <see cref="Directory"/> 指向应用私有目录。
/// </remarks>
public static class SbiMessageLog
{
    private const int MaxRetainedFiles = 100;

    private static readonly ILogger Logger = Log.ForContext("SourceContext", "Intent");

    private static readonly Lock Gate = new();

    /// <summary>
    /// 报文保存目录；未设置时使用程序目录下的 <c>sbi-messages</c>
    /// </summary>
    public static string? Directory
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// 写入一次调用的请求与响应
    /// </summary>
    /// <param name="action">Intent action</param>
    /// <param name="request">原始请求字节（可为空）</param>
    /// <param name="response">原始响应字节（可为空）</param>
    /// <param name="exception">处理过程中的异常（可为空）</param>
    public static void Write(string? action, byte[]? request, byte[]? response,
        Exception? exception = null)
    {
        try
        {
            var directory = Directory ?? Path.Combine(AppContext.BaseDirectory, "sbi-messages");
            System.IO.Directory.CreateDirectory(directory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var unique = Guid.NewGuid().ToString("N")[..8];
            var baseName = $"{timestamp}-{Sanitize(action)}-{unique}";

            if (request is { Length: > 0 })
            {
                File.WriteAllBytes(Path.Combine(directory, baseName + "-request.json"), request);
            }

            if (response is { Length: > 0 })
            {
                File.WriteAllBytes(Path.Combine(directory, baseName + "-response.json"), response);
            }

            if (exception is not null)
            {
                File.WriteAllText(Path.Combine(directory, baseName + "-error.txt"),
                    exception.ToString());
            }

            Prune(directory);

            Logger.Information(
                "SBI 报文已记录：{Action:l}，请求 {RequestBytes} 字节，响应 {ResponseBytes} 字节，目录 {Directory:l}",
                action, request?.Length ?? 0, response?.Length ?? 0, directory);
        }
        catch (Exception logException)
        {
            Logger.Warning(logException, "写入 SBI 报文日志失败");
        }
    }

    /// <summary>
    /// 只保留最近的若干报文文件，避免长期运行占满存储
    /// </summary>
    private static void Prune(string directory)
    {
        lock (Gate)
        {
            var expired = new DirectoryInfo(directory).GetFiles("*")
                .OrderByDescending(file => file.CreationTimeUtc)
                .Skip(MaxRetainedFiles)
                .ToArray();

            foreach (var file in expired)
            {
                try
                {
                    file.Delete();
                }
                catch (IOException)
                {
                    // 文件被占用时下次再清理
                }
            }
        }
    }

    /// <summary>
    /// 过滤 action 中的非法文件名字符
    /// </summary>
    private static string Sanitize(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(action.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());

        return name.Length <= 64 ? name : name[..64];
    }
}
