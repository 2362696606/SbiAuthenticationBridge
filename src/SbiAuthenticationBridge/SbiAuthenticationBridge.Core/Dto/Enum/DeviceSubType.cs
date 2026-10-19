using System.Text.Json.Serialization;

namespace SbiAuthenticationBridge.Core.Dto.Enum;

/// <summary>
/// 设备子类型，对应 digitalId.deviceSubType
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceSubType
{
    /// <summary>
    /// 指纹：按压式扫描设备，常用于注册
    /// </summary>
    [JsonStringEnumMemberName("Slap")] Slap,

    /// <summary>
    /// 单点扫描设备：指纹为单指、虹膜为单眼，常用于认证
    /// </summary>
    [JsonStringEnumMemberName("Single")] Single,

    /// <summary>
    /// 指纹：非接触式采集设备
    /// </summary>
    [JsonStringEnumMemberName("Touchless")]
    Touchless,

    /// <summary>
    /// 虹膜：双眼扫描设备，常用于注册
    /// </summary>
    Double,

    /// <summary>
    /// 人脸：全脸采集设备
    /// </summary>
    FullFace
}