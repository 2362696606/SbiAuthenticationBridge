namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// FTM 协议错误：请求字段不合规或响应不符合约定
/// </summary>
public sealed class FtmProtocolException : Exception
{
    /// <summary>创建协议错误</summary>
    /// <param name="message">说明哪一步、哪个字段对不上</param>
    public FtmProtocolException(string message) : base(message)
    {
    }
}