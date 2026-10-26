using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// 认证用途的 SBI 服务实现
/// </summary>
internal sealed class SbiAuthenticationServiceHandle : SbiServiceHandleBase, ISbiAuthenticationServiceHandle
{
    /// <summary>
    /// 创建认证服务句柄
    /// </summary>
    /// <param name="deviceProvider">设备提供者</param>
    /// <param name="hostContext">宿主上下文</param>
    /// <param name="loggerFactory">日志工厂</param>
    public SbiAuthenticationServiceHandle(
        ISbiDeviceProvider deviceProvider,
        ISbiHostContext hostContext,
        ILoggerFactory loggerFactory)
        : base(deviceProvider, hostContext, loggerFactory)
    {
    }

    /// <inheritdoc />
    protected override DevicePurpose Purpose => DevicePurpose.Auth;

    /// <inheritdoc />
    protected override bool IsSupported(ISbiDevice device) => device is ISbiAuthenticationDevice;

    /// <inheritdoc />
    public Task<CaptureResponse> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CaptureCoreAsync(
            "CAPTURE",
            request,
            GetDevices(),
            static (device, subRequest, token) =>
                ((ISbiAuthenticationDevice)device).CaptureAsync(subRequest, token),
            cancellationToken);
    }
}
