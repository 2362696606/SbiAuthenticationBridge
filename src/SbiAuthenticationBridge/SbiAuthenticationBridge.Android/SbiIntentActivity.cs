using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using SbiAuthenticationBridge.Core.Common;
using SbiAuthenticationBridge.Host.Intent;
using AndroidIntent = Android.Content.Intent;

namespace SbiAuthenticationBridge.Android;

/// <summary>
/// SBI Intent 入口：接收发现/信息/采集请求，把响应放回结果 Intent
/// </summary>
[Activity(
    Name = SbiIntentAppId.ActivityName,
    Exported = true,
    ExcludeFromRecents = true,
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTop,
    Theme = "@android:style/Theme.Translucent.NoTitleBar")]
[IntentFilter(
    new[]
    {
        SbiIntentProtocol.DiscoveryCompatAction,
        SbiIntentProtocol.DiscoveryAction,
        SbiIntentAppId.InfoCompatAction,
        SbiIntentAppId.InfoAction,
        SbiIntentAppId.CaptureCompatAction,
        SbiIntentAppId.CaptureAction
    },
    Categories = new[] { DefaultCategory })]
public sealed class SbiIntentActivity : Activity
{
    /// <summary>
    /// 日志标签
    /// </summary>
    private const string LogTag = "SbiIntent";

    /// <summary>
    /// 默认分类：PackageManager 按 MATCH_DEFAULT_ONLY 查询时必需
    /// </summary>
    private const string DefaultCategory = "android.intent.category.DEFAULT";

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _ = HandleIntentAsync(Intent);
    }

    /// <inheritdoc />
    protected override void OnNewIntent(AndroidIntent? intent)
    {
        base.OnNewIntent(intent);
        _ = HandleIntentAsync(intent);
    }

    /// <summary>
    /// 处理请求并把结果回传给调用方
    /// </summary>
    private async Task HandleIntentAsync(AndroidIntent? intent)
    {
        var action = intent?.Action;
        var result = new AndroidIntent();

        Log.Info(LogTag, $"收到 SBI Intent：action={action}");

        try
        {
            if (string.IsNullOrEmpty(action))
            {
                throw new InvalidOperationException("Intent 缺少 action。");
            }

            var dispatcher = await SbiIntentBridge.GetDispatcherAsync().ConfigureAwait(false);
            var input = intent!.GetByteArrayExtra(SbiIntentProtocol.InputExtra) ?? [];
            var response = await dispatcher.DispatchAsync(action, input).ConfigureAwait(false);

            if (response.IsContent)
            {
                var uri = SbiIntentResponseStore.Write(
                    this,
                    SbiIntentAppId.ResponseAuthority,
                    response.ContentPayload!);
                // CTK 插件用 Bundle.getParcelable 读取，必须是 Parcelable 的 Uri 而非字符串
                var responseUri = global::Android.Net.Uri.Parse(uri.ToString());
                result.PutExtra(SbiIntentProtocol.ResponseExtra, responseUri);
                // 读授权只对 Intent 的 data/ClipData 生效，仅放 extra 不会记录授权
                result.SetData(responseUri);
                result.ClipData = global::Android.Content.ClipData.NewRawUri((string?)null, responseUri);
                result.AddFlags(ActivityFlags.GrantReadUriPermission);
            }
            else
            {
                result.PutExtra(SbiIntentProtocol.ResponseExtra, response.InlinePayload);
            }
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, exception.ToString());

            var fallback = SbiIntentErrorResponse.Create(
                action ?? string.Empty,
                SbiIntentAppId.Value,
                SbiErrorHelper.TechnicalErrorDuringExtraction);
            result.PutExtra(SbiIntentProtocol.ResponseExtra, fallback.InlinePayload);
        }

        FinishWithResult(result);
    }

    /// <summary>
    /// 在 UI 线程回传结果并结束 Activity
    /// </summary>
    private void FinishWithResult(AndroidIntent result)
    {
        RunOnUiThread(() =>
        {
            SetResult(Result.Ok, result);
            Finish();
        });
    }
}
