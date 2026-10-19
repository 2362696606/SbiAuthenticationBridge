namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// CAPTURE 请求中的手指位置
/// </summary>
public enum FtmFingerPosition
{
    /// <summary>未指定</summary>
    Unknown = 0,

    /// <summary>右手拇指</summary>
    RightThumb = 1,

    /// <summary>右手食指</summary>
    RightIndexFinger = 2,

    /// <summary>右手中指</summary>
    RightMiddleFinger = 3,

    /// <summary>右手无名指</summary>
    RightRingFinger = 4,

    /// <summary>右手小指</summary>
    RightLittleFinger = 5,

    /// <summary>左手拇指</summary>
    LeftThumb = 6,

    /// <summary>左手食指</summary>
    LeftIndexFinger = 7,

    /// <summary>左手中指</summary>
    LeftMiddleFinger = 8,

    /// <summary>左手无名指</summary>
    LeftRingFinger = 9,

    /// <summary>左手小指</summary>
    LeftLittleFinger = 10
}