using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Microsoft.Extensions.Options;
using SbiAuthenticationBridge.Core.Security;

namespace SbiAuthenticationBridge.Services;

/// <summary>
/// 从磁盘加载本机证书材料；首次访问时加载并缓存
/// </summary>
public sealed class FileCertificateProvider : ICertificateProvider
{
    private readonly CertificateProviderOptions _options;

    private readonly Lazy<ProviderKeyPair> _providerKeyPair;

    private readonly Lazy<X509Certificate2?> _domainCertificate;

    /// <summary>
    /// 创建文件证书提供者
    /// </summary>
    /// <param name="options">证书材料存储配置</param>
    public FileCertificateProvider(IOptions<CertificateProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _providerKeyPair = new Lazy<ProviderKeyPair>(LoadProviderKeyPair,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _domainCertificate = new Lazy<X509Certificate2?>(LoadDomainEncryptionCertificate,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public X509Certificate2 ProviderCertificate => _providerKeyPair.Value.Certificate;

    /// <inheritdoc />
    public RSA ProviderPrivateKey => _providerKeyPair.Value.PrivateKey;

    /// <inheritdoc />
    public X509Certificate2? DomainEncryptionCertificate => _domainCertificate.Value;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_providerKeyPair.IsValueCreated)
        {
            _providerKeyPair.Value.Dispose();
        }

        if (_domainCertificate.IsValueCreated)
        {
            _domainCertificate.Value?.Dispose();
        }
    }

    private ProviderKeyPair LoadProviderKeyPair()
    {
        var certificate = X509CertificateLoader.LoadCertificateFromFile(
            RequireFile(_options.ProviderCertificatePath, "Device Provider 证书"));

        try
        {
            return new ProviderKeyPair(certificate, LoadPrivateKey(certificate));
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }

    private RSA LoadPrivateKey(X509Certificate2 certificate)
    {
        var path = RequireFile(_options.ProviderPrivateKeyPath, "Device Provider 私钥");
        var password = _options.ProviderPrivateKeyPassword ?? string.Empty;
        var rsa = RSA.Create();

        try
        {
            rsa.ImportFromEncryptedPem(File.ReadAllText(path), password);
        }
        catch (CryptographicException exception)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                "无法加载 Device Provider 私钥，请检查私钥密码与 PEM 格式。", exception);
        }
        catch (ArgumentException exception)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                "Device Provider 私钥不是受支持的 PEM 格式。", exception);
        }

        try
        {
            using var certificateKey = certificate.GetRSAPublicKey()
                ?? throw new InvalidOperationException("Device Provider 证书不是 RSA 证书。");

            if (!CryptographicOperations.FixedTimeEquals(
                    rsa.ExportSubjectPublicKeyInfo(),
                    certificateKey.ExportSubjectPublicKeyInfo()))
            {
                throw new InvalidOperationException("Device Provider 证书与私钥不匹配。");
            }
        }
        catch
        {
            rsa.Dispose();
            throw;
        }

        return rsa;
    }

    private X509Certificate2? LoadDomainEncryptionCertificate()
    {
        var path = _options.DomainEncryptionCertificatePath;

        return string.IsNullOrWhiteSpace(path)
            ? null
            : X509CertificateLoader.LoadCertificateFromFile(
                RequireFile(path, "MOSIP 域加密证书"));
    }

    private static string RequireFile(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{description}路径未配置。");
        }

        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"{description}不存在：{fullPath}", fullPath);
        }

        return fullPath;
    }

    private sealed class ProviderKeyPair : IDisposable
    {
        internal ProviderKeyPair(X509Certificate2 certificate, RSA privateKey)
        {
            Certificate = certificate;
            PrivateKey = privateKey;
        }

        internal X509Certificate2 Certificate { get; }

        internal RSA PrivateKey { get; }

        public void Dispose()
        {
            PrivateKey.Dispose();
            Certificate.Dispose();
        }
    }
}
