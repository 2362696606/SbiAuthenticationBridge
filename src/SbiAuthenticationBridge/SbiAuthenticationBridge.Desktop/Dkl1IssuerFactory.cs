using System;
using System.IO;
using SbiAuthenticationBridge.Core.Security;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SbiAuthenticationBridge.Desktop;

/// <summary>
/// 从 dkl1.yaml 创建 DKL1 证书签发器
/// </summary>
/// <remarks>
/// 配置文件缺失或内容不完整时返回占位实现，设备操作界面会显示具体原因，
/// 不影响设备枚举与连接等功能。
/// </remarks>
internal static class Dkl1IssuerFactory
{
    private const string ConfigFileName = "dkl1.yaml";

    /// <summary>
    /// 创建签发器
    /// </summary>
    public static IDkl1CertificateIssuer Create()
    {
        var configPath = FindConfigFile();
        if (configPath is null)
        {
            return new UnavailableDkl1CertificateIssuer($"未找到 {ConfigFileName}。");
        }

        try
        {
            var settings = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<Dkl1Settings?>(File.ReadAllText(configPath));

            if (settings is null)
            {
                return new UnavailableDkl1CertificateIssuer($"{ConfigFileName} 内容为空。");
            }

            var missing = settings.FindMissingField();
            if (missing is not null)
            {
                return new UnavailableDkl1CertificateIssuer(
                    $"{ConfigFileName} 缺少必填项：{missing}。");
            }

            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;

            return new Dkl1CertificateIssuer(new Dkl1IssuerOptions
            {
                ProviderCertificatePath = ResolvePath(baseDirectory, settings.ProviderCertificatePath!),
                ProviderPrivateKeyPath = ResolvePath(baseDirectory, settings.ProviderPrivateKeyPath!),
                ProviderPrivateKeyPassword = settings.ProviderPrivateKeyPassword ?? string.Empty,
                SerialNo = settings.SerialNo!,
                Make = settings.Make!,
                Model = settings.Model!,
                DeviceProvider = settings.DeviceProvider!,
                IssuedCertificatePath = string.IsNullOrWhiteSpace(settings.IssuedCertificatePath)
                    ? null
                    : ResolvePath(baseDirectory, settings.IssuedCertificatePath)
            });
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or YamlException or InvalidOperationException)
        {
            return new UnavailableDkl1CertificateIssuer(
                $"加载 {ConfigFileName} 失败：{exception.Message}");
        }
    }

    private static string? FindConfigFile()
    {
        var currentDirectoryPath = Path.Combine(Environment.CurrentDirectory, ConfigFileName);
        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        var baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        return File.Exists(baseDirectoryPath) ? baseDirectoryPath : null;
    }

    private static string ResolvePath(string baseDirectory, string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(baseDirectory, path));

    // ReSharper disable once ClassNeverInstantiated.Local
    // ReSharper disable UnusedAutoPropertyAccessor.Local
    private sealed class Dkl1Settings
    {
        public string? ProviderCertificatePath { get; set; }

        public string? ProviderPrivateKeyPath { get; set; }

        public string? ProviderPrivateKeyPassword { get; set; }

        public string? SerialNo { get; set; }

        public string? Make { get; set; }

        public string? Model { get; set; }

        public string? DeviceProvider { get; set; }

        public string? IssuedCertificatePath { get; set; }

        public string? FindMissingField()
        {
            if (string.IsNullOrWhiteSpace(ProviderCertificatePath)) return "providerCertificatePath";
            if (string.IsNullOrWhiteSpace(ProviderPrivateKeyPath)) return "providerPrivateKeyPath";
            if (string.IsNullOrWhiteSpace(SerialNo)) return "serialNo";
            if (string.IsNullOrWhiteSpace(Make)) return "make";
            if (string.IsNullOrWhiteSpace(Model)) return "model";
            if (string.IsNullOrWhiteSpace(DeviceProvider)) return "deviceProvider";
            return null;
        }
    }
    // ReSharper restore UnusedAutoPropertyAccessor.Local
}
