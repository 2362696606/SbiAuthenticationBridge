namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// DKL 操作结果
/// </summary>
public enum FtmDklResult
{
    /// <summary>操作成功</summary>
    Ok = 0,

    /// <summary>rotationId、DER 或证书策略不合法</summary>
    InvalidData = 1,

    /// <summary>设备忙，可稍后重试同一请求</summary>
    Busy = 2,

    /// <summary>密钥生成、Flash 或内部恢复失败</summary>
    RotateFailed = 3
}