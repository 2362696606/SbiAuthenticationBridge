namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// DKL 公钥
/// </summary>
public sealed record FtmDklPublicKey
{
    /// <summary>当前密钥代数</summary>
    public required int RotationId { get; init; }

    /// <summary>RSA 公钥的 SubjectPublicKeyInfo（DER）</summary>
    public required ReadOnlyMemory<byte> SubjectPublicKeyInfo { get; init; }
}