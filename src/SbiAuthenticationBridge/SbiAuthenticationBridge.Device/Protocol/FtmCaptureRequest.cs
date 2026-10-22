namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// CAPTURE(0x20/0x21) 请求
/// </summary>
/// <remarks>
/// 固定头 63 字节 + domainUri + transactionId + deviceId，字符串不带结尾 NUL。
/// </remarks>
public sealed record FtmCaptureRequest
{
    /// <summary>未指定期望质量分时的默认值</summary>
    public const int DefaultRequestedScore = 60;

    /// <summary>事务 ID，4..50 字节，只允许字母、数字与连字符</summary>
    public required string TransactionId { get; init; }

    /// <summary>设备 ID，取自设备身份 serialNo，长度 12 或 20 字节</summary>
    public required string DeviceId { get; init; }

    /// <summary>本次命令携带的 UTC</summary>
    public required DateTimeOffset Utc { get; init; }

    /// <summary>手指位置</summary>
    public required FtmFingerPosition FingerPosition { get; init; }

    /// <summary>采集环境</summary>
    public FtmEnvironment Environment { get; init; } = FtmEnvironment.Staging;

    /// <summary>本段采集次数，0..255；超过 9 时由固件返回数量不匹配错误</summary>
    public int BioCount { get; init; } = 1;

    /// <summary>设备子 ID，0..3；0 表示未指定</summary>
    public int DeviceSubId { get; init; }

    /// <summary>期望质量分，0..100</summary>
    public int RequestedScore { get; init; } = DefaultRequestedScore;

    /// <summary>域 URI，可打印 ASCII，0..192 字节</summary>
    public string DomainUri { get; init; } = "";

    /// <summary>上一帧 32 字节原始 SHA-256；null 表示首次采集</summary>
    public ReadOnlyMemory<byte>? PreviousHash { get; init; }
}