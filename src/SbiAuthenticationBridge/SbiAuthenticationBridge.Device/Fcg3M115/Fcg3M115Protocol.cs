using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SbiAuthenticationBridge.Device.Fcg3M115;

/// <summary>
/// FCG-3M115 安全采图报文与加解密
/// </summary>
/// <remarks>
/// 实现《FCG-3M115 上位机安全采图与解密协议 V1.0》：336 字节 Host 签名请求，
/// 124104 字节 Module 签名响应，RSA-OAEP-SHA256 解包 AES 密钥，AES-256-CTR 解密图像。
/// </remarks>
public static class Fcg3M115Protocol
{
    /// <summary>请求长度</summary>
    public const int RequestLength = 336;

    /// <summary>响应长度</summary>
    public const int ResponseLength = 124_104;

    /// <summary>响应 Header 长度</summary>
    public const int HeaderLength = 64;

    /// <summary>Module 证书槽长度</summary>
    public const int CertificateSlotLength = 3500;

    /// <summary>图像明文长度（300×400 GRAY8）</summary>
    public const int ImageLength = 120_000;

    /// <summary>图像宽度</summary>
    public const int Width = 300;

    /// <summary>图像高度</summary>
    public const int Height = 400;

    /// <summary>Module 证书 Common Name</summary>
    public const string ModuleCommonName = "FCG-3M115 Fingerprint Module";

    /// <summary>Host 证书 Common Name</summary>
    public const string HostCommonName = "FCG-3M115 Android Host";

    private const int WrappedKeyOffset = 3564;

    private const int IvOffset = 3820;

    private const int ImageOffset = 3832;

    private const int ReservedOffset = 123832;

    private const int SignatureOffset = 123848;

    private const int CertificateOffset = 64;

    private static ReadOnlySpan<byte> RequestDomain => "FCG3M115-CAP-REQ-V1"u8;

    private static ReadOnlySpan<byte> ResponseDomain => "FCG3M115-CAP-RSP-V1"u8;

    /// <summary>
    /// 构造一次安全采图请求
    /// </summary>
    /// <param name="hostKey">Host 密钥</param>
    /// <returns>336 字节请求及其 requestId/nonce</returns>
    public static Fcg3M115SecureRequest CreateRequest(IFcg3M115HostKey hostKey)
    {
        ArgumentNullException.ThrowIfNull(hostKey);

        var request = new byte[RequestLength];
        var requestId = BinaryPrimitives.ReadUInt32BigEndian(RandomNumberGenerator.GetBytes(4));
        var nonce = RandomNumberGenerator.GetBytes(32);

        "FCR1"u8.CopyTo(request);
        request[4] = 1;
        request[5] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8), requestId);
        nonce.CopyTo(request.AsSpan(12));
        SHA256.HashData(hostKey.CertificateDer.Span).CopyTo(request.AsSpan(44));
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(76), Width);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(78), Height);

        var signingInput = new byte[RequestDomain.Length + 80];
        RequestDomain.CopyTo(signingInput);
        request.AsSpan(0, 80).CopyTo(signingInput.AsSpan(RequestDomain.Length));

        var signature = hostKey.Sign(signingInput);
        if (signature.Length != 256)
        {
            throw new Fcg3M115Exception($"Host 签名长度应为 256 字节，实际为 {signature.Length} 字节。");
        }

        signature.CopyTo(request.AsSpan(80));

        return new Fcg3M115SecureRequest(request, requestId, nonce);
    }

    /// <summary>
    /// 构造仅用于固件协议探测的 336 字节请求（签名槽保持为零）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该请求没有有效 Host 签名，模组会拒绝采集；但模组返回的响应格式
    /// 可以区分旧版 JSON 固件与 FCG-3M115 固件。
    /// </para>
    /// <para>
    /// 调用前应先下发 Host 证书；否则模组可能直接拒绝该命令。
    /// </para>
    /// </remarks>
    /// <param name="hostCertificateDer">Host 证书 DER，用于填写请求中的证书摘要</param>
    /// <returns>336 字节探测请求</returns>
    public static Fcg3M115SecureRequest CreateProbeRequest(ReadOnlySpan<byte> hostCertificateDer)
    {
        var request = new byte[RequestLength];
        var requestId = BinaryPrimitives.ReadUInt32BigEndian(RandomNumberGenerator.GetBytes(4));
        var nonce = RandomNumberGenerator.GetBytes(32);

        "FCR1"u8.CopyTo(request);
        request[4] = 1;
        request[5] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8), requestId);
        nonce.CopyTo(request.AsSpan(12));
        SHA256.HashData(hostCertificateDer).CopyTo(request.AsSpan(44));
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(76), Width);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(78), Height);

        // 80..335 为 Host 签名槽，探测请求保持全零。

        return new Fcg3M115SecureRequest(request, requestId, nonce);
    }

    /// <summary>
    /// 验证并解密一次 124104 字节安全采图响应
    /// </summary>
    /// <param name="response">完整响应</param>
    /// <param name="hostKey">Host 密钥（解包 AES 会话密钥）</param>
    /// <param name="rootCertificate">预置的 FCG-3M115 Root CA</param>
    /// <param name="request">对应的请求</param>
    /// <returns>120000 字节 GRAY8 原图</returns>
    /// <exception cref="Fcg3M115Exception">响应结构、证书、签名或解密校验失败</exception>
    public static byte[] VerifyAndDecrypt(ReadOnlySpan<byte> response, IFcg3M115HostKey hostKey,
        X509Certificate2 rootCertificate, Fcg3M115SecureRequest request)
    {
        ArgumentNullException.ThrowIfNull(hostKey);
        ArgumentNullException.ThrowIfNull(rootCertificate);
        ArgumentNullException.ThrowIfNull(request);

        if (response.Length != ResponseLength)
        {
            throw new Fcg3M115Exception(
                $"响应长度应为 {ResponseLength} 字节，实际为 {response.Length} 字节。");
        }

        var header = response[..HeaderLength];

        if (!header[..4].SequenceEqual("FCP1"u8))
        {
            throw new Fcg3M115Exception("响应 Magic 不是 FCP1。");
        }

        Ensure(header[4] == 1 && header[5] == 2, "响应版本或消息类型不正确。");
        Ensure(BinaryPrimitives.ReadUInt16BigEndian(header[6..]) == 0x0007,
            "响应 flags 不是 0x0007。");
        Ensure(BinaryPrimitives.ReadUInt32BigEndian(header[8..]) == ResponseLength,
            "响应 totalLength 不正确。");
        Ensure(BinaryPrimitives.ReadUInt32BigEndian(header[12..]) == request.RequestId,
            "响应 requestId 与请求不匹配。");
        Ensure(CryptographicOperations.FixedTimeEquals(header.Slice(16, 32), request.Nonce),
            "响应 nonce 与请求不匹配。");
        Ensure(BinaryPrimitives.ReadUInt16BigEndian(header[48..]) == Width
               && BinaryPrimitives.ReadUInt16BigEndian(header[50..]) == Height,
            "响应图像尺寸不是 300×400。");
        Ensure(header[52] == 1 && header[53] == 0, "响应像素格式或保留字节不正确。");
        Ensure(BinaryPrimitives.ReadUInt32BigEndian(header[56..]) == ImageLength,
            "响应 plaintextLength 不是 120000。");
        Ensure(BinaryPrimitives.ReadUInt16BigEndian(header[60..]) == CertificateSlotLength,
            "响应 certificateSlotLength 不是 3500。");
        Ensure(BinaryPrimitives.ReadUInt16BigEndian(header[62..]) == HeaderLength,
            "响应 headerLength 不是 64。");

        var certificateLength = BinaryPrimitives.ReadUInt16BigEndian(header[54..]);
        Ensure(certificateLength is >= 1 and <= CertificateSlotLength,
            "响应 Module 证书长度不合法。");

        var certificateSlot = response.Slice(CertificateOffset, CertificateSlotLength);
        Ensure(!certificateSlot[certificateLength..].ContainsAnyExcept((byte)0),
            "响应 Module 证书槽的未使用部分不为零。");

        using var moduleCertificate =
            X509CertificateLoader.LoadCertificate(certificateSlot[..certificateLength]);

        VerifyLeaf(rootCertificate, moduleCertificate, ModuleCommonName,
            requireServerAuth: true, requireKeyEncipherment: false);

        using var moduleKey = moduleCertificate.GetRSAPublicKey()
                              ?? throw new Fcg3M115Exception("Module 证书不含 RSA 公钥。");

        var signedData = new byte[ResponseDomain.Length + SignatureOffset];
        ResponseDomain.CopyTo(signedData);
        response[..SignatureOffset].CopyTo(signedData.AsSpan(ResponseDomain.Length));

        var signature = response.Slice(SignatureOffset, ResponseLength - SignatureOffset);
        Ensure(moduleKey.VerifyData(signedData, signature, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1),
            "Module 响应签名验证失败。");

        Ensure(!response.Slice(ReservedOffset, 16).ContainsAnyExcept((byte)0),
            "响应保留槽不为零。");

        var aesKey = hostKey.Decrypt(response.Slice(WrappedKeyOffset, 256));

        try
        {
            if (aesKey.Length != 32)
            {
                throw new Fcg3M115Exception(
                    $"解包得到的 AES 密钥长度应为 32 字节，实际为 {aesKey.Length} 字节。");
            }

            return AesCtrCrypt(aesKey, response.Slice(IvOffset, 12),
                response.Slice(ImageOffset, ImageLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    /// <summary>
    /// 验证叶证书：链、Common Name、有效期、BasicConstraints、KeyUsage、EKU 与 RSA 参数
    /// </summary>
    /// <param name="root">预置的 Root CA</param>
    /// <param name="leaf">待验证叶证书</param>
    /// <param name="expectedCommonName">叶证书 Common Name</param>
    /// <param name="requireServerAuth">true 检查 serverAuth，false 检查 clientAuth</param>
    /// <param name="requireKeyEncipherment">是否要求 keyEncipherment</param>
    /// <exception cref="Fcg3M115Exception">证书不符合要求</exception>
    public static void VerifyLeaf(X509Certificate2 root, X509Certificate2 leaf,
        string expectedCommonName, bool requireServerAuth, bool requireKeyEncipherment)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(leaf);

        using (var chain = new X509Chain())
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.DisableCertificateDownloads = true;

            if (!chain.Build(leaf))
            {
                throw new Fcg3M115Exception("证书链验证失败。");
            }
        }

        Ensure(leaf.GetNameInfo(X509NameType.SimpleName, false) == expectedCommonName,
            $"证书 Common Name 不是 {expectedCommonName}。");

        var basicConstraints = leaf.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        Ensure(basicConstraints is { CertificateAuthority: false }, "证书不能是 CA。");

        var keyUsage = leaf.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        Ensure(keyUsage is not null
               && (keyUsage.KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0,
            "证书缺少 digitalSignature 用途。");

        if (requireKeyEncipherment)
        {
            Ensure((keyUsage!.KeyUsages & X509KeyUsageFlags.KeyEncipherment) != 0,
                "Host 证书缺少 keyEncipherment 用途。");
        }

        var enhancedKeyUsage = leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        var requiredOid = requireServerAuth
            ? "1.3.6.1.5.5.7.3.1" // serverAuth
            : "1.3.6.1.5.5.7.3.2"; // clientAuth

        Ensure(enhancedKeyUsage is not null
               && enhancedKeyUsage.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == requiredOid),
            $"证书缺少 {(requireServerAuth ? "serverAuth" : "clientAuth")} EKU。");

        using var rsa = leaf.GetRSAPublicKey();
        Ensure(rsa is { KeySize: 2048 }, "证书公钥不是 RSA-2048。");

        var parameters = rsa!.ExportParameters(false);
        Ensure(parameters.Exponent is [0x01, 0x00, 0x01], "证书公钥指数不是 65537。");
    }

    /// <summary>
    /// 读取内置的 FCG-3M115 Root CA DER
    /// </summary>
    public static byte[] LoadEmbeddedRootCertificateDer() =>
        ReadEmbedded("SbiAuthenticationBridge.Device.fcg3m115_root_ca.cert.der");

    /// <summary>
    /// 读取内置的 FCG-3M115 Host 证书 DER（用于对照，不代替 P12）
    /// </summary>
    public static byte[] LoadEmbeddedHostCertificateDer() =>
        ReadEmbedded("SbiAuthenticationBridge.Device.fcg3m115_host.cert.der");

    private static byte[] ReadEmbedded(string name)
    {
        using var stream = typeof(Fcg3M115Protocol).Assembly.GetManifestResourceStream(name)
                           ?? throw new Fcg3M115Exception($"程序集缺少内嵌资源 {name}。");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// FCG-3M115 V1 CTR：首个计数器块为 IV || 0x00000002，末尾 32 位按大端递增
    /// </summary>
    private static byte[] AesCtrCrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv,
        ReadOnlySpan<byte> data)
    {
        var output = new byte[data.Length];
        var counterBlock = new byte[16];
        var keyStream = new byte[16];

        iv.CopyTo(counterBlock);
        counterBlock[15] = 2;

        using var aes = Aes.Create();
        aes.Key = [.. key];
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var encryptor = aes.CreateEncryptor();

        for (var offset = 0; offset < data.Length; offset += 16)
        {
            encryptor.TransformBlock(counterBlock, 0, 16, keyStream, 0);

            var blockLength = Math.Min(16, data.Length - offset);
            for (var index = 0; index < blockLength; index++)
            {
                output[offset + index] = (byte)(data[offset + index] ^ keyStream[index]);
            }

            IncrementCounter(counterBlock);
        }

        CryptographicOperations.ZeroMemory(counterBlock);
        CryptographicOperations.ZeroMemory(keyStream);
        return output;
    }

    private static void IncrementCounter(Span<byte> counter)
    {
        for (var index = 15; index >= 12; index--)
        {
            if (++counter[index] != 0)
            {
                return;
            }
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new Fcg3M115Exception(message);
        }
    }
}

/// <summary>
/// 一次 FCG-3M115 安全采图请求
/// </summary>
/// <param name="Bytes">336 字节请求</param>
/// <param name="RequestId">请求 ID</param>
/// <param name="Nonce">32 字节随机数</param>
public sealed record Fcg3M115SecureRequest(byte[] Bytes, uint RequestId, byte[] Nonce);
