namespace SbiAuthenticationBridge.Core.Model;

/// <summary>
/// 固件返回的候选 DKL1 公钥
/// </summary>
public sealed record Dkl1PublicKeyInfo
{
    /// <summary>
    /// RSA 公钥的 SubjectPublicKeyInfo（DER）；私钥不离开设备
    /// </summary>
    public required ReadOnlyMemory<byte> SubjectPublicKeyInfo { get; init; }

    /// <summary>
    /// 公钥编码格式，当前固定为 <c>X509_SPKI_DER</c>
    /// </summary>
    public string PublicKeyFormat { get; init; } = "X509_SPKI_DER";

    /// <summary>
    /// 公钥算法，当前固定为 <c>RSA-2048</c>
    /// </summary>
    public string KeyAlgorithm { get; init; } = "RSA-2048";
}
