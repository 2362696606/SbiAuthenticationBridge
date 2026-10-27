using Microsoft.Extensions.Configuration;

namespace SbiAuthenticationBridge.Device.Android;

/// <summary>
/// Android FAP20 设备配置：USB VID/PID
/// </summary>
/// <param name="VendorId">USB 厂商 ID</param>
/// <param name="ProductId">USB 产品 ID</param>
internal readonly record struct AndroidFap20Options(ushort VendorId, ushort ProductId)
{
    /// <summary>
    /// 设备配置节
    /// </summary>
    public const string SectionName = "Device";

    /// <summary>
    /// 从配置读取 VID/PID
    /// </summary>
    /// <param name="configuration">应用配置</param>
    /// <param name="options">解析结果</param>
    /// <returns>配置完整且合法时为 <see langword="true"/></returns>
    public static bool TryFromConfiguration(IConfiguration configuration,
        out AndroidFap20Options options)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        options = default;

        var section = configuration.GetSection(SectionName);
        if (!TryParseId(section["vendorId"], out var vendorId)
            || !TryParseId(section["productId"], out var productId))
        {
            return false;
        }

        options = new AndroidFap20Options(vendorId, productId);
        return true;
    }

    /// <summary>
    /// 解析 USB ID：支持 <c>0x</c> 十六进制、<c>0</c> 八进制与十进制
    /// </summary>
    private static bool TryParseId(string? text, out ushort id)
    {
        id = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
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
            id = Convert.ToUInt16(text, numberBase);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            return false;
        }

        return id != 0;
    }
}
