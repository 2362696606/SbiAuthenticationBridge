using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Device.Protocol;
using Serilog;

namespace SbiAuthenticationBridge.Device;

/// <summary>
/// FAP20 的 SBI 设备句柄基类：负责设备发现与设备信息查询
/// </summary>
/// <remarks>
/// 该类只处理 SBI 角色层职责，不包含 USB/FTM 协议实现；具体采集能力由派生句柄提供。
/// </remarks>
public class Fap20SbiDevice : ISbiDevice
{
    private const string SuccessErrorCode = "0";

    private const string DeviceStatusBusy = "Busy";

    private const string DeviceStatusNotReady = "Not Ready";

    private const string DeviceStatusNotRegistered = "Not Registered";

    private static readonly ILogger Logger = Log.ForContext("SourceContext", "FAP20");

    private string? _lastDeviceId;

    /// <summary>
    /// 创建 FAP20 的 SBI 设备句柄
    /// </summary>
    /// <param name="device">FAP20 设备实例</param>
    /// <param name="callbackId">
    /// SBI 设备服务回调地址，须为 1..192 字节可打印 ASCII
    /// </param>
    public Fap20SbiDevice(FAP20 device, string callbackId)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrEmpty(callbackId);

        Device = device;
        CallbackId = callbackId;
    }

    /// <summary>
    /// FAP20 设备实例
    /// </summary>
    protected FAP20 Device { get; }

    /// <summary>
    /// SBI 设备服务回调地址
    /// </summary>
    protected string CallbackId { get; }

    /// <summary>
    /// 最近一次发现得到的设备代码（serialNo）
    /// </summary>
    protected string? DeviceCode { get; private set; }

    /// <summary>
    /// 设备发现：请求类型为指纹设备或任意生物识别设备时应答，未指定类型时按任意类型处理
    /// </summary>
    /// <param name="request">发现请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>
    /// 本设备的一条发现结果；设备物理不在线时返回空列表。
    /// 设备已连接但命令失败时返回一条携带标准 SBI 错误的记录。
    /// </returns>
    public async Task<IReadOnlyList<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!CanAnswerDiscovery(request.Type))
        {
            return [];
        }

        // 物理不在线的设备不应答发现请求，由服务层过滤掉
        if (!await TryConnectAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        try
        {
            var response = await Device.DiscoverAsync(CallbackId, FtmDeviceType.Finger,
                FAP20.DefaultTimeoutMs, cancellationToken).ConfigureAwait(false);

            _lastDeviceId = response.DeviceId;
            DeviceCode = string.IsNullOrEmpty(response.DeviceCode) ? DeviceCode : response.DeviceCode;
            EnsureDiscoveryError(response);
            return [response];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (FtmCommandException exception) when (exception.ResultCode == FAP20.DeviceBusyResultCode)
        {
            Logger.Warning(exception, "SBI 设备发现时设备忙");
            return
            [
                new DiscoveryResponse
                {
                    DeviceId = _lastDeviceId,
                    DeviceStatus = DeviceStatusBusy,
                    Error = SbiErrorHelper.DeviceBusy
                }
            ];
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "SBI 设备发现失败");
            return
            [
                new DiscoveryResponse
                {
                    DeviceId = _lastDeviceId,
                    DeviceStatus = DeviceStatusNotReady,
                    Error = SbiErrorHelper.DeviceNotReady
                }
            ];
        }
    }

    /// <summary>
    /// 获取设备信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>本设备的一条设备信息结果；失败时返回携带标准 SBI 错误的记录。</returns>
    public async Task<IReadOnlyList<DeviceInfoResponse>> GetDeviceInfoAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await TryConnectAsync(cancellationToken).ConfigureAwait(false))
        {
            return [new DeviceInfoResponse { Error = SbiErrorHelper.DeviceNotReady }];
        }

        try
        {
            var response = await Device.GetDeviceInfoAsync(CallbackId, FAP20.DefaultTimeoutMs,
                cancellationToken).ConfigureAwait(false);

            return [response];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (FtmCommandException exception) when (exception.ResultCode == FAP20.DeviceBusyResultCode)
        {
            Logger.Warning(exception, "SBI 设备信息查询时设备忙");
            return [new DeviceInfoResponse { Error = SbiErrorHelper.DeviceBusy }];
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "SBI 设备信息查询失败");
            return [new DeviceInfoResponse { Error = SbiErrorHelper.DeviceNotReady }];
        }
    }

    /// <summary>
    /// 为 SBI 操作准备设备连接；物理不在线或打开失败时返回 false
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备是否已连接</returns>
    protected async Task<bool> TryConnectAsync(CancellationToken cancellationToken)
    {
        if (Device.IsConnected)
        {
            return true;
        }

        try
        {
            await Device.ConnectAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return Device.IsConnected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "SBI 操作前连接设备失败");
            return false;
        }
    }

    /// <summary>
    /// 确保已取得设备的 serialNo；必要时先执行一次发现
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备代码；无法取得时返回 null</returns>
    protected async Task<string?> EnsureDeviceCodeAsync(CancellationToken cancellationToken)
    {
        // 重新连接后设备可能已更换，不能沿用上一次的 serialNo
        if (!Device.IsConnected)
        {
            DeviceCode = null;
        }

        if (!string.IsNullOrEmpty(DeviceCode))
        {
            return DeviceCode;
        }

        var responses = await DiscoverAsync(new DiscoveryRequest { Type = "Finger" }, cancellationToken)
            .ConfigureAwait(false);

        foreach (var response in responses)
        {
            if (string.IsNullOrEmpty(response.DeviceCode))
            {
                continue;
            }

            DeviceCode = response.DeviceCode;
            return DeviceCode;
        }

        return null;
    }

    /// <summary>
    /// 判断本设备是否应应答指定类型的发现请求
    /// </summary>
    private static bool CanAnswerDiscovery(string? type)
    {
        var normalized = type?.Trim();

        return string.IsNullOrEmpty(normalized)
               || string.Equals(normalized, "Finger", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Biometric Device", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 设备只返回状态、未返回错误对象时，按 SBI 错误码表补齐状态错误
    /// </summary>
    private static void EnsureDiscoveryError(DiscoveryResponse response)
    {
        if (!string.IsNullOrEmpty(response.Error?.ErrorCode)
            && response.Error.ErrorCode != SuccessErrorCode)
        {
            return;
        }

        response.Error = response.DeviceStatus switch
        {
            DeviceStatusBusy => SbiErrorHelper.DeviceBusy,
            DeviceStatusNotReady => SbiErrorHelper.DeviceNotReady,
            DeviceStatusNotRegistered => SbiErrorHelper.DeviceNotRegistered,
            _ => response.Error ?? SbiErrorHelper.Success
        };
    }
}
