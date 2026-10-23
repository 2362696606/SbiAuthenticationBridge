using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SbiAuthenticationBridge.Core.Security;

/// <summary>
/// 使用 Device Provider 证书与私钥签发 DKL1 叶证书
/// </summary>
/// <remarks>
/// 流程与参考实现一致：固件返回候选公钥后，本地签发一张 90 天有效期的叶证书，
/// 再由调用方通过 SET_DKL1_CERTIFICATE 安装到设备。私钥不离开本机。
/// </remarks>
public sealed class Dkl1CertificateIssuer : IDkl1CertificateIssuer
{
    /// <summary>
    /// 设备可接受的证书 DER 最大长度
    /// </summary>
    private const int MaxCertificateLength = 3500;

    /// <summary>
    /// 叶证书有效期
    /// </summary>
    private static readonly TimeSpan CertificateLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// 时钟容错，向前多给 5 分钟
    /// </summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private readonly Dkl1IssuerOptions _options;

    /// <summary>
    /// 创建签发器
    /// </summary>
    /// <param name="options">签发参数</param>
    public Dkl1CertificateIssuer(Dkl1IssuerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public string SubjectDescription =>
        $"C=CN, ST=Guangdong, L=Shenzhen, O={_options.DeviceProvider}, " +
        $"OU={_options.Make}-{_options.Model}, CN={_options.SerialNo}";

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Issue(ReadOnlyMemory<byte> subjectPublicKeyInfo)
    {
        if (subjectPublicKeyInfo.IsEmpty)
        {
            throw new ArgumentException("设备公钥不能为空。", nameof(subjectPublicKeyInfo));
        }

        using var providerCertificate = LoadProviderCertificate();
        using var providerKey = LoadProviderKey(providerCertificate);
        using var deviceKey = ImportDeviceKey(subjectPublicKeyInfo.Span);

        var request = CreateCertificateRequest(deviceKey, providerCertificate);

        var serialNumber = CreateSerialNumber();
        var notBefore = DateTimeOffset.UtcNow - ClockSkew;
        var signatureGenerator = X509SignatureGenerator.CreateForRSA(
            providerKey, RSASignaturePadding.Pkcs1);

        using var certificate = request.Create(
            providerCertificate.SubjectName,
            signatureGenerator,
            notBefore,
            notBefore + CertificateLifetime,
            serialNumber);

        var certificateDer = certificate.Export(X509ContentType.Cert);
        if (certificateDer.Length > MaxCertificateLength)
        {
            throw new InvalidOperationException(
                $"签发结果 {certificateDer.Length} 字节，超过设备允许的 {MaxCertificateLength} 字节。");
        }

        SaveIssuedCertificate(certificateDer);
        return certificateDer;
    }

    private X509Certificate2 LoadProviderCertificate()
    {
        var path = RequireFile(_options.ProviderCertificatePath, "Device Provider 证书");
        return X509CertificateLoader.LoadCertificateFromFile(path);
    }

    private RSA LoadProviderKey(X509Certificate2 providerCertificate)
    {
        var path = RequireFile(_options.ProviderPrivateKeyPath, "Device Provider 私钥");
        var rsa = RSA.Create();

        try
        {
            rsa.ImportFromEncryptedPem(File.ReadAllText(path), _options.ProviderPrivateKeyPassword);
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
            using var certificateKey = providerCertificate.GetRSAPublicKey()
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

    private static RSA ImportDeviceKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        var rsa = RSA.Create();

        try
        {
            rsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
            if (bytesRead != subjectPublicKeyInfo.Length)
            {
                throw new InvalidOperationException("设备公钥包含多余的尾部数据。");
            }
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidOperationException)
        {
            rsa.Dispose();
            throw new InvalidOperationException("设备返回的 DKL1 公钥格式无效。", exception);
        }

        if (rsa.KeySize != 2048)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                $"设备 DKL1 公钥应为 RSA-2048，实际为 {rsa.KeySize} 位。");
        }

        return rsa;
    }

    private CertificateRequest CreateCertificateRequest(
        RSA deviceKey, X509Certificate2 providerCertificate)
    {
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCountryOrRegion("CN");
        subject.AddStateOrProvinceName("Guangdong");
        subject.AddLocalityName("Shenzhen");
        subject.AddOrganizationName(_options.DeviceProvider);
        subject.AddOrganizationalUnitName($"{_options.Make}-{_options.Model}");
        subject.AddCommonName(_options.SerialNo);

        var request = new CertificateRequest(
            subject.Build(), deviceKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(new PublicKey(deviceKey), false));

        try
        {
            request.CertificateExtensions.Add(
                X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                    providerCertificate, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "Device Provider 证书缺少 Subject Key Identifier，无法生成 AKI 扩展。", exception);
        }

        return request;
    }

    private static byte[] CreateSerialNumber()
    {
        var serialNumber = new byte[8];

        do
        {
            RandomNumberGenerator.Fill(serialNumber);
            // 最高位清零，保证序列号为正数
            serialNumber[0] &= 0x7F;
        } while (Array.TrueForAll(serialNumber, static value => value == 0));

        return serialNumber;
    }

    private void SaveIssuedCertificate(byte[] certificateDer)
    {
        if (string.IsNullOrWhiteSpace(_options.IssuedCertificatePath))
        {
            return;
        }

        var outputPath = Path.GetFullPath(_options.IssuedCertificatePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, certificateDer);
    }

    private static string RequireFile(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{description}路径未配置。");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{description}不存在：{path}", path);
        }

        return path;
    }
}
