namespace SbiAuthenticationBridge.Core.Security;

/// <summary>
/// DKL1 叶证书签发参数
/// </summary>
/// <remarks>
/// 私钥文件不入库，部署时单独放置；密码应通过受限权限的配置文件提供。
/// </remarks>
public sealed class Dkl1IssuerOptions
{
    /// <summary>
    /// Device Provider 证书路径（DER 或 PEM）
    /// </summary>
    public required string ProviderCertificatePath { get; init; }

    /// <summary>
    /// Device Provider 私钥路径（PKCS#8 PEM，可加密）
    /// </summary>
    public required string ProviderPrivateKeyPath { get; init; }

    /// <summary>
    /// Device Provider 私钥密码；无密码时为空字符串
    /// </summary>
    public required string ProviderPrivateKeyPassword { get; init; }

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
