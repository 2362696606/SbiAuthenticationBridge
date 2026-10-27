using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.Device.Android;

/// <summary>
/// Android 平台的 FAP20 设备工厂
/// </summary>
internal sealed class AndroidFap20DeviceFactory : ISbiDeviceFactory
{
    private readonly IConfiguration _configuration;

    private readonly ISbiHostContext _hostContext;

    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// 创建设备工厂
    /// </summary>
    /// <param name="configuration">应用配置</param>
    /// <param name="hostContext">宿主上下文</param>
    /// <param name="loggerFactory">日志工厂</param>
    public AndroidFap20DeviceFactory(
        IConfiguration configuration,
        ISbiHostContext hostContext,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(hostContext);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _configuration = configuration;
        _hostContext = hostContext;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public ISbiDevice CreateDevice()
    {
        if (!AndroidFap20Options.TryFromConfiguration(_configuration, out var options))
        {
            throw new InvalidOperationException(
                $"未配置 {AndroidFap20Options.SectionName}:vendorId / " +
                $"{AndroidFap20Options.SectionName}:productId，无法创建 FAP20 设备。");
        }

        var opener = new AndroidFtmDeviceOpener(
            options.VendorId,
            options.ProductId,
            _loggerFactory.CreateLogger<AndroidFtmDeviceOpener>());

        _loggerFactory
            .CreateLogger<AndroidFap20DeviceFactory>()
            .LogInformation(
                "创建 Android FAP20 设备：VID=0x{VendorId:X4}，PID=0x{ProductId:X4}",
                options.VendorId,
                options.ProductId);

        var fap20 = new FAP20(opener, _loggerFactory.CreateLogger<FAP20>());
        var proxy = new FAP20Proxy(
            fap20,
            _hostContext,
            _loggerFactory.CreateLogger<FAP20Proxy>());

        return proxy;
    }
}
