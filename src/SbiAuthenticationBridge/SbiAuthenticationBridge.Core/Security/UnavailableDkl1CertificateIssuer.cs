namespace SbiAuthenticationBridge.Core.Security;

/// <summary>
/// 未配置签发材料时使用的占位实现；调用 <see cref="Issue"/> 会说明原因
/// </summary>
public sealed class UnavailableDkl1CertificateIssuer : IDkl1CertificateIssuer
{
    /// <summary>
    /// 创建占位实现
    /// </summary>
    /// <param name="reason">不可用原因</param>
    public UnavailableDkl1CertificateIssuer(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>
    /// 不可用原因
    /// </summary>
    public string Reason { get; }

    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public string SubjectDescription => Reason;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Issue(ReadOnlyMemory<byte> subjectPublicKeyInfo) =>
        throw new InvalidOperationException(Reason);
}
