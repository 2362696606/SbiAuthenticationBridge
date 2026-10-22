using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto;
using SbiAuthenticationBridge.Core.Dto.Enum;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 设备列表中的单个设备，承载用于区分设备的基础信息与当前状态
/// </summary>
public partial class DeviceItemViewModel : ViewModelBase
{
    /// <summary>
    /// 发现任意生物识别设备
    /// </summary>
    private const string AnyBiometricDevice = "Biometric Device";

    private static readonly IBrush ReadyBrush = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly IBrush BusyBrush = new SolidColorBrush(Color.Parse("#EF6C00"));
    private static readonly IBrush NotReadyBrush = new SolidColorBrush(Color.Parse("#C62828"));
    private static readonly IBrush NotRegisteredBrush = new SolidColorBrush(Color.Parse("#757575"));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse("#757575"));

    /// <summary>
    /// 尚无法从设备取得标识时使用的名称
    /// </summary>
    private readonly string _fallbackName;

    /// <summary>
    /// 创建设备列表项
    /// </summary>
    /// <param name="device">底层设备</param>
    /// <param name="ordinal">在列表中的序号，用于标识尚未返回信息的设备</param>
    public DeviceItemViewModel(ISbiDevice device, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(device);

        Device = device;
        _fallbackName = $"{KindText} {ordinal}";
        DisplayName = _fallbackName;

        if (device is IDevice physicalDevice)
        {
            IsConnected = physicalDevice.IsConnected;
        }
    }

    /// <summary>
    /// 底层设备
    /// </summary>
    public ISbiDevice Device { get; }

    /// <summary>
    /// 设备类型，按实现的 SBI 接口区分
    /// </summary>
    public string KindText => Device switch
    {
        ISbiAuthenticationDevice => "认证设备",
        ISbiRegistrationDevice => "注册设备",
        _ => "设备服务"
    };

    /// <summary>
    /// 用于区分设备的名称：优先设备代码，其次设备 ID
    /// </summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; }

    /// <summary>
    /// 设备服务内部的设备 ID
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceIdText))]
    public partial string? DeviceId { get; set; }

    /// <summary>
    /// 设备代码，与数字身份中的 serialNo 相同
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceCodeText))]
    public partial string? DeviceCode { get; set; }

    /// <summary>
    /// 认证等级
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CertificationText))]
    public partial string? Certification { get; set; }

    /// <summary>
    /// 设备服务版本
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceVersionText))]
    public partial string? ServiceVersion { get; set; }

    /// <summary>
    /// 设备在 MOSIP 中的用途
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PurposeText))]
    public partial string? Purpose { get; set; }

    /// <summary>
    /// 当前设备状态
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    public partial DeviceStatus? Status { get; set; }

    /// <summary>
    /// 物理连接状态
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    public partial bool IsConnected { get; set; }

    /// <summary>
    /// 设备返回的错误信息
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorInfo { get; set; }

    /// <summary>
    /// 是否正在刷新
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// 设备是否提供物理连接状态
    /// </summary>
    public bool HasConnectionState => Device is IDevice;

    /// <summary>
    /// 连接状态文本
    /// </summary>
    public string ConnectionText => IsConnected ? "已连接" : "未连接";

    /// <summary>
    /// 是否显示错误信息
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorInfo);

    /// <summary>
    /// 设备状态文本
    /// </summary>
    public string StatusText => Status switch
    {
        DeviceStatus.Ready => "就绪",
        DeviceStatus.Busy => "忙",
        DeviceStatus.NotReady => "未就绪",
        DeviceStatus.NotRegistered => "未注册",
        _ => "未知"
    };

    /// <summary>
    /// 设备状态对应的颜色
    /// </summary>
    public IBrush StatusBrush => Status switch
    {
        DeviceStatus.Ready => ReadyBrush,
        DeviceStatus.Busy => BusyBrush,
        DeviceStatus.NotReady => NotReadyBrush,
        DeviceStatus.NotRegistered => NotRegisteredBrush,
        _ => UnknownBrush
    };

    /// <summary>
    /// 设备 ID 展示文本
    /// </summary>
    public string DeviceIdText => $"设备 ID：{ValueOrDash(DeviceId)}";

    /// <summary>
    /// 设备代码展示文本
    /// </summary>
    public string DeviceCodeText => $"设备代码：{ValueOrDash(DeviceCode)}";

    /// <summary>
    /// 认证等级展示文本
    /// </summary>
    public string CertificationText => $"认证：{ValueOrDash(Certification)}";

    /// <summary>
    /// 服务版本展示文本
    /// </summary>
    public string ServiceVersionText => $"服务版本：{ValueOrDash(ServiceVersion)}";

    /// <summary>
    /// 用途展示文本
    /// </summary>
    public string PurposeText => $"用途：{Purpose switch
    {
        "Auth" => "认证",
        "Registration" => "注册",
        _ => ValueOrDash(Purpose)
    }}";

    /// <summary>
    /// 刷新设备基础信息与当前状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorInfo = null;

        try
        {
            if (Device is IDevice physicalDevice)
            {
                IsConnected = physicalDevice.IsConnected;

                // 未连接时无法向固件查询，直接按 SBI 约定标记为未就绪
                if (!IsConnected)
                {
                    Status = DeviceStatus.NotReady;
                    return;
                }
            }

            var responses = await Device.DiscoverAsync(
                new DiscoveryRequest { Type = AnyBiometricDevice }, cancellationToken);

            var response = responses.Count > 0 ? responses[0] : null;
            if (response is null)
            {
                Status = null;
                ErrorInfo = "设备未返回发现响应。";
                return;
            }

            Apply(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Status = null;
            ErrorInfo = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply(DiscoveryResponse response)
    {
        DeviceId = response.DeviceId;
        DeviceCode = response.DeviceCode;
        Certification = response.Certification;
        ServiceVersion = response.ServiceVersion;
        Purpose = response.Purpose;
        Status = ParseStatus(response.DeviceStatus);
        ErrorInfo = response.Error is null ? null : DescribeError(response.Error);

        var identity = !string.IsNullOrWhiteSpace(response.DeviceCode)
            ? response.DeviceCode
            : !string.IsNullOrWhiteSpace(response.DeviceId)
                ? response.DeviceId
                : null;

        DisplayName = identity ?? _fallbackName;
    }

    private static DeviceStatus? ParseStatus(string? status) => status switch
    {
        "Ready" => DeviceStatus.Ready,
        "Busy" => DeviceStatus.Busy,
        "Not Ready" => DeviceStatus.NotReady,
        "Not Registered" => DeviceStatus.NotRegistered,
        _ => null
    };

    private static string DescribeError(SbiError error)
    {
        var message = string.IsNullOrWhiteSpace(error.ErrorInfo)
            ? "设备返回错误。"
            : error.ErrorInfo;

        return string.IsNullOrWhiteSpace(error.ErrorCode)
            ? message
            : $"错误 {error.ErrorCode}：{message}";
    }

    private static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
