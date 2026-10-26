using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Core.Dto.Enum;
using SbiAuthenticationBridge.Core.Security;

namespace SbiAuthenticationBridge.Host;

/// <summary>
/// 认证用途的 SBI 服务实现
/// </summary>
internal sealed class SbiAuthenticationServiceHandle : SbiServiceHandleBase, ISbiAuthenticationServiceHandle
{
    private readonly ICertificateProvider _certificateProvider;

    /// <summary>
    /// 已成功下发域证书的设备；按设备引用相等区分
    /// </summary>
    private readonly ConcurrentDictionary<ISbiDevice, byte> _certificateLoadedDevices =
        new(ReferenceEqualityComparer.Instance);

    private int _domainCertificateWarningLogged;

    /// <summary>
    /// 创建认证服务句柄
    /// </summary>
    /// <param name="deviceProvider">设备提供者</param>
    /// <param name="hostContext">宿主上下文</param>
    /// <param name="certificateProvider">本机证书材料</param>
    /// <param name="loggerFactory">日志工厂</param>
    public SbiAuthenticationServiceHandle(
        ISbiDeviceProvider deviceProvider,
        ISbiHostContext hostContext,
        ICertificateProvider certificateProvider,
        ILoggerFactory loggerFactory)
        : base(deviceProvider, hostContext, loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(certificateProvider);

        _certificateProvider = certificateProvider;
    }

    /// <inheritdoc />
    protected override DevicePurpose Purpose => DevicePurpose.Auth;

    /// <inheritdoc />
    protected override bool IsSupported(ISbiDevice device) => device is ISbiAuthenticationDevice;

    /// <inheritdoc />
    protected override async Task PrepareDeviceAsync(
        ISbiDevice device,
        CancellationToken cancellationToken)
    {
        if (device is not IFtmSecureDevice secureDevice
            || _certificateLoadedDevices.ContainsKey(device))
        {
            return;
        }

        // 每个设备会话只下发一次；设备复位或返回 108 时再补发
        await TryLoadDomainCertificateAsync(device, secureDevice, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 获取域加密证书；未配置或加载失败时记录日志并返回 <see langword="null"/>
    /// </summary>
    /// <returns>域加密证书，不可用时为 <see langword="null"/></returns>
    private X509Certificate2? GetDomainEncryptionCertificate()
    {
        try
        {
            var certificate = _certificateProvider.DomainEncryptionCertificate;
            if (certificate is not null)
            {
                return certificate;
            }

            if (Interlocked.Exchange(ref _domainCertificateWarningLogged, 1) == 0)
            {
                Logger.LogWarning(
                    "未配置域加密证书（CertificateProvider:domainEncryptionCertificatePath），采集将返回 108");
            }
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _domainCertificateWarningLogged, 1) == 0)
            {
                Logger.LogError(exception, "域加密证书加载失败，采集将返回 108");
            }
        }

        return null;
    }

    /// <summary>
    /// 向设备下发域证书；成功时记录该设备已加载
    /// </summary>
    /// <param name="device">目标设备</param>
    /// <param name="secureDevice">设备的证书能力</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>下发成功时为 <see langword="true"/></returns>
    private async Task<bool> TryLoadDomainCertificateAsync(
        ISbiDevice device,
        IFtmSecureDevice secureDevice,
        CancellationToken cancellationToken)
    {
        var certificate = GetDomainEncryptionCertificate();
        if (certificate is null)
        {
            return false;
        }

        try
        {
            await secureDevice
                .LoadDomainEncryptionCertificateAsync(certificate.RawData, cancellationToken)
                .ConfigureAwait(false);

            _certificateLoadedDevices[device] = 0;

            Logger.LogInformation(
                "已向设备 {Device} 加载域加密证书：thumbprint={Thumbprint}",
                device.GetType().Name,
                certificate.Thumbprint);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                exception,
                "向设备 {Device} 加载域加密证书失败",
                device.GetType().Name);

            return false;
        }
    }

    /// <summary>
    /// 采集单个设备；缺域证书时补发并重试一次
    /// </summary>
    /// <param name="device">已路由的设备</param>
    /// <param name="request">单 bio 采集请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备采集响应</returns>
    private async Task<CaptureResponse> CaptureDeviceAsync(
        ISbiDevice device,
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        var response = await ((ISbiAuthenticationDevice)device)
            .CaptureAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (device is not IFtmSecureDevice secureDevice
            || !IsDomainCertificateMissing(response))
        {
            return response;
        }

        Logger.LogWarning(
            "设备 {Device} 返回 108（缺域证书），重新下发后重试一次",
            device.GetType().Name);

        _certificateLoadedDevices.TryRemove(device, out _);

        if (!await TryLoadDomainCertificateAsync(device, secureDevice, cancellationToken)
                .ConfigureAwait(false))
        {
            return response;
        }

        return await ((ISbiAuthenticationDevice)device)
            .CaptureAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 判断响应是否因缺少域证书而失败
    /// </summary>
    /// <param name="response">采集响应</param>
    /// <returns>存在 108 错误时为 <see langword="true"/></returns>
    private static bool IsDomainCertificateMissing(CaptureResponse response) =>
        response.Biometrics?.Any(static biometric =>
            string.Equals(
                biometric.Error?.ErrorCode,
                SbiErrorHelper.DomainPublicKeyMissing.ErrorCode,
                StringComparison.Ordinal)) == true;

    /// <inheritdoc />
    public Task<CaptureResponse> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CaptureCoreAsync(
            "CAPTURE",
            request,
            GetDevices(),
            CaptureDeviceAsync,
            cancellationToken);
    }
}
