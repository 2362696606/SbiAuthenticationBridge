using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 面向服务面的注册设备接口：设备发现、设备信息、注册采集（<c>/rcapture</c>）与预览流（<c>/stream</c>）
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ISbiAuthenticationServiceHandle"/> 的区别在于：注册采集输出明文生物特征、不做加密，
/// 且额外提供注册环境专用的预览流。实现方持有的设备句柄为 <see cref="ISbiRegistrationDevice"/>。
/// </para>
/// <para>
/// 设备发现与设备信息复用 <see cref="ISbiServiceHandle"/>；注册采集与预览流按设备面的
/// <see cref="ISbiRegistrationDevice"/> 一一对应。
/// </para>
/// </remarks>
public interface ISbiRegistrationServiceHandle : ISbiServiceHandle
{
    /// <summary>
    /// 注册采集（<c>/rcapture</c>）
    /// </summary>
    /// <param name="request">注册采集请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>
    /// 合并后的采集响应；<see cref="CaptureResponse.Biometrics"/> 与
    /// <see cref="CaptureRequest.Bio"/> 按顺序一一对应，请求了几项就返回几项。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 路由规则与合并规则同 <see cref="ISbiAuthenticationServiceHandle.CaptureAsync"/>，但只使用注册类设备：
    /// 按 <see cref="BioInfo.DeviceId"/>（若提供）、<see cref="BioInfo.Type"/>、
    /// <see cref="BioInfo.DeviceSubId"/> 把每个 bio 项分发到对应的 <see cref="ISbiRegistrationDevice"/>。
    /// </para>
    /// <para>
    /// 注册采集不加密，因此成功项不应携带 <see cref="BiometricInfo.SessionKey"/> 与
    /// <see cref="BiometricInfo.Thumbprint"/>；实现方也不得为凑齐字段而自行补写。
    /// </para>
    /// <para>
    /// 单个 bio 项失败或无设备可承接时，在对应位置返回一条携带 <see cref="BiometricInfo.Error"/>
    /// 的记录，不得静默丢弃该项。错误对象统一通过 <see cref="SbiErrorHelper"/> 构造，
    /// 不得手写错误码字符串。
    /// </para>
    /// </remarks>
    Task<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 打开注册设备预览流（<c>/stream</c>）
    /// </summary>
    /// <param name="request">
    /// 预览流请求；<see cref="StreamRequest.DeviceId"/> 为空时，由实现方选择一个可用的注册设备
    /// </param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>M-JPEG 视频流；由调用方负责释放</returns>
    /// <exception cref="SbiException">
    /// 无可用设备、设备未注册或设备报错时抛出，异常携带的 <see cref="SbiException.Error"/>
    /// 即应返回给调用方的 SBI 错误。
    /// </exception>
    /// <remarks>
    /// <para>
    /// 仅用途为 Registration 的设备支持该操作。实现方应把请求路由到目标设备的
    /// <see cref="ISbiRegistrationDevice.StreamAsync"/>，并把设备返回的流原样交给调用方。
    /// </para>
    /// <para>
    /// 与设备面一致，正常结果是视频流而不是 JSON。无法提供流时，实现方应抛出
    /// <see cref="SbiException"/>，其 <see cref="SbiException.Error"/> 通过
    /// <see cref="SbiErrorHelper"/> 构造；宿主层（Host）捕获后转换为
    /// <see cref="ErrorResponse"/> 返回调用方。
    /// </para>
    /// </remarks>
    Task<Stream> StreamAsync(StreamRequest request, CancellationToken cancellationToken = default);
}
