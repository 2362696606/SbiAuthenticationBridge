using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto.Enum;

/// <summary>
/// 生物特征类型，对应 digitalId.type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BiometricType
{
    /// <summary>
    /// 指纹
    /// </summary>
    [JsonStringEnumMemberName("Finger")] Finger,

    /// <summary>
    /// 虹膜
    /// </summary>
    [JsonStringEnumMemberName("Iris")] Iris,

    /// <summary>
    /// 人脸
    /// </summary>
    [JsonStringEnumMemberName("Face")] Face
}