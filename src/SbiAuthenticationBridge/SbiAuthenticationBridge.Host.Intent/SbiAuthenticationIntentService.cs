using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// 认证用途的 Intent 宿主服务
/// </summary>
internal sealed class SbiAuthenticationIntentService(
    ISbiHostContext hostContext,
    SbiIntentHostOptions options,
    ILoggerFactory loggerFactory)
    : SbiIntentHostServiceBase(hostContext, options, loggerFactory), ISbiAuthenticationService
{
    /// <inheritdoc />
    protected override DevicePurpose Purpose => DevicePurpose.Auth;

    /// <inheritdoc />
    protected override string ServiceName => "认证服务";
}
