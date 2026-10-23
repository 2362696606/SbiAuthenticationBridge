namespace SbiAuthenticationBridge.Core.Security;

/// <summary>
/// DKL1 叶证书的 Subject 与输出参数
/// </summary>
/// <remarks>
/// 证书与私钥路径由 <see cref="ICertificateProvider"/> 的配置提供。
/// </remarks>
public sealed class Dkl1IssuerOptions
{
    /// <summary>
    /// 叶证书 Subject 中的序列号（CN）
    /// </summary>
    public required string SerialNo { get; init; }

    /// <summary>
    /// 叶证书 Subject 中的品牌（OU 前缀）
    /// </summary>
    public required string Make { get; init; }

    /// <summary>
    /// 叶证书 Subject 中的型号（OU 后缀）
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// 叶证书 Subject 中的设备提供商（O）
    /// </summary>
    public required string DeviceProvider { get; init; }

    /// <summary>
    /// 签发后的叶证书输出路径；为空时不保存
    /// </summary>
    public string? IssuedCertificatePath { get; init; }
}
