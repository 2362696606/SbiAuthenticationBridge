namespace SbiAuthenticationBridge.Device.Protocol;

/// <summary>
/// GET_DKL1_PUBLIC_KEY 的响应结果
/// </summary>
public sealed record FtmDklPublicKeyResult
{
    /// <summary>操作结果</summary>
    public required FtmDklResult Result { get; init; }

    /// <summary>公钥；仅当结果为 <see cref="FtmDklResult.Ok"/> 时非空</summary>
    public FtmDklPublicKey? Key { get; init; }
}