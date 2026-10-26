using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// 按 SBI 请求中的设备标识选择目标设备
/// </summary>
internal static class SbiDeviceSelector
{
    /// <summary>
    /// 从候选设备中选择目标设备
    /// </summary>
    /// <typeparam name="TDevice">设备接口类型</typeparam>
    /// <param name="candidates">候选设备，按稳定顺序排列</param>
    /// <param name="deviceId">请求指定的设备 ID；可为空</param>
    /// <param name="biometricType">请求的生物特征类型；可为空</param>
    /// <param name="deviceSubId">请求指定的设备子 ID；可为空</param>
    /// <param name="logger">日志器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的设备；无匹配时为 <see langword="null"/></returns>
    public static async Task<TDevice?> SelectAsync<TDevice>(
        IReadOnlyList<TDevice> candidates,
        string? deviceId,
        string? biometricType,
        int? deviceSubId,
        ILogger logger,
        CancellationToken cancellationToken)
        where TDevice : class, ISbiDevice
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        // 单设备无需探测：直接交给设备校验请求中的设备标识与类型
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var descriptors = new List<(TDevice Device, IReadOnlyList<DiscoveryResponse> Responses)>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var responses = await candidate
                    .DiscoverAsync(new DiscoveryRequest { Type = biometricType }, cancellationToken)
                    .ConfigureAwait(false);

                descriptors.Add((candidate, responses));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SbiException exception)
            {
                logger.LogDebug(
                    exception,
                    "设备发现返回业务错误，跳过候选：{Device}",
                    candidate.GetType().Name);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "设备发现异常，跳过候选：{Device}",
                    candidate.GetType().Name);
            }
        }

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            foreach (var descriptor in descriptors)
            {
                if (descriptor.Responses.Any(response => MatchesDeviceId(response, deviceId)))
                {
                    return descriptor.Device;
                }
            }

            return null;
        }

        if (!string.IsNullOrWhiteSpace(biometricType))
        {
            var supported = descriptors
                .Where(static descriptor => descriptor.Responses.Any(static response => response.Error is null))
                .ToList();

            if (deviceSubId is not null)
            {
                foreach (var descriptor in supported)
                {
                    if (descriptor.Responses.Any(response =>
                            response.Error is null
                            && response.DeviceSubId?.Contains(deviceSubId.Value) == true))
                    {
                        return descriptor.Device;
                    }
                }
            }

            return supported.Count > 0 ? supported[0].Device : null;
        }

        return candidates[0];
    }

    private static bool MatchesDeviceId(DiscoveryResponse response, string deviceId) =>
        response.Error is null
        && (string.Equals(response.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(response.DeviceCode, deviceId, StringComparison.OrdinalIgnoreCase));
}
