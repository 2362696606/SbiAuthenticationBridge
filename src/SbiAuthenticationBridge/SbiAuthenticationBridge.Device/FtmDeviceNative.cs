using SbiAuthenticationBridge.Device.Interop.ICT.FAP20;

namespace SbiAuthenticationBridge.Device;

/// <summary>
/// 供平台设备包使用的原生入口：按文件描述符打开设备并指定 SDK 日志路径
/// </summary>
/// <remarks>
/// 只暴露平台设备实现必需的能力，不对外提供协议、句柄等底层细节。
/// </remarks>
public static class FtmDeviceNative
{
    /// <summary>
    /// 用平台已打开的 USB 文件描述符初始化设备
    /// </summary>
    /// <param name="fileDescriptor">UsbDeviceConnection 的文件描述符</param>
    /// <returns>设备句柄；失败时为 <see cref="IntPtr.Zero"/></returns>
    public static IntPtr OpenFd(int fileDescriptor) => FtmNativeMethods.OpenFd(fileDescriptor);

    /// <summary>
    /// 指定 SDK 日志文件路径与输出等级
    /// </summary>
    /// <param name="path">日志文件路径</param>
    /// <param name="level">输出等级</param>
    public static void SetLogPath(string path, FtmLogLevel level) =>
        FtmNativeMethods.LogInitPath(path, (int)level);
}
