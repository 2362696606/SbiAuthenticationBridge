using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto.Enum;

/// <summary>
/// 设备状态，对应 deviceInfo.deviceStatus
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceStatus
{
    /// <summary>
    /// 就绪，可接受请求（报文取值 "Ready"）
    /// </summary>
    [JsonStringEnumMemberName("Ready")] Ready,

    /// <summary>
    /// 忙，正在处理其他请求（报文取值 "Busy"）
    /// </summary>
    [JsonStringEnumMemberName("Busy")] Busy,

    /// <summary>
    /// 未就绪（报文取值 "Not Ready"，带空格）
    /// </summary>
    [JsonStringEnumMemberName("Not Ready")]
    NotReady,

    /// <summary>
    /// 未注册（报文取值 "Not Registered"，带空格）
    /// </summary>
    [JsonStringEnumMemberName("Not Registered")]
    NotRegistered
}