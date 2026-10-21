using System.Globalization;
using SbiAuthenticationBridge.Core.Dto;

namespace SbiAuthenticationBridge.Core.Common;

/// <summary>
/// MOSIP SBI 的标准错误对象。
/// </summary>
/// <remarks>
/// <para>
/// 取值逐字对齐 CTK schema 里 <c>error</c> 的枚举：<c>errorCode</c> 是<b>裸数字</b>
/// （<c>["0"]</c>、<c>["111"]</c>、<c>["100"]</c>、<c>["110"]</c>……），描述另放 <c>errorInfo</c>
/// （<c>["Success"]</c>、<c>["Device is Busy"]</c>……）。
/// </para>
/// <para>
/// <b>⚠ 不要写成"码 + 描述"的合并形式</b>（如 <c>"111 - Device is Busy"</c>）：规范正文那张错误码表
/// 把码与描述排在同一列，照着抄很容易抄成合并串，但 CTK 的 schema 枚举只认裸数字。
/// 2026-10-09 核对了 schema 原件（见下），且本服务以裸数字跑过 CTK 的 SBI1000/1001/1026/1196。
/// </para>
/// <para>
/// 每次访问都返回新实例：<see cref="SbiError"/> 的属性可写，共享一个实例会被调用方改坏。
/// </para>
/// <para>
/// 依据：<c>https://raw.githubusercontent.com/mosip/mosip-compliance-toolkit/master/resources/schemas/sbi/0.9.5/</c>
/// 下的 <c>DiscoverResponseSchema.json</c>、<c>DeviceInfo{Ready,Busy,NotReady,NotRegistered}ResponseSchema.json</c>
/// —— CTK 的 schema 与用例清单都是公开的，不必等实跑才能确认。
/// </para>
/// </remarks>
public static class SbiErrorHelper
{
    /// <summary>
    /// 成功。
    /// </summary>
    public static SbiError Success => Create("0", "Success");

    /// <summary>
    /// 设备未注册。
    /// </summary>
    public static SbiError DeviceNotRegistered => Create("100", "Device not registered");

    /// <summary>
    /// 检测不到生物特征。
    /// </summary>
    public static SbiError UnableToDetectBiometricObject =>
        Create("101", "Unable to detect a biometric object");

    /// <summary>
    /// 提取时出现技术错误。
    /// </summary>
    public static SbiError TechnicalErrorDuringExtraction =>
        Create("102", "Technical error during extraction");

    /// <summary>
    /// 提取时出现技术错误，并附带具体原因（用于现场排查）。
    /// </summary>
    /// <param name="detail">具体原因</param>
    /// <returns>errorCode 为 102、errorInfo 为具体原因的错误对象。</returns>
    public static SbiError TechnicalErrorDetail(string detail) =>
        Create("102", string.IsNullOrWhiteSpace(detail)
            ? "Technical error during extraction"
            : detail);

    /// <summary>
    /// 检测到设备被篡改。
    /// </summary>
    public static SbiError DeviceTamperDetected => Create("103", "Device tamper detected");

    /// <summary>
    /// 连不上管理服务端。
    /// </summary>
    public static SbiError UnableToConnectToManagementServer =>
        Create("104", "Unable to connect to management server");

    /// <summary>
    /// 图像方向错误。
    /// </summary>
    public static SbiError ImageOrientationError => Create("105", "Image orientation error");

    /// <summary>
    /// 找不到设备。
    /// </summary>
    public static SbiError DeviceNotFound => Create("106", "Device not found");

    /// <summary>
    /// 设备公钥已过期。
    /// </summary>
    public static SbiError DevicePublicKeyExpired => Create("107", "Device public key expired");

    /// <summary>
    /// 缺少域公钥。
    /// </summary>
    public static SbiError DomainPublicKeyMissing => Create("108", "Domain public key missing");

    /// <summary>
    /// 不支持请求的生物特征数量（手指／虹膜）。
    /// </summary>
    public static SbiError RequestedBiometricCountNotSupported =>
        Create("109", "Requested number of biometric (Finger/IRIS) not supported");

    /// <summary>
    /// 设备未就绪。<b>设备未连接也归这里</b> —— 状态映射会把"就绪但没连上"降为未就绪。
    /// </summary>
    public static SbiError DeviceNotReady => Create("110", "Device is not ready");

    /// <summary>
    /// 设备忙，正在处理上一次采集。
    /// </summary>
    public static SbiError DeviceBusy => Create("111", "Device is Busy");

    /// <summary>
    /// 事务 ID 无效。
    /// </summary>
    public static SbiError InvalidTransactionId => Create("112", "Invalid Transaction ID");

    /// <summary>
    /// 没有连接设备。
    /// </summary>
    /// <remarks>
    /// 只在"整台设备都报不出来"时用；设备只是没连上时用 <see cref="DeviceNotReady"/> ——
    /// CTK 的 <c>DeviceInfoNotReadyResponseSchema</c> 把该状态的码钉死在 110。
    /// </remarks>
    public static SbiError NoDeviceConnected => Create("202", "No device connected");

    /// <summary>
    /// 构造一个设备供应商自定义的错误（5xx 段）。
    /// </summary>
    /// <param name="errorCode">
    /// 自定义错误码的数字部分，取值须落在 500–599 之间。
    /// </param>
    /// <param name="errorInfo">
    /// 自定义错误描述。
    /// </param>
    /// <returns>
    /// 错误对象，<c>errorCode</c> 为裸数字。
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="errorCode"/> 不在 5xx 段内。
    /// </exception>
    public static SbiError Custom(int errorCode, string errorInfo)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(errorCode, 500);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(errorCode, 599);

        return Create(errorCode.ToString(CultureInfo.InvariantCulture), errorInfo);
    }

    /// <summary>
    /// 按错误码与描述构造错误对象。
    /// </summary>
    /// <param name="errorCode">
    /// 错误码（裸数字）。
    /// </param>
    /// <param name="errorInfo">
    /// 错误描述。
    /// </param>
    /// <returns>
    /// 错误对象。
    /// </returns>
    private static SbiError Create(string errorCode, string errorInfo) => new()
    {
        ErrorCode = errorCode,
        ErrorInfo = errorInfo,
    };
}