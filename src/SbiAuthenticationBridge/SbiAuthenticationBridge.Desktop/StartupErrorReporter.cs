using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SbiAuthenticationBridge.Desktop;

/// <summary>
/// 在应用无法正常启动或运行时显示错误原因
/// </summary>
/// <remarks>
/// 启动失败时 Avalonia 可能尚未完成初始化，因此使用原生消息框而非 Avalonia 窗口；
/// 非 Windows 平台或原生弹窗不可用时退回标准错误输出。
/// </remarks>
internal static partial class StartupErrorReporter
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconError = 0x00000010;
    private const uint MessageBoxSetForeground = 0x00010000;

    /// <summary>
    /// 错误日志文件名，写入应用所在目录
    /// </summary>
    private const string LogFileName = "startup-error.log";

    /// <summary>
    /// 弹窗中显示的最大字符数，完整内容写入日志
    /// </summary>
    private const int MaxDialogLength = 4000;

    /// <summary>
    /// 记录并显示异常
    /// </summary>
    /// <param name="exception">要报告的异常</param>
    /// <param name="title">弹窗标题</param>
    public static void Report(Exception exception, string title)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var details = Format(exception);
        var logPath = TryWriteLog(details);

        var message = new StringBuilder(details);
        if (logPath is not null)
        {
            message.AppendLine().Append("完整信息已写入：").AppendLine(logPath);
        }

        if (message.Length > MaxDialogLength)
        {
            message.Length = MaxDialogLength;
            message.Append('…');
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _ = MessageBoxW(IntPtr.Zero, message.ToString(), title,
                    MessageBoxOk | MessageBoxIconError | MessageBoxSetForeground);
                return;
            }
            catch (Exception exception2) when (exception2 is DllNotFoundException or EntryPointNotFoundException)
            {
                // 原生弹窗不可用时退回控制台
            }
        }

        Console.Error.WriteLine($"{title}: {details}");
    }

    private static string Format(Exception exception)
    {
        var builder = new StringBuilder();
        builder.Append(exception.GetType().FullName).Append(": ").AppendLine(exception.Message);

        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            builder.Append("  --> ")
                .Append(inner.GetType().FullName)
                .Append(": ")
                .AppendLine(inner.Message);
        }

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            builder.AppendLine().AppendLine(exception.StackTrace);
        }

        return builder.ToString();
    }

    private static string? TryWriteLog(string details)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, LogFileName);
            var entry =
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}]{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}";

            File.AppendAllText(path, entry, Encoding.UTF8);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
