using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SbiAuthenticationBridge.Core.Security;

/// <summary>
/// 使用签发者私钥为设备公钥签发叶证书
/// </summary>
public static class CertificateSigner
{
    /// <summary>
    /// 签发叶证书
    /// </summary>
    /// <param name="issuerCertificate">签发者证书，提供 Issuer 名称与 Subject Key Identifier</param>
    /// <param name="issuerPrivateKey">签发者私钥</param>
    /// <param name="subjectPublicKey">待签发证书的公钥</param>
    /// <param name="subjectName">证书 Subject</param>
    /// <param name="notBefore">证书生效时间</param>
    /// <param name="notAfter">证书失效时间</param>
    /// <returns>签名后的叶证书</returns>
    public static X509Certificate2 Sign(
        X509Certificate2 issuerCertificate,
        RSA issuerPrivateKey,
        RSA subjectPublicKey,
        X500DistinguishedName subjectName,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        ArgumentNullException.ThrowIfNull(issuerCertificate);
        ArgumentNullException.ThrowIfNull(issuerPrivateKey);
        ArgumentNullException.ThrowIfNull(subjectPublicKey);
        ArgumentNullException.ThrowIfNull(subjectName);

        if (notAfter <= notBefore)
        {
            throw new ArgumentException("证书失效时间必须晚于生效时间。", nameof(notAfter));
        }

        var request = new CertificateRequest(
            subjectName, subjectPublicKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(new PublicKey(subjectPublicKey), false));

        try
        {
            request.CertificateExtensions.Add(
                X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                    issuerCertificate, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "签发者证书缺少 Subject Key Identifier，无法生成 AKI 扩展。", exception);
        }

        var signatureGenerator = X509SignatureGenerator.CreateForRSA(
            issuerPrivateKey, RSASignaturePadding.Pkcs1);

        return request.Create(
            issuerCertificate.SubjectName,
            signatureGenerator,
            notBefore,
            notAfter,
            CreateSerialNumber());
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
}
