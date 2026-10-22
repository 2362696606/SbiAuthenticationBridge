using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Core;

/// <summary>
/// 面向服务面的 SBI 公共接口：接受外部（注册客户端、认证服务等）的设备发现与信息查询调用，
/// 汇总所管理的全部设备后，按 SBI 报文返回整体响应。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ISbiDevice"/> 的"单设备返回"不同，本接口面向的是对外暴露的 SBI 服务：
/// 实现方持有一个或多个设备句柄，负责把一次外部调用分发到这些设备，并合并各设备的响应。
/// </para>
/// <para>
/// 单个设备的失败不应使整体调用失败，而应以携带 <see cref="SbiError"/> 的记录出现在结果中；
/// 只有取消、实现方内部错误这类无法按 SBI 报文表达的情况才允许抛出异常。
/// 各记录中的错误对象统一通过 <see cref="SbiErrorHelper"/> 构造，不得手写错误码字符串。
/// </para>
/// </remarks>
public interface ISbiServiceHandle
{
    /// <summary>
    /// 设备发现（<c>/device</c>）
    /// </summary>
    /// <param name="request">发现请求，<see cref="DiscoveryRequest.Type"/> 用于筛选设备</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>
    /// 所有匹配设备的发现结果，每个设备一条；设备自身的失败同样以一条带
    /// <see cref="DiscoveryResponse.Error"/> 的记录返回。
    /// </returns>
    /// <remarks>
    /// 实现方应向每个已注册的设备句柄调用 <see cref="ISbiDevice.DiscoverAsync"/>，
    /// 过滤掉不应答的设备（类型不匹配或未返回结果），再将结果合并为列表。
    /// 返回顺序应保持稳定（例如按设备注册顺序），以便调用方对照。
    /// </remarks>
    Task<IReadOnlyList<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取设备信息（<c>/info</c>）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>
    /// 所有设备的信息，每个设备一条；设备自身的失败同样以一条带
    /// <see cref="DeviceInfoResponse.Error"/> 的记录返回。
    /// </returns>
    /// <remarks>
    /// 实现方应汇总每个设备句柄的 <see cref="ISbiDevice.GetDeviceInfoAsync"/> 结果。
    /// </remarks>
    Task<IReadOnlyList<DeviceInfoResponse>> GetDeviceInfoAsync(CancellationToken cancellationToken = default);
}
