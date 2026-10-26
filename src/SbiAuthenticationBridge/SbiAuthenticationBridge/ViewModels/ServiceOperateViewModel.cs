using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 服务操作视图模型：认证与注册两个 SBI 服务的启停控制
/// </summary>
public class ServiceOperateViewModel : ViewModelBase
{
    /// <summary>
    /// 认证服务默认端口
    /// </summary>
    private const int DefaultAuthenticationPort = 4501;

    /// <summary>
    /// 注册服务默认端口
    /// </summary>
    private const int DefaultRegistrationPort = 4502;

    /// <summary>
    /// 创建服务操作视图模型
    /// </summary>
    /// <param name="authenticationServices">当前平台注册的认证服务；无实现时为空集合</param>
    /// <param name="registrationServices">当前平台注册的注册服务；无实现时为空集合</param>
    /// <param name="loggerFactory">日志工厂</param>
    public ServiceOperateViewModel(
        IEnumerable<ISbiAuthenticationService> authenticationServices,
        IEnumerable<ISbiRegistrationService> registrationServices,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(authenticationServices);
        ArgumentNullException.ThrowIfNull(registrationServices);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Authentication = new SbiServiceControlViewModel(
            "认证服务",
            authenticationServices.FirstOrDefault(),
            DefaultAuthenticationPort,
            loggerFactory);

        Registration = new SbiServiceControlViewModel(
            "注册服务",
            registrationServices.FirstOrDefault(),
            DefaultRegistrationPort,
            loggerFactory);
    }

    /// <summary>
    /// 认证服务控制
    /// </summary>
    public SbiServiceControlViewModel Authentication { get; }

    /// <summary>
    /// 注册服务控制
    /// </summary>
    public SbiServiceControlViewModel Registration { get; }

    /// <summary>
    /// 从服务读取当前运行状态与端口
    /// </summary>
    public void Refresh()
    {
        Authentication.Refresh();
        Registration.Refresh();
    }
}
