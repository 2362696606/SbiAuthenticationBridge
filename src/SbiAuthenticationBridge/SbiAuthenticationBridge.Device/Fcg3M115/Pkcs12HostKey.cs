using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SbiAuthenticationBridge.Device.Fcg3M115;

/// <summary>
/// 从 P12/PFX 或 PEM 加载的 FCG-3M115 Host 密钥（桌面实现）
/// </summary>
/// <remarks>
/// 路径由环境变量 <c>FCG3M115_HOST_P12</c> 指定，密码由
/// <c>FCG3M115_HOST_P12_PASSWORD</c> 指定。PEM 文件可包含证书与加密私钥。
/// </remarks>
public sealed class Pkcs12HostKey : IFcg3M115HostKey
{
    private readonly X509Certificate2 _certificate;

    private readonly RSA _rsa;

    private readonly Lock _gate = new();

    private Pkcs12HostKey(X509Certificate2 certificate, RSA rsa)
    {
        _certificate = certificate;
        _rsa = rsa;
        CertificateDer = certificate.Export(X509ContentType.Cert);
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> CertificateDer { get; }

    /// <summary>
    /// 从环境变量加载 Host 密钥
    /// </summary>
    /// <returns>未配置 <c>FCG3M115_HOST_P12</c> 时返回 <c>null</c></returns>
    /// <exception cref="InvalidOperationException">路径已配置但加载失败</exception>
    public static Pkcs12HostKey? TryLoadFromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("FCG3M115_HOST_P12");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var password = Environment.GetEnvironmentVariable("FCG3M115_HOST_P12_PASSWORD");
        return Load(path, password);
    }

    /// <summary>
    /// 从 P12/PFX 或 PEM 文件加载 Host 密钥
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="password">私钥密码</param>
    /// <exception cref="InvalidOperationException">文件不含 RSA 私钥</exception>
    public static Pkcs12HostKey Load(string path, string? password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        X509Certificate2 certificate;
        var extension = Path.GetExtension(path).ToLowerInvariant();

        switch (extension)
        {
            case ".p12":
            case ".pfx":
                certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password,
                    X509KeyStorageFlags.EphemeralKeySet);
                break;
            case ".pem":
            {
                var pem = File.ReadAllText(path);
                certificate = string.IsNullOrEmpty(password)
                    ? X509Certificate2.CreateFromPem(pem)
                    : X509Certificate2.CreateFromEncryptedPem(pem, pem, password);
                break;
            }
            default:
                throw new InvalidOperationException(
                    $"不支持的 Host 密钥格式：{extension}，请使用 .p12/.pfx 或 .pem。");
        }

        var rsa = certificate.GetRSAPrivateKey();
        if (rsa is not null)
        {
            return new Pkcs12HostKey(certificate, rsa);
        }

        certificate.Dispose();
        throw new InvalidOperationException($"Host 密钥 {path} 不包含 RSA 私钥。");
    }

    /// <inheritdoc />
    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            return _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }

    /// <inheritdoc />
    public byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        lock (_gate)
        {
            return _rsa.Decrypt(ciphertext, RSAEncryptionPadding.OaepSHA256);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _rsa.Dispose();
        _certificate.Dispose();
    }
}
