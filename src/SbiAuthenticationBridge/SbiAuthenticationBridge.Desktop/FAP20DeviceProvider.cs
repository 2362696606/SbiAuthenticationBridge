using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Device;

namespace SbiAuthenticationBridge.Desktop;

/// <summary>
/// 桌面平台的 FAP20 设备提供者
/// </summary>
/// <remarks>
/// 原生 SDK 在 <c>ftm_open</c> 时按进程工作目录读取 <c>FingerPrintDebug.INI</c> 并自行匹配 VID/PID。
/// 这里读取同一份配置取得 VID/PID，仅用于 <see cref="FAP20"/> 的 USB 在线监测，
/// 避免设备标识在两处分别维护。
/// </remarks>
// ReSharper disable once InconsistentNaming
public class FAP20DeviceProvider : ISbiDeviceProvider
{
    private readonly ConcurrentDictionary<string, ISbiDevice> _devices = [];

    /// <summary>
    /// 创建设备提供者
    /// </summary>
    /// <exception cref="InvalidOperationException">配置中未找到有效的 VID/PID</exception>
    public FAP20DeviceProvider(FAP20Factory factory)
    {
        // ArgumentNullException.ThrowIfNull(configuration);
        // ArgumentNullException.ThrowIfNull(context);
        //
        // var (vendorId, productId) = ReadDeviceId(configuration);
        //
        // var container = provider.GetContainer();
        //
        // var fap20 = new FAP20(vendorId, productId);
        // _devices.TryAdd(Guid.NewGuid().ToString(), new FAP20Proxy(fap20, context));
        _devices.TryAdd(Guid.NewGuid().ToString(), factory.CreateDevice());
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ISbiDevice> Devices => _devices;
}