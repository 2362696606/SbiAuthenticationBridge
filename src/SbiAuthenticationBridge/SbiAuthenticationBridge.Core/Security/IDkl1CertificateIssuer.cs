namespace SbiAuthenticationBridge.Core.Security;

/// <summary>
/// 使用 Device Provider 材料为设备候选公钥签发 DKL1 叶证书
/// </summary>
public interface IDkl1CertificateIssuer
{
    /// <summary>
    /// 是否已配置签发材料
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// 证书 Subject 描述，用于界面展示；未配置时为原因说明
    /// </summary>
    string SubjectDescription { get; }

    /// <summary>
    /// 签发叶证书
    /// </summary>
    /// <param name="subjectPublicKeyInfo">设备返回的 RSA SubjectPublicKeyInfo（DER）</param>
    /// <returns>叶证书 DER，长度不超过 3500 字节</returns>
    /// <exception cref="InvalidOperationException">未配置或签发失败</exception>
    ReadOnlyMemory<byte> Issue(ReadOnlyMemory<byte> subjectPublicKeyInfo);
}
