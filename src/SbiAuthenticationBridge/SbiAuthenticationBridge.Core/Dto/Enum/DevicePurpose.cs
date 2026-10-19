using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto.Enum;

/// <summary>
/// 设备在 MOSIP 中的用途，对应 deviceInfo.purpose
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DevicePurpose
{
    /// <summary>
    /// 认证（报文取值 "Auth"）
    /// </summary>
    [JsonStringEnumMemberName("Auth")] Auth,

    /// <summary>
    /// 注册（报文取值 "Registration"）
    /// </summary>
    [JsonStringEnumMemberName("Registration")]
    Registration
}