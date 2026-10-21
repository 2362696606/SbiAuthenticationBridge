namespace SbiAuthenticationBridge.Core.Dto;

/// <summary>
/// 设备密钥信息
/// </summary>
public sealed record DeviceKeyInfo
{
    /// <summary>
    /// 密钥代数，每次轮换递增
    /// </summary>
    public required int RotationId { get; init; }

    /// <summary>
    /// RSA 公钥的 SubjectPublicKeyInfo（DER）
    /// </summary>
    public required ReadOnlyMemory<byte> SubjectPublicKeyInfo { get; init; }

    /// <summary>
    /// 证书或公钥的 SHA-256 指纹（大写十六进制）
    /// </summary>
    public string? Fingerprint { get; init; }
}
