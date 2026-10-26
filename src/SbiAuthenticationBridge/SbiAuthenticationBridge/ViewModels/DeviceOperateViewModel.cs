using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prism.Events;
using SbiAuthenticationBridge.Core;
using SbiAuthenticationBridge.Core.Dto.Enum;
using SbiAuthenticationBridge.Core.Security;
using SbiAuthenticationBridge.Events;
using SbiAuthenticationBridge.Services;

namespace SbiAuthenticationBridge.ViewModels;

/// <summary>
/// 单设备操作视图模型；当前设备由全局设备选择决定
/// </summary>
public partial class DeviceOperateViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// 设备可接受的证书 DER 最大长度
    /// </summary>
    private const int MaxCertificateLength = 3500;

    /// <summary>
    /// 叶证书有效期
    /// </summary>
    private static readonly TimeSpan CertificateLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// 时钟容错，向前多给 5 分钟
    /// </summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private readonly IDeviceSelectionService _selectionService;

    private readonly ICertificateProvider _certificateProvider;

    private readonly Dkl1IssuerOptions _dkl1Options;

    private readonly IEventAggregator _eventAggregator;

    private readonly ILogger<DeviceOperateViewModel> _logger;

    /// <summary>
    /// 构造期间不触发选择变化处理
    /// </summary>
    private readonly bool _isInitializing = true;

    /// <summary>
    /// 创建设备操作视图模型
    /// </summary>
    /// <param name="selectionService">全局设备选择服务</param>
    /// <param name="certificateProvider">证书材料提供者</param>
    /// <param name="dkl1Options">DKL1 叶证书 Subject 与输出配置</param>
    /// <param name="eventAggregator">Prism 事件聚合器</param>
    /// <param name="logger">日志</param>
    public DeviceOperateViewModel(
        IDeviceSelectionService selectionService,
        ICertificateProvider certificateProvider,
        IOptions<Dkl1IssuerOptions> dkl1Options,
        IEventAggregator eventAggregator,
        ILogger<DeviceOperateViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(selectionService);
        ArgumentNullException.ThrowIfNull(certificateProvider);
        ArgumentNullException.ThrowIfNull(dkl1Options);
        ArgumentNullException.ThrowIfNull(eventAggregator);
        ArgumentNullException.ThrowIfNull(logger);

        _selectionService = selectionService;
        _certificateProvider = certificateProvider;
        _dkl1Options = dkl1Options.Value;
        _eventAggregator = eventAggregator;
        _logger = logger;
        IsKeyRotationConfigured = IsDkl1Configured(_dkl1Options);
        KeySubjectDescription = IsKeyRotationConfigured
            ? DescribeSubject(_dkl1Options)
            : "DKL1 签发材料未配置。";

        SelectedDevice = selectionService.SelectedDevice;
        SelectedDeviceId = selectionService.DeviceId;
        selectionService.SelectionChanged += OnSelectionChanged;
        _isInitializing = false;

        IsConnected = SelectedDevice is IDevice { IsConnected: true };
        SelectedStatusOption = StatusOptions[0];
    }

    /// <summary>
    /// 可设置的设备状态
    /// </summary>
    public IReadOnlyList<DeviceStatusOption> StatusOptions { get; } =
    [
        new(DeviceStatus.Ready, "就绪"),
        new(DeviceStatus.Busy, "忙"),
        new(DeviceStatus.NotReady, "未就绪"),
        new(DeviceStatus.NotRegistered, "未注册")
    ];

    /// <summary>
    /// 当前选择的设备；未选择时为 <see langword="null"/>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDevice))]
    public partial ISbiDevice? SelectedDevice { get; set; }

    /// <summary>
    /// 当前选择设备的内部标识；未选择时为 <see langword="null"/>
    /// </summary>
    [ObservableProperty]
    public partial string? SelectedDeviceId { get; set; }

    /// <summary>
    /// 是否已选择设备
    /// </summary>
    public bool HasSelectedDevice => SelectedDevice is not null;

    /// <summary>
    /// 叶证书 Subject，用于界面展示
    /// </summary>
    public string KeySubjectDescription { get; }

    /// <summary>
    /// 是否已配置 DKL1 签发材料
    /// </summary>
    public bool IsKeyRotationConfigured { get; }

    /// <summary>
    /// 是否正在执行操作
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// 设备是否已连接
    /// </summary>
    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    /// <summary>
    /// 最近一次错误
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// 设备当前状态
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStatusText))]
    public partial DeviceStatus? CurrentStatus { get; set; }

    /// <summary>
    /// 待设置的状态
    /// </summary>
    [ObservableProperty]
    public partial DeviceStatusOption? SelectedStatusOption { get; set; }

    /// <summary>
    /// 是否有错误信息
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>
    /// 当前状态文本
    /// </summary>
    public string CurrentStatusText => CurrentStatus switch
    {
        DeviceStatus.Ready => "就绪",
        DeviceStatus.Busy => "忙",
        DeviceStatus.NotReady => "未就绪",
        DeviceStatus.NotRegistered => "未注册",
        _ => "未知"
    };

    partial void OnSelectedDeviceChanged(ISbiDevice? value)
    {
        if (_isInitializing)
        {
            return;
        }

        HandleSelectedDeviceChanged(value);
    }

    /// <summary>
    /// 当前设备变化后刷新与该设备相关的状态
    /// </summary>
    private void HandleSelectedDeviceChanged(ISbiDevice? device)
    {
        CurrentStatus = null;
        ErrorMessage = null;
        IsConnected = device is IDevice { IsConnected: true };
        SelectedStatusOption = StatusOptions[0];

        NotifyCommandStates();

        _ = RefreshStateAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        _ = value;
        NotifyCommandStates();
    }

    partial void OnIsConnectedChanged(bool value)
    {
        _ = value;
        NotifyCommandStates();
    }

    partial void OnSelectedStatusOptionChanged(DeviceStatusOption? value)
    {
        _ = value;
        SetStatusCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 刷新连接状态与设备状态
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshStateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        var deviceId = SelectedDeviceId;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await LoadDeviceStateAsync(device);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            _logger.LogWarning(exception, "刷新设备状态失败：{DeviceId}", deviceId);
        }
        finally
        {
            IsBusy = false;
            SynchronizeConnectionState(deviceId);
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        var selectedDevice = SelectedDevice;
        var selectedDeviceId = SelectedDeviceId;

        if (selectedDevice is not IDevice device)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        _logger.LogDebug(
            "开始连接设备：{DeviceId}，类型={DeviceType}",
            selectedDeviceId,
            device.GetType().Name);

        try
        {
            await device.ConnectAsync();

            _logger.LogInformation(
                "设备已连接：{DeviceId}，类型={DeviceType}",
                selectedDeviceId,
                device.GetType().Name);

            if (IsStillSelected(selectedDeviceId))
            {
                IsConnected = device.IsConnected;
                await LoadDeviceStateAsync(selectedDevice);
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            if (device.IsConnected)
            {
                _logger.LogWarning(exception, "设备已连接，但读取状态失败：{DeviceId}", selectedDeviceId);
            }
            else
            {
                _logger.LogError(exception, "连接设备失败：{DeviceId}", selectedDeviceId);
            }
        }
        finally
        {
            IsBusy = false;
            SynchronizeConnectionState(selectedDeviceId);
            PublishConnectionChanged(selectedDeviceId, device.IsConnected);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        if (SelectedDevice is not IDevice device)
        {
            return;
        }

        var selectedDeviceId = SelectedDeviceId;

        IsBusy = true;
        ErrorMessage = null;

        _logger.LogDebug("开始断开设备：{DeviceId}", selectedDeviceId);

        try
        {
            await device.DisconnectAsync();

            _logger.LogInformation("设备已断开：{DeviceId}", selectedDeviceId);

            if (IsStillSelected(selectedDeviceId))
            {
                IsConnected = device.IsConnected;
                CurrentStatus = DeviceStatus.NotReady;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            _logger.LogError(exception, "断开设备失败：{DeviceId}", selectedDeviceId);
        }
        finally
        {
            IsBusy = false;
            SynchronizeConnectionState(selectedDeviceId);
            PublishConnectionChanged(selectedDeviceId, device.IsConnected);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSetStatus))]
    private async Task SetStatusAsync()
    {
        if (SelectedDevice is not IManualDeviceStatus statusControl
            || SelectedStatusOption is not { } statusOption)
        {
            return;
        }

        var selectedDeviceId = SelectedDeviceId;

        IsBusy = true;
        ErrorMessage = null;

        _logger.LogDebug(
            "开始设置设备状态：{DeviceId} -> {Status}",
            selectedDeviceId,
            statusOption.Value);

        try
        {
            await statusControl.SetDeviceStatusAsync(statusOption.Value);

            _logger.LogInformation(
                "设备状态已设置：{DeviceId} -> {Status}",
                selectedDeviceId,
                statusOption.Value);

            if (IsStillSelected(selectedDeviceId))
            {
                CurrentStatus = statusOption.Value;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            _logger.LogError(
                exception,
                "设置设备状态失败：{DeviceId} -> {Status}",
                selectedDeviceId,
                statusOption.Value);
        }
        finally
        {
            IsBusy = false;
            SynchronizeConnectionState(selectedDeviceId);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRotateKey))]
    private async Task RotateKeyAsync()
    {
        if (SelectedDevice is not IFtmSecureDevice secureDevice)
        {
            return;
        }

        var selectedDeviceId = SelectedDeviceId;

        IsBusy = true;
        ErrorMessage = null;

        _logger.LogDebug("开始轮换设备密钥：{DeviceId}", selectedDeviceId);

        try
        {
            var publicKey = await secureDevice.GetDkl1PublicKeyAsync();
            var certificateDer = CreateDkl1Certificate(publicKey.SubjectPublicKeyInfo);
            await secureDevice.SetDkl1CertificateAsync(certificateDer);

            _logger.LogInformation(
                "设备密钥轮换完成：{DeviceId}，Subject={Subject}",
                selectedDeviceId,
                KeySubjectDescription);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            _logger.LogError(exception, "设备密钥轮换失败：{DeviceId}", selectedDeviceId);
        }
        finally
        {
            IsBusy = false;
            SynchronizeConnectionState(selectedDeviceId);
        }
    }

    private bool CanRefresh() => !IsBusy && HasSelectedDevice;

    private bool CanConnect() => !IsBusy && SelectedDevice is IDevice { IsConnected: false };

    private bool CanDisconnect() => !IsBusy && SelectedDevice is IDevice { IsConnected: true };

    private bool CanSetStatus() =>
        !IsBusy
        && IsDeviceUsable
        && SelectedStatusOption is not null
        && SelectedDevice is IManualDeviceStatus;

    private bool CanRotateKey() =>
        !IsBusy
        && IsDeviceUsable
        && IsKeyRotationConfigured
        && SelectedDevice is IFtmSecureDevice;

    /// <summary>
    /// 对不提供物理连接状态的服务型设备，视为可用
    /// </summary>
    private bool IsDeviceUsable =>
        SelectedDevice is not IDevice physicalDevice || physicalDevice.IsConnected;

    /// <summary>
    /// 操作结束后按设备实际状态刷新连接显示，避免失败后界面仍停留在“已连接”
    /// </summary>
    /// <param name="deviceId">本次操作开始时的设备内部标识</param>
    private void SynchronizeConnectionState(string? deviceId)
    {
        if (!IsStillSelected(deviceId) || SelectedDevice is not IDevice physicalDevice)
        {
            return;
        }

        IsConnected = physicalDevice.IsConnected;
        if (!IsConnected)
        {
            CurrentStatus = DeviceStatus.NotReady;
        }
    }

    private async Task LoadDeviceStateAsync(ISbiDevice device)
    {
        var deviceId = SelectedDeviceId;

        if (device is IDevice physicalDevice)
        {
            IsConnected = physicalDevice.IsConnected;
        }

        if (device is not IManualDeviceStatus statusControl)
        {
            return;
        }

        var status = await statusControl.GetDeviceStatusAsync();
        if (!IsStillSelected(deviceId))
        {
            return;
        }

        CurrentStatus = status;

        foreach (var option in StatusOptions)
        {
            if (option.Value == status)
            {
                SelectedStatusOption = option;
                break;
            }
        }
    }

    /// <summary>
    /// 使用 Device Provider 私钥为设备公钥签发 DKL1 叶证书
    /// </summary>
    private ReadOnlyMemory<byte> CreateDkl1Certificate(ReadOnlyMemory<byte> subjectPublicKeyInfo)
    {
        if (subjectPublicKeyInfo.IsEmpty)
        {
            throw new ArgumentException("设备公钥不能为空。", nameof(subjectPublicKeyInfo));
        }

        using var deviceKey = ImportDeviceKey(subjectPublicKeyInfo.Span);

        var notBefore = DateTimeOffset.UtcNow - ClockSkew;
        using var certificate = CertificateSigner.Sign(
            _certificateProvider.ProviderCertificate,
            _certificateProvider.ProviderPrivateKey,
            deviceKey,
            CreateSubjectName(),
            notBefore,
            notBefore + CertificateLifetime);

        var certificateDer = certificate.Export(X509ContentType.Cert);
        if (certificateDer.Length > MaxCertificateLength)
        {
            throw new InvalidOperationException(
                $"签发结果 {certificateDer.Length} 字节，超过设备允许的 {MaxCertificateLength} 字节。");
        }

        SaveIssuedCertificate(certificateDer);
        return certificateDer;
    }

    private X500DistinguishedName CreateSubjectName()
    {
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCountryOrRegion("CN");
        subject.AddStateOrProvinceName("Guangdong");
        subject.AddLocalityName("Shenzhen");
        subject.AddOrganizationName(_dkl1Options.DeviceProvider);
        subject.AddOrganizationalUnitName($"{_dkl1Options.Make}-{_dkl1Options.Model}");
        subject.AddCommonName(_dkl1Options.SerialNo);
        return subject.Build();
    }

    /// <summary>
    /// 导入设备返回的 SubjectPublicKeyInfo，并校验 DKL1 要求的 RSA-2048
    /// </summary>
    private static RSA ImportDeviceKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        var rsa = RSA.Create();

        try
        {
            rsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
            if (bytesRead != subjectPublicKeyInfo.Length)
            {
                throw new InvalidOperationException("设备公钥包含多余的尾部数据。");
            }
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidOperationException)
        {
            rsa.Dispose();
            throw new InvalidOperationException("设备返回的 DKL1 公钥格式无效。", exception);
        }

        if (rsa.KeySize != 2048)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                $"设备 DKL1 公钥应为 RSA-2048，实际为 {rsa.KeySize} 位。");
        }

        return rsa;
    }

    private void SaveIssuedCertificate(byte[] certificateDer)
    {
        if (string.IsNullOrWhiteSpace(_dkl1Options.IssuedCertificatePath))
        {
            return;
        }

        var outputPath = ResolvePath(_dkl1Options.IssuedCertificatePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, certificateDer);
    }

    private static bool IsDkl1Configured(Dkl1IssuerOptions options) =>
        !string.IsNullOrWhiteSpace(options.SerialNo)
        && !string.IsNullOrWhiteSpace(options.Make)
        && !string.IsNullOrWhiteSpace(options.Model)
        && !string.IsNullOrWhiteSpace(options.DeviceProvider);

    private static string DescribeSubject(Dkl1IssuerOptions options) =>
        $"C=CN, ST=Guangdong, L=Shenzhen, O={options.DeviceProvider}, " +
        $"OU={options.Make}-{options.Model}, CN={options.SerialNo}";

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

    /// <summary>
    /// 通知设备列表刷新指定设备的连接状态
    /// </summary>
    /// <param name="deviceId">设备内部标识</param>
    /// <param name="isConnected">操作后的连接状态</param>
    private void PublishConnectionChanged(string? deviceId, bool isConnected)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return;
        }

        _eventAggregator
            .GetEvent<DeviceConnectionChangedEvent>()
            .Publish(new DeviceConnectionChangedEventArgs(deviceId, isConnected));
    }

    private void NotifyCommandStates()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        RefreshStateCommand.NotifyCanExecuteChanged();
        SetStatusCommand.NotifyCanExecuteChanged();
        RotateKeyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 异步操作开始后选择是否仍停留在同一设备；按内部标识判断，不依赖实例引用
    /// </summary>
    private bool IsStillSelected(string? deviceId) =>
        string.Equals(SelectedDeviceId, deviceId, StringComparison.Ordinal);

    private void OnSelectionChanged(object? sender, DeviceSelectionChangedEventArgs e)
    {
        var deviceChanged = !ReferenceEquals(SelectedDevice, e.SelectedDevice);
        var deviceIdChanged = !string.Equals(SelectedDeviceId, e.SelectedDeviceId,
            StringComparison.Ordinal);

        SelectedDevice = e.SelectedDevice;
        SelectedDeviceId = e.SelectedDeviceId;

        // 实例未变但内部标识变化时 SelectedDevice 不会触发属性变更，需显式刷新
        if (!deviceChanged && deviceIdChanged)
        {
            HandleSelectedDeviceChanged(SelectedDevice);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _selectionService.SelectionChanged -= OnSelectionChanged;
        GC.SuppressFinalize(this);
    }

}