namespace SbiAuthenticationBridge.Services;

/// <summary>
/// 证书材料的存储配置
/// </summary>
public sealed class CertificateProviderOptions
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
    /// MOSIP 域加密证书路径；为空表示不提供
    /// </summary>
    public string? DomainEncryptionCertificatePath { get; init; }
}
