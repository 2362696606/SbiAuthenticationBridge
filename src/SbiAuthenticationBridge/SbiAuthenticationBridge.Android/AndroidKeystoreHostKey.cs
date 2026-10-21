using Java.Security;
using Java.Security.Spec;
using Javax.Crypto.Spec;
using SbiAuthenticationBridge.Device.Fcg3M115;
using System;

namespace SbiAuthenticationBridge.Android;

/// <summary>
/// 从 Android Keystore 读取 FCG-3M115 Host 私钥
/// </summary>
/// <remarks>
/// P12 必须通过受控流程导入 Keystore，别名为 <c>fcg3m115_android_host</c>；
/// 私钥不会进入 APK。签名与 OAEP 解密都由 Keystore 完成。
/// </remarks>
internal sealed class AndroidKeystoreHostKey : IFcg3M115HostKey
{
    /// <summary>
    /// 协议约定的 Keystore 别名
    /// </summary>
    internal const string Alias = "fcg3m115_android_host";

    private const string AndroidKeyStore = "AndroidKeyStore";

    private readonly KeyStore.PrivateKeyEntry _entry;

    private AndroidKeystoreHostKey(KeyStore.PrivateKeyEntry entry, byte[] certificateDer)
    {
        _entry = entry;
        CertificateDer = certificateDer;
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> CertificateDer { get; }

    /// <summary>
    /// 尝试从 Keystore 读取 Host 密钥
    /// </summary>
    /// <param name="alias">Keystore 别名</param>
    /// <returns>别名不存在或条目不完整时返回 <c>null</c></returns>
    internal static AndroidKeystoreHostKey? TryCreate(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        var keyStore = KeyStore.GetInstance(AndroidKeyStore)
                       ?? throw new InvalidOperationException("无法取得 AndroidKeyStore。");
        keyStore.Load(null, null);

        if (!keyStore.ContainsAlias(alias))
        {
            return null;
        }

        if (keyStore.GetEntry(alias, null) is not KeyStore.PrivateKeyEntry entry
            || entry.PrivateKey is null
            || entry.Certificate is not Java.Security.Cert.X509Certificate certificate
            || certificate.GetEncoded() is not { Length: > 0 } certificateDer)
        {
            return null;
        }

        return new AndroidKeystoreHostKey(entry, certificateDer);
    }

    /// <inheritdoc />
    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        using var signature = Signature.GetInstance("SHA256withRSA")
                              ?? throw new InvalidOperationException("无法创建 SHA256withRSA 签名器。");
        signature.InitSign(_entry.PrivateKey!);
        signature.Update([.. data]);

        return signature.Sign()
               ?? throw new InvalidOperationException("Keystore 未返回签名结果。");
    }

    /// <inheritdoc />
    public byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        using var cipher = Javax.Crypto.Cipher.GetInstance(
            "RSA/ECB/OAEPWithSHA-256AndMGF1Padding")
            ?? throw new InvalidOperationException("无法创建 RSA-OAEP 解密器。");

        var parameters = new OAEPParameterSpec("SHA-256", "MGF1",
            MGF1ParameterSpec.Sha256, PSource.PSpecified.Default);
        cipher.Init(Javax.Crypto.CipherMode.DecryptMode, _entry.PrivateKey!, parameters);

        return cipher.DoFinal([.. ciphertext])
               ?? throw new InvalidOperationException("Keystore 未返回解密结果。");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Keystore 条目由系统管理，无需释放
    }
}
