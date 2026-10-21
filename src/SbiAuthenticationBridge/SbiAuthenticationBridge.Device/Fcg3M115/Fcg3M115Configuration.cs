using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;

namespace SbiAuthenticationBridge.Device.Fcg3M115;

/// <summary>
/// FCG-3M115 运行配置：Host 私钥、Root CA 与 SBI 应用加密证书
/// </summary>
/// <remarks>
/// <para>
/// 桌面默认从环境变量读取：<c>FCG3M115_HOST_P12</c>、
/// <c>FCG3M115_HOST_P12_PASSWORD</c>、<c>FCG3M115_ROOT_CA</c>、
/// <c>FCG3M115_APP_CERT</c>。
/// </para>
/// <para>
/// Android 由宿主设置 <see cref="HostKeyFactory"/>（Keystore）与
/// <see cref="ApplicationCertificateFactory"/>（应用私有目录中的 SBI 应用公钥证书）。
/// Root CA 默认使用程序集内嵌的 <c>fcg3m115_root_ca.cert.der</c>。
/// </para>
/// </remarks>
public static class Fcg3M115Configuration
{
    private static readonly Lock Gate = new();

    private static IFcg3M115HostKey? _hostKey;

    private static X509Certificate2? _rootCertificate;

    private static X509Certificate2? _applicationCertificate;

    /// <summary>
    /// Host 私钥工厂；Android 在启动时设置
    /// </summary>
    public static Func<IFcg3M115HostKey?>? HostKeyFactory { get; set; }

    /// <summary>
    /// Root CA 工厂；未设置时读取 <c>FCG3M115_ROOT_CA</c>，否则使用内嵌 Root
    /// </summary>
    public static Func<X509Certificate2?>? RootCertificateFactory { get; set; }

    /// <summary>
    /// SBI 应用加密证书工厂；未设置时读取 <c>FCG3M115_APP_CERT</c>
    /// </summary>
    public static Func<X509Certificate2?>? ApplicationCertificateFactory { get; set; }

    /// <summary>
    /// 获取 Host 私钥
    /// </summary>
    /// <exception cref="InvalidOperationException">未配置或加载失败</exception>
    public static IFcg3M115HostKey GetHostKey()
    {
        lock (Gate)
        {
            if (_hostKey is not null)
            {
                return _hostKey;
            }

            _hostKey = HostKeyFactory?.Invoke()
                       ?? Pkcs12HostKey.TryLoadFromEnvironment()
                       ?? throw new InvalidOperationException(
                           "未配置 FCG-3M115 Host 私钥：桌面请设置 FCG3M115_HOST_P12；" +
                           "Android 请把 P12 导入 Keystore 别名 fcg3m115_android_host，" +
                           "或放置 FilesDir/fcg3m115_host.p12（密码文件 fcg3m115_host.password）。");

            return _hostKey;
        }
    }

    /// <summary>
    /// 尝试获取 Host 私钥；未配置时返回 <c>false</c>，不抛出异常
    /// </summary>
    /// <remarks>
    /// 旧固件不支持 FCG-3M115 时不会配置 Host 私钥，调用方据此回退到旧版采集协议。
    /// </remarks>
    /// <param name="hostKey">Host 私钥；未配置时为 <c>null</c></param>
    public static bool TryGetHostKey([NotNullWhen(true)] out IFcg3M115HostKey? hostKey)
    {
        try
        {
            hostKey = GetHostKey();
            return true;
        }
        catch (InvalidOperationException)
        {
            hostKey = null;
            return false;
        }
    }

    /// <summary>
    /// 获取 FCG-3M115 Root CA
    /// </summary>
    public static X509Certificate2 GetRootCertificate()
    {
        lock (Gate)
        {
            if (_rootCertificate is not null)
            {
                return _rootCertificate;
            }

            var path = Environment.GetEnvironmentVariable("FCG3M115_ROOT_CA");

            _rootCertificate = RootCertificateFactory?.Invoke()
                               ?? (string.IsNullOrWhiteSpace(path)
                                   ? X509CertificateLoader.LoadCertificate(
                                       Fcg3M115Protocol.LoadEmbeddedRootCertificateDer())
                                   : LoadCertificate(path));

            return _rootCertificate;
        }
    }

    /// <summary>
    /// 获取 SBI 应用加密证书（用于加密 bioValue 会话密钥）
    /// </summary>
    /// <exception cref="InvalidOperationException">未配置应用证书</exception>
    public static X509Certificate2 GetApplicationCertificate()
    {
        lock (Gate)
        {
            if (_applicationCertificate is not null)
            {
                return _applicationCertificate;
            }

            _applicationCertificate = ApplicationCertificateFactory?.Invoke()
                                      ?? LoadApplicationCertificateFromEnvironment()
                                      ?? throw new InvalidOperationException(
                                          "未配置 SBI 应用加密证书：设置 FCG3M115_APP_CERT，" +
                                          "Android 请把 fcg3m115_app.cert.der 放入应用 FilesDir。");

            return _applicationCertificate;
        }
    }

    /// <summary>
    /// 清除已缓存的密钥与证书，供配置变更或测试使用
    /// </summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _hostKey?.Dispose();
            _hostKey = null;

            _rootCertificate?.Dispose();
            _rootCertificate = null;

            _applicationCertificate?.Dispose();
            _applicationCertificate = null;
        }
    }

    /// <summary>
    /// 读取 DER 或 PEM 编码的 X.509 证书
    /// </summary>
    public static X509Certificate2 LoadCertificate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.GetExtension(path).Equals(".pem", StringComparison.OrdinalIgnoreCase)
            ? X509Certificate2.CreateFromPemFile(path)
            : X509CertificateLoader.LoadCertificateFromFile(path);
    }

    private static X509Certificate2? LoadApplicationCertificateFromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("FCG3M115_APP_CERT");
        return string.IsNullOrWhiteSpace(path) ? null : LoadCertificate(path);
    }
}
