using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SbiAuthenticationBridge.ViewModels;

namespace SbiAuthenticationBridge.Views;

public partial class DeviceView : UserControl
{
    public DeviceView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 更换密钥：真机需要先选择 DKL 证书，模拟设备直接生成新密钥
    /// </summary>
    private async void OnRotateKeyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeviceViewModel { CanRotateKey: true } viewModel)
        {
            return;
        }

        try
        {
            ReadOnlyMemory<byte>? certificateDer = null;

            if (viewModel.RequiresKeyCertificate)
            {
                certificateDer = await PickCertificateAsync();

                // 用户取消选择
                if (certificateDer is null)
                {
                    return;
                }
            }

            await viewModel.RotateDeviceKeyAsync(certificateDer);
        }
        catch (Exception exception)
        {
            // 事件处理器不能把异常抛回 UI 线程，否则会终止进程
            viewModel.ErrorMessage = exception.Message;
        }
    }

    /// <summary>
    /// 打开文件选择器并读取证书
    /// </summary>
    /// <returns>证书 DER；用户取消时返回 <c>null</c></returns>
    private async Task<ReadOnlyMemory<byte>?> PickCertificateAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            throw new InvalidOperationException("当前平台不支持选择证书文件。");
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 DKL 证书",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("X.509 证书")
                {
                    Patterns = ["*.der", "*.cer", "*.crt", "*.pem"],
                    MimeTypes = ["application/x-x509-ca-cert", "application/pkix-cert"]
                }
            ]
        });

        if (files.Count == 0)
        {
            return null;
        }

        await using var stream = await files[0].OpenReadAsync();
        return CertificateFile.LoadDer(stream);
    }
}
