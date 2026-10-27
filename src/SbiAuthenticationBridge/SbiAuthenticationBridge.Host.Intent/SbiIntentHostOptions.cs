namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// Intent 宿主配置
/// </summary>
public sealed class SbiIntentHostOptions
{
    /// <summary>
    /// Android appId（包名）；同时作为 DISCOVER/DEVICE_INFO 的 callbackId
    /// </summary>
    public string AppId { get; set; } = string.Empty;
}
