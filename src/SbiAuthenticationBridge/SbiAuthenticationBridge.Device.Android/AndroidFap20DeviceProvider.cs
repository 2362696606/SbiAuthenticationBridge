using System.Collections.Concurrent;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Device.Android;

/// <summary>
/// Android 平台的 FAP20 设备提供者
/// </summary>
/// <remarks>
/// 与桌面实现一致：启动时创建一个设备实例；未配置 VID/PID 时设备表为空。
/// </remarks>
internal sealed class AndroidFap20DeviceProvider : ISbiDeviceProvider
{
    private readonly ConcurrentDictionary<string, ISbiDevice> _devices = [];

    /// <summary>
    /// 创建设备提供者
    /// </summary>
    /// <param name="factory">设备工厂；为 <see langword="null"/> 时不创建设备</param>
    public AndroidFap20DeviceProvider(ISbiDeviceFactory? factory)
    {
        if (factory is null)
        {
            return;
        }

        _devices.TryAdd(Guid.NewGuid().ToString(), factory.CreateDevice());
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ISbiDevice> Devices => _devices;
}
