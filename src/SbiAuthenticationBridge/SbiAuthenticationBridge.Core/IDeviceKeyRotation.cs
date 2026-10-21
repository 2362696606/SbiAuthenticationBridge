using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 设备密钥轮换能力：供 CTK 的 Key Rotation 用例使用
/// </summary>
/// <remarks>
/// <para>
/// 真实设备通过下发新的 DKL 证书完成轮换，因此 <see cref="RequiresCertificate"/> 为 true；
/// 模拟设备可直接在本机生成新的 RSA 密钥与自签证书，无需外部证书。
/// </para>
/// <para>
/// 轮换后，设备输出的 SBI 签名数据（JWT 头部 <c>x5c</c> 与签名）应使用新密钥，
/// 使 CTK 在多次扫描之间能够观察到设备密钥确实发生变化。
/// </para>
/// </remarks>
public interface IDeviceKeyRotation
{
    /// <summary>
    /// 轮换是否需要调用方提供证书
    /// </summary>
    bool RequiresCertificate { get; }

    /// <summary>
    /// 读取当前设备密钥
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>当前设备密钥信息</returns>
    Task<DeviceKeyInfo> GetKeyInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 更换设备密钥
    /// </summary>
    /// <param name="certificateDer">
    /// 新的 DKL 证书（DER）。<see cref="RequiresCertificate"/> 为 true 时必填，否则忽略。
    /// </param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>轮换后的设备密钥信息</returns>
    Task<DeviceKeyInfo> RotateKeyAsync(ReadOnlyMemory<byte>? certificateDer = null,
        CancellationToken cancellationToken = default);
}
