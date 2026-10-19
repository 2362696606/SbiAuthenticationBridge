using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SbiAuthenticationBridge.Device.Interop.ICT.FAP20;

/// <summary>
/// ftmBridgeAPI.dll 的平台调用声明，逐条对应 ftmBridgeAPI.h 中的导出函数
/// </summary>
internal static partial class FtmNativeMethods
{
    /// <summary>动态库名（不含扩展名），Windows 下解析为 ftmBridgeAPI.dll</summary>
    internal const string LibraryName = "ftmBridgeAPI";

    /// <summary>对应 ftm_log_init：初始化日志模块并设置输出等级，须在 <see cref="Open"/> 之前调用</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_log_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void LogInit(int level);

    /// <summary>对应 ftm_open：打开并初始化设备，失败返回 <see cref="IntPtr.Zero"/></summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_open")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial IntPtr Open();

    /// <summary>对应 ftm_discover：探测设备在线状态与基本能力，请求为 DISCOVER(0x01) 紧凑二进制 payload</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_discover")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Discover(IntPtr handle, IntPtr request, int wlen, IntPtr response, uint timeoutMs);

    /// <summary>
    /// 对应 ftm_get_device_info：读取固件版本、设备 ID、型号、运行状态等属性，
    /// 请求为 DEVICE_INFO(0x02) 紧凑二进制 payload
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_get_device_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetDeviceInfo(IntPtr handle, IntPtr request, int wlen, IntPtr response, uint timeoutMs);

    /// <summary>
    /// 对应 ftm_load_certificate：下发 LOAD_CERTIFICATE(0x11) 请求（37 字节固定头 + DER），
    /// wlen 为请求总长度而非 DER 长度
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_load_certificate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int LoadCertificate(IntPtr handle, IntPtr request, int wlen, IntPtr response, uint timeoutMs);

    /// <summary>对应 ftm_capture_auth：认证比对场景的指纹采集，请求为 CAPTURE(0x20) 紧凑二进制 payload</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_capture_auth")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int CaptureAuth(IntPtr handle, IntPtr request, int wlen, IntPtr response, uint timeoutMs);

    /// <summary>对应 ftm_capture_reg：用户注册场景的指纹采集，请求为 CAPTURE(0x21) 紧凑二进制 payload</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_capture_reg")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int CaptureReg(IntPtr handle, IntPtr request, int wlen, IntPtr response, uint timeoutMs);

    /// <summary>对应 ftm_get_dkl_public_key：获取设备 DKL 公钥，请求为单字节版本号</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_get_dkl_public_key")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetDklPublicKey(IntPtr handle, IntPtr request, int wlen, IntPtr response, uint timeoutMs);

    /// <summary>对应 ftm_set_dkl_certificate：下发 DKL 证书，请求直接为证书 DER，无帧头</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_set_dkl_certificate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SetDklCertificate(IntPtr handle, IntPtr request, int wlen, IntPtr response, uint timeoutMs);

    /// <summary>对应 ftm_send：USB Bulk OUT 自定义数据发送</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_send")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Send(IntPtr handle, IntPtr sendBuffer, int wlen, uint timeoutMs);

    /// <summary>对应 ftm_recv：USB Bulk IN 自定义数据接收，rlen 为接收缓冲区最大长度</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_recv")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Recv(IntPtr handle, IntPtr rcvBuffer, int rlen, uint timeoutMs);

    /// <summary>对应 ftm_close：关闭设备并释放 USB 接口、libusb 上下文与设备实例内存</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Close(IntPtr handle);

    /// <summary>对应 ftm_setTime：同步系统时间到设备，请求为 SET_TIME(0x03) 紧凑二进制 payload</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_setTime")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SetTime(IntPtr handle, IntPtr request, int wlen, uint timeoutMs);

    /// <summary>
    /// 对应 ftm_setDevStatus：设置设备工作状态，
    /// status 取 0=Normal、1=Busy、2=NotReady、3=Ready、4=NotRegistered
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_setDevStatus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SetDevStatus(IntPtr handle, byte status, uint timeoutMs);

    /// <summary>对应 ftm_device_reset：触发设备软复位，会清空内部采集状态、图像缓冲与工作状态</summary>
    [LibraryImport(LibraryName, EntryPoint = "ftm_device_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int DeviceReset(IntPtr handle, uint timeoutMs);
}
