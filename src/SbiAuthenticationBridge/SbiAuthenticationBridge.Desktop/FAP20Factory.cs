using System;
using System.Linq;
using DryIoc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Prism.Container.DryIoc;
using Prism.Ioc;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Device;

namespace SbiAuthenticationBridge.Desktop;

// ReSharper disable once InconsistentNaming
public class FAP20Factory : ISbiDeviceFactory
{
    private readonly IContainerProvider _containerProvider;

    public FAP20Factory(IContainerProvider containerProvider)
    {
        _containerProvider = containerProvider;
    }

    public ISbiDevice CreateDevice()
    {
        var configuration = _containerProvider.Resolve<IConfiguration>();
        var (vendorId, productId) = ReadDeviceId(configuration);
        var container = _containerProvider.GetContainer();
        var logger = container.Resolve<ILogger<FAP20>>();
        var opener = new WindowsFtmDeviceOpener(vendorId, productId);
        var fap20 = container.Resolve<FAP20>(args: [opener, logger]);
        var context = container.Resolve<ISbiHostContext>();
        var proxyLogger = container.Resolve<ILogger<FAP20Proxy>>();
        var fap20Proxy = container.Resolve<FAP20Proxy>(args: [fap20, context, proxyLogger]);
        return fap20Proxy;
    }

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