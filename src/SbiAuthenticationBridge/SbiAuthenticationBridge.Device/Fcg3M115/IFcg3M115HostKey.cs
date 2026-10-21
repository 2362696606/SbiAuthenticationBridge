namespace SbiAuthenticationBridge.Device.Fcg3M115;

/// <summary>
/// FCG-3M115 Host 密钥：签名采图请求、解包模组返回的 AES 会话密钥
/// </summary>
/// <remarks>
/// 桌面从 P12/PEM 加载；Android 从 Keystore 别名 <c>fcg3m115_android_host</c> 读取，
/// 私钥不得进入 APK。
/// </remarks>
public interface IFcg3M115HostKey : IDisposable
{
    /// <summary>
    /// Host 证书 DER，上传给模组用于验证请求签名
    /// </summary>
    ReadOnlyMemory<byte> CertificateDer { get; }

    /// <summary>
    /// SHA256withRSA / RSASSA-PKCS1-v1_5 签名
    /// </summary>
    /// <param name="data">待签名数据</param>
    /// <returns>256 字节签名</returns>
    byte[] Sign(ReadOnlySpan<byte> data);

    /// <summary>
    /// RSA/ECB/OAEPWithSHA-256AndMGF1Padding 解密
    /// </summary>
    /// <param name="ciphertext">256 字节密文</param>
    /// <returns>明文（32 字节 AES 密钥）</returns>
    byte[] Decrypt(ReadOnlySpan<byte> ciphertext);
}

/// <summary>
/// FCG-3M115 协议错误
/// </summary>
public sealed class Fcg3M115Exception : Exception
{
    /// <summary>
    /// 创建协议错误
    /// </summary>
    public Fcg3M115Exception(string message) : base(message)
    {
    }

    /// <summary>
    /// 创建带内部异常的协议错误
    /// </summary>
    public Fcg3M115Exception(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
