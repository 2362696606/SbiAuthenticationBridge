using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SbiAuthenticationBridge.Core.Security;

/// <summary>
/// 提供本机证书材料
/// </summary>
/// <remarks>
/// 证书在首次访问时加载并由实现方持有，调用方不要释放返回的对象。
/// </remarks>
public interface ICertificateProvider : IDisposable
{
    /// <summary>
    /// Device Provider 证书
    /// </summary>
    X509Certificate2 ProviderCertificate { get; }

    /// <summary>
    /// Device Provider 私钥，与 <see cref="ProviderCertificate"/> 匹配
    /// </summary>
    RSA ProviderPrivateKey { get; }

    /// <summary>
    /// MOSIP 域加密证书；未配置时为 <see langword="null"/>
    /// </summary>
    X509Certificate2? DomainEncryptionCertificate { get; }
}
