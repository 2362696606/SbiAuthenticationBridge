using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// 面向界面的设备条目：连接控制用 <see cref="IDevice"/>，SBI 查询/采集用 <see cref="ISbiDevice"/>
/// </summary>
/// <remarks>
/// 一个物理设备通常同时提供这两个视图（例如 FAP20 与 <c>Fap20AuthenticationDevice</c>）。
/// 设备条目由平台启动代码创建，UI 只依赖本类型，不直接依赖具体设备实现。
/// </remarks>
public sealed class SbiDeviceEntry
{
    /// <summary>
    /// 创建设备条目
    /// </summary>
    /// <param name="name">界面显示名称</param>
    /// <param name="device">连接控制句柄</param>
    /// <param name="sbiDevice">SBI 设备句柄；设备不支持 SBI 时为空</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空</exception>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> 为 null</exception>
    public SbiDeviceEntry(string name, IDevice device, ISbiDevice? sbiDevice = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(device);

        Name = name;
        Device = device;
        SbiDevice = sbiDevice;
    }

    /// <summary>
    /// 界面显示名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 连接控制句柄
    /// </summary>
    public IDevice Device { get; }

    /// <summary>
    /// SBI 设备句柄；设备不支持 SBI 时为空
    /// </summary>
    public ISbiDevice? SbiDevice { get; }

    /// <summary>
    /// 设备密钥轮换能力；设备不支持时为空
    /// </summary>
    public IDeviceKeyRotation? KeyRotation => Device as IDeviceKeyRotation;
}
