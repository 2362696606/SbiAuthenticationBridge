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
    LaunchMode = LaunchMode.SingleTop,
    Theme = "@style/MyTheme.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
[IntentFilter([SbiIntentProtocol.DiscoveryAction], DataMimeType = "application/json")]
[IntentFilter([SbiIntentAppId.InfoAction], DataMimeType = "application/json")]
[IntentFilter([SbiIntentAppId.CaptureAction], DataMimeType = "application/json")]
[IntentFilter([SbiIntentAppId.RecaptureAction], DataMimeType = "application/json")]
public sealed class SbiIntentActivity : Activity
{
    private const string LogTag = "SbiIntent";

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
                result.PutExtra(SbiIntentProtocol.ResponseExtra, uri.ToString());
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
