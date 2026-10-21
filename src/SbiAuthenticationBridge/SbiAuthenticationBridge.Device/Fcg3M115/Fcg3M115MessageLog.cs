using Serilog;

namespace SbiAuthenticationBridge.Device.Fcg3M115;

/// <summary>
/// FCG-3M115 设备级原始报文记录：请求、原生返回码与响应缓冲区
/// </summary>
/// <remarks>
/// <para>
/// 采图响应包含加密后的生物特征数据，报文文件应按敏感数据处理。
/// <see cref="Directory"/> 为空时不写文件；Android 宿主会将其设置为应用私有目录。
/// </para>
/// <para>
/// 与 SBI 报文日志不同，这里记录的是下发到模组的 336 字节请求和模组返回的原始缓冲区，
/// 可用于区分旧版 JSON 固件与 FCG-3M115 固件。
/// </para>
/// </remarks>
public static class Fcg3M115MessageLog
{
    private const int RetentionCount = 100;

    private static string? _directory;

    private static ILogger Logger => Log.ForContext("SourceContext", "FCG3M115");

    /// <summary>
    /// 报文目录；为空表示不记录
    /// </summary>
    public static string? Directory
    {
        get => _directory;
        set => _directory = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// 写入一次设备命令的请求、返回码与响应
    /// </summary>
    /// <param name="operation">命令名，例如 <c>capture-auth</c></param>
    /// <param name="request">下发的请求字节</param>
    /// <param name="nativeResult">原生接口返回码</param>
    /// <param name="response">原生接口填充的响应缓冲区</param>
    public static void Write(string operation, ReadOnlySpan<byte> request, int nativeResult,
        ReadOnlySpan<byte> response)
    {
        var directory = _directory;
        if (directory is null)
        {
            return;
        }

        try
        {
            System.IO.Directory.CreateDirectory(directory);

            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
            var id = Guid.NewGuid().ToString("N")[..8];
            var prefix = $"{stamp}-{operation}-{id}";

            File.WriteAllBytes(Path.Combine(directory, prefix + "-request.bin"), request.ToArray());
            File.WriteAllBytes(Path.Combine(directory, prefix + "-response.bin"), response.ToArray());

            var lastNonZero = -1;
            for (var index = response.Length - 1; index >= 0; index--)
            {
                if (response[index] != 0)
                {
                    lastNonZero = index;
                    break;
                }
            }

            File.WriteAllText(Path.Combine(directory, prefix + "-result.txt"),
                $"operation={operation}{Environment.NewLine}" +
                $"nativeResult={nativeResult} (0x{nativeResult:X}){Environment.NewLine}" +
                $"requestLength={request.Length}{Environment.NewLine}" +
                $"responseBufferLength={response.Length}{Environment.NewLine}" +
                $"responseLastNonZeroIndex={lastNonZero}{Environment.NewLine}");

            Trim(directory);
        }
        catch (Exception exception)
        {
            // 日志失败不能影响采集
            Logger.Warning(exception, "写入 FCG-3M115 原始报文失败");
        }
    }

    /// <summary>
    /// 只保留最近的若干组报文，避免长期运行占满应用私有目录
    /// </summary>
    private static void Trim(string directory)
    {
        var requestFiles = new DirectoryInfo(directory).GetFiles("*-request.bin");
        if (requestFiles.Length <= RetentionCount)
        {
            return;
        }

        foreach (var requestFile in requestFiles
                     .OrderBy(file => file.LastWriteTimeUtc)
                     .Take(requestFiles.Length - RetentionCount))
        {
            var prefix = requestFile.Name[..^"-request.bin".Length];

            TryDelete(Path.Combine(directory, prefix + "-request.bin"));
            TryDelete(Path.Combine(directory, prefix + "-response.bin"));
            TryDelete(Path.Combine(directory, prefix + "-result.txt"));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 文件可能正被读取，留给下次清理
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }
}
