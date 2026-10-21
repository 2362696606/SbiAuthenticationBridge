using SbiAuthenticationBridge.Device.Interop.ICT.FAP20;

namespace SbiAuthenticationBridge.Device;

/// <summary>
/// Android 宿主初始化 ftmBridgeAPI 的入口
/// </summary>
/// <remarks>
/// Android 的当前目录不可写，且 INI 文件位于 APK 的 assets 中。宿主应在创建/连接
/// <see cref="FAP20"/> 之前，把 INI 复制到应用私有目录，然后依次调用
/// <see cref="SetConfigPath"/> 与 <see cref="InitLog"/>。
/// </remarks>
public static class Fap20AndroidSdk
{
    /// <summary>
    /// 日志是否已由 Android 宿主指定路径
    /// </summary>
    internal static bool IsLogConfigured { get; private set; }

    /// <summary>
    /// 指定 FingerPrintDebug.INI 的绝对路径
    /// </summary>
    /// <param name="path">应用私有目录中的 INI 绝对路径</param>
    public static void SetConfigPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FtmNativeMethods.SetConfigPath(path);
    }

    /// <summary>
    /// 把 SDK 日志写入应用私有目录
    /// </summary>
    /// <param name="path">日志文件绝对路径</param>
    /// <param name="level">0=Debug、1=Info、2=Warn、3=Error、4=关闭</param>
    public static void InitLog(string path, int level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FtmNativeMethods.LogInitPath(path, level);
        IsLogConfigured = true;
    }

    /// <summary>
    /// 使用宿主已打开的 USB 文件描述符创建设备句柄（对应 <c>ftm_open_fd()</c>）
    /// </summary>
    /// <param name="fileDescriptor">UsbDeviceConnection 的文件描述符</param>
    /// <returns>设备句柄；失败时为 <see cref="IntPtr.Zero"/></returns>
    public static IntPtr OpenFromFileDescriptor(int fileDescriptor) =>
        FtmNativeMethods.OpenFromFileDescriptor(fileDescriptor);
}
