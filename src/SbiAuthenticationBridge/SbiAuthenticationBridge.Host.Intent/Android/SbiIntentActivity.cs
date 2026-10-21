using Android.Content;
using Android.Content.PM;
using AndroidIntent = Android.Content.Intent;
using AndroidUri = Android.Net.Uri;
using Serilog;

namespace SbiAuthenticationBridge.Host.Intent.Android;

/// <summary>
/// 接收 MOSIP SBI 调用的无界面 Activity
/// </summary>
/// <remarks>
/// <para>
/// 调用方通过 <c>startActivityForResult</c> 发起调用，请求 JSON 放在 <c>input</c> extra，
/// 本 Activity 处理完成后把响应写入结果 Intent 的 <c>response</c> extra 并结束。
/// </para>
/// <para>
/// 设备发现与设备信息直接返回字节数组；认证/注册采集返回 content URI，
/// 并在结果 Intent 上附加 <see cref="ActivityFlags.GrantReadUriPermission"/>。
/// </para>
/// </remarks>
[Activity(
    Exported = true,
    ExcludeFromRecents = true,
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTop,
    Theme = "@android:style/Theme.Translucent.NoTitleBar")]
[IntentFilter(
    new[]
    {
        SbiIntentActions.Discovery,
        SbiIntentActions.DiscoveryAlias,
        SbiIntentActions.DefaultInfoAction,
        SbiIntentActions.DefaultInfoAliasAction,
        SbiIntentActions.DefaultCaptureAction,
        SbiIntentActions.DefaultCaptureAliasAction,
        SbiIntentActions.DefaultRegistrationCaptureAction
    },
    Categories = new[] { AndroidIntent.CategoryDefault })]
public sealed class SbiIntentActivity : Activity
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", "Intent");

    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>
    /// 接收调用并发起异步处理
    /// </summary>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var intent = this.Intent;
        _ = HandleAsync(intent);
    }

    /// <summary>
    /// Activity 销毁时取消尚未完成的设备操作
    /// </summary>
    protected override void OnDestroy()
    {
        _cancellation.Cancel();
        base.OnDestroy();
    }

    /// <summary>
    /// 调用宿主并把结果写回调用方
    /// </summary>
    private async Task HandleAsync(AndroidIntent? intent)
    {
        try
        {
            var input = intent is null ? null : SbiIntentExtras.ReadInput(intent);
            var result = await SbiIntentHostRegistry.Current
                .HandleAsync(intent?.Action, input, _cancellation.Token)
                .ConfigureAwait(true);

            SetResult(Result.Ok, CreateReply(result));
            Logger.Debug("SBI Intent 请求完成：{Action:l}", intent?.Action);
#if DEBUG
            var responseText = System.Text.Encoding.UTF8.GetString(result.Payload);
            var preview = responseText.Length <= 512
                ? responseText
                : responseText[..512] + "…";

            Logger.Debug("SBI Intent 响应（{Length} 字节）：{Response:l}",
                result.Payload.Length, preview);
#endif
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("SBI Intent 请求已取消：{Action:l}", intent?.Action);
            SetResult(Result.Canceled);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "处理 SBI Intent 失败");
            SetResult(Result.Canceled);
        }
        finally
        {
            Finish();
        }
    }

    /// <summary>
    /// 构造结果 Intent
    /// </summary>
    private static AndroidIntent CreateReply(SbiIntentResult result)
    {
        var reply = new AndroidIntent();

        if (result.IsContentUri)
        {
            var id = SbiIntentResponseStore.Put(result.Payload);
            var uri = AndroidUri.Parse(
                          $"content://{SbiIntentActions.ContentProviderAuthority}/sbi/{id}")
                      ?? throw new InvalidOperationException("无法构造 SBI 响应 content URI。");

            // SBI 调用方（mosip-sbi-capacitor-plugin）用 Bundle.getParcelable("response")
            // 取 content URI；这里必须放 Uri 对象，放字符串会得到 null 并导致读取失败。
            // GrantReadUriPermission 只对 Intent.data（或 ClipData）中的 URI 生效，
            // 所以还必须把 URI 设为 data，否则调用方会得到 "No content provider"。
            reply.SetData(uri);
            reply.PutExtra(SbiIntentActions.ResponseExtra, uri);
            reply.AddFlags(ActivityFlags.GrantReadUriPermission);
        }
        else
        {
            reply.PutExtra(SbiIntentActions.ResponseExtra, result.Payload);
        }

        return reply;
    }
}
