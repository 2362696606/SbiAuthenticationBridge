using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
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
    private readonly List<ISbiDevice> _devices = [];

    /// <summary>
    /// 创建设备提供者
    /// </summary>
    /// <param name="configuration">平台配置，须包含 <c>FingerPrintDebug.INI</c></param>
    /// <param name="context">宿主上下文</param>
    /// <exception cref="InvalidOperationException">配置中未找到有效的 VID/PID</exception>
    public FAP20DeviceProvider(IConfiguration configuration, ISbiHostContext context)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);

        var (vendorId, productId) = ReadDeviceId(configuration);

        var fap20 = new FAP20(vendorId, productId);
        _devices.Add(new FAP20Proxy(fap20, context));
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ISbiDevice> Devices => _devices;

    /// <summary>
    /// 读取配置中第一个同时具有 VID/PID 的设备段
    /// </summary>
    /// <remarks>
    /// 与 SDK 的 <c>std::stoull(value, nullptr, 0)</c> 保持一致：支持 <c>0x</c> 前缀的十六进制、
    /// <c>0</c> 前缀的八进制以及十进制。数字段名按序号升序处理，对应 SDK 的遍历顺序。
    /// </remarks>
    private static (ushort VendorId, ushort ProductId) ReadDeviceId(IConfiguration configuration)
    {
        var sections = configuration.GetChildren()
            .OrderBy(section => int.TryParse(section.Key, out var index) ? index : int.MaxValue);

        foreach (var section in sections)
        {
            var vendorId = ParseDeviceId(section["VID"]);
            var productId = ParseDeviceId(section["PID"]);

            if (vendorId is not null && productId is not null)
            {
                return (vendorId.Value, productId.Value);
            }
        }

        throw new InvalidOperationException("FingerPrintDebug.INI 中未找到有效的 VID/PID。");
    }

    private static ushort? ParseDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        var numberBase = 10;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            numberBase = 16;
            text = text[2..];
        }
        else if (text.Length > 1 && text[0] == '0')
        {
            numberBase = 8;
            text = text[1..];
        }

        try
        {
            // VID/PID 不能为 0，0 视为无效配置
            var deviceId = Convert.ToUInt16(text, numberBase);
            return deviceId == 0 ? null : deviceId;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            return null;
        }
    }
}