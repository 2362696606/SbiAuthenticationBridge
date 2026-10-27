using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.Host.Intent;

/// <summary>
/// 注册用途的 Intent 宿主服务
/// </summary>
internal sealed class SbiRegistrationIntentService(
    ISbiHostContext hostContext,
    SbiIntentHostOptions options,
    ILoggerFactory loggerFactory)
    : SbiIntentHostServiceBase(hostContext, options, loggerFactory), ISbiRegistrationService
{
    /// <inheritdoc />
    protected override DevicePurpose Purpose => DevicePurpose.Registration;

    /// <inheritdoc />
    protected override string ServiceName => "注册服务";
}
