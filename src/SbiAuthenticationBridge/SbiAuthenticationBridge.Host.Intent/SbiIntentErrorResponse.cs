using System.Text.Json;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// 按 action 生成对应报文结构的 SBI 错误响应
/// </summary>
public static class SbiIntentErrorResponse
{
    /// <summary>
    /// 采集响应使用的 SBI 规范版本
    /// </summary>
    private const string SbiSpecVersion = "0.9.5";

    /// <summary>
    /// 生成错误响应：发现/信息返回错误数组，采集返回 biometrics 错误条目
    /// </summary>
    /// <param name="action">Intent action</param>
    /// <param name="appId">Intent 宿主 appId</param>
    /// <param name="error">错误对象</param>
    /// <returns>内联响应结果</returns>
    public static SbiIntentResult Create(string action, string appId, SbiError error)
    {
        if (action == SbiIntentProtocol.DiscoveryAction)
        {
            return SbiIntentResult.Inline(Serialize(
                new List<DiscoveryResponse> { new() { Error = error } }));
        }

        if (action == SbiIntentProtocol.InfoAction(appId))
        {
            return SbiIntentResult.Inline(Serialize(
                new List<DeviceInfoResponse> { new() { Error = error } }));
        }

        return SbiIntentResult.Inline(Serialize(new CaptureResponse
        {
            Biometrics =
            [
                new BiometricInfo
                {
                    SpecVersion = SbiSpecVersion,
                    Data = "",
                    Hash = "",
                    Error = error,
                },
            ],
        }));
    }

    private static byte[] Serialize<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, SbiIntentProtocol.JsonOptions);
}
