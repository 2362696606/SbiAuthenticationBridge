using SbiAuthenticationBridge.Core.Model;

namespace SbiAuthenticationBridge.Core;

/// <summary>
/// L1 安全设备的密钥与证书配置能力
/// </summary>
public interface IFtmSecureDevice
{
    /// <summary>
    /// 获取固件准备/返回的候选 DKL1 公钥，（当前活动公钥与证书无法获取，获取也不应影响当前活动公钥与密钥）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>候选公钥CSR</returns>
    Task<Dkl1PublicKeyInfo> GetDkl1PublicKeyAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 安装 Device Provider 签发的 DKL1 叶证书，并原子切换到新密钥
    /// </summary>
    /// <param name="certificateDer">DKL1 叶证书 DER，长度 1..3500 字节</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SetDkl1CertificateAsync(
        ReadOnlyMemory<byte> certificateDer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 安装 MOSIP 域加密证书（服务器/应用公钥证书）
    /// </summary>
    /// <param name="certificateDer">域加密证书 DER，长度 1..3500 字节</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task LoadDomainEncryptionCertificateAsync(
        ReadOnlyMemory<byte> certificateDer,
        CancellationToken cancellationToken = default);
}