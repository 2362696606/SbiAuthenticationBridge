using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SbiAuthenticationBridge;

/// <summary>
/// 读取 X.509 证书文件（DER 或 PEM）并统一转换为 DER
/// </summary>
internal static class CertificateFile
{
    /// <summary>
    /// 读取并校验证书文件
    /// </summary>
    /// <param name="stream">证书文件流</param>
    /// <returns>DER 编码的证书</returns>
    /// <exception cref="InvalidDataException">文件为空或不是可识别的 X.509 证书</exception>
    public static byte[] LoadDer(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var data = buffer.ToArray();

        if (data.Length == 0)
        {
            throw new InvalidDataException("证书文件为空。");
        }

        try
        {
            var text = Encoding.ASCII.GetString(data);
            using var certificate = IsPem(text)
                ? X509Certificate2.CreateFromPem(text)
                : X509CertificateLoader.LoadCertificate(data);

            return certificate.Export(X509ContentType.Cert);
        }
        catch (Exception exception) when (exception is CryptographicException
                                              or ArgumentException
                                              or FormatException)
        {
            throw new InvalidDataException(
                "无法识别的证书文件，请选择 DER 或 PEM 编码的 X.509 证书。", exception);
        }
    }

    /// <summary>
    /// 判断文本是否为 PEM 编码
    /// </summary>
    private static bool IsPem(string text) =>
        text.TrimStart('﻿', ' ', '\t', '\r', '\n')
            .StartsWith("-----BEGIN", StringComparison.Ordinal);
}
