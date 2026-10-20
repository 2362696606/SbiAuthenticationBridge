using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 面向服务面的认证采集接口（<c>/capture</c>）
/// </summary>
/// <remarks>
/// <para>
/// 外部传入的认证采集请求中，<see cref="CaptureRequest.Bio"/> 可能包含多项，且各项可能指向不同设备。
/// 实现方按 <see cref="BioInfo.DeviceId"/>（若提供）、<see cref="BioInfo.Type"/>、
/// <see cref="BioInfo.DeviceSubId"/> 将每一项路由到对应的 <see cref="IAuthenticationDevice"/>，
/// 再把各设备的采集结果合并为一个 <see cref="CaptureResponse"/>。
/// </para>
/// <para>
/// 设备发现与设备信息由 <see cref="ISbiServiceHandle"/> 提供；本接口只负责认证采集，
/// 与设备面的 <see cref="IAuthenticationDevice"/> 相对应。
/// </para>
/// </remarks>
public interface IAuthenticationServiceHandle : ISbiServiceHandle
{
    /// <summary>
    /// 认证采集（<c>/capture</c>）
    /// </summary>
    /// <param name="request">认证采集请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>
    /// 合并后的采集响应；<see cref="CaptureResponse.Biometrics"/> 与
    /// <see cref="CaptureRequest.Bio"/> 按顺序一一对应，请求了几项就返回几项。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 多个 bio 项可并行分发到多个设备。单设备采集失败（如未放手指、超时、设备忙）不应使整体调用失败，
    /// 而应在对应位置返回一条携带 <see cref="BiometricInfo.Error"/> 的记录，错误码沿用 SBI 错误码表。
    /// </para>
    /// <para>
    /// 无设备可承接某个 bio 项时，同样在对应位置返回一条错误记录（不得静默丢弃该项），
    /// 以保证响应项数与请求项数一致。
    /// </para>
    /// <para>
    /// 错误对象统一通过 <see cref="SbiErrorHelper"/> 构造，不得手写错误码字符串。例如：
    /// 无匹配设备时用 <see cref="SbiErrorHelper.DeviceNotFound"/>，整机无设备时用
    /// <see cref="SbiErrorHelper.NoDeviceConnected"/>，请求枚数不支持时用
    /// <see cref="SbiErrorHelper.RequestedBiometricCountNotSupported"/>。
    /// </para>
    /// <para>
    /// 实现方只负责编排与合并，不得改写设备返回的 <see cref="BiometricInfo.Data"/>、
    /// <see cref="BiometricInfo.Hash"/>、<see cref="BiometricInfo.SessionKey"/>、
    /// <see cref="BiometricInfo.Thumbprint"/> 等已签名或已加密的内容。
    /// </para>
    /// </remarks>
    Task<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default);
}