namespace SbiAuthenticationBridge.Core.Security;

/// <summary>
/// DKL1 叶证书输出配置
/// </summary>
/// <remarks>
/// 叶证书 Subject 由设备 digitalId 与 Device Provider 证书动态生成，不在配置中维护。
/// </remarks>
public sealed class Dkl1IssuerOptions
{
    /// <summary>
    /// 签发后的叶证书输出路径；为空时不保存
    /// </summary>
    public string? IssuedCertificatePath { get; init; }
}
