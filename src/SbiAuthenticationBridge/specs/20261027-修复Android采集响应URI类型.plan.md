# Plan:修复 Android 采集响应 URI 类型

> 现象：CTK 执行采集类用例失败，提示
> `connection Failure - Unable to connect to device / SBI`，
> 随后 `Unable to save / update test run details`。

## 1. 结论

不是设备信息不符，也不是证书不匹配。设备侧发现/信息/采集全部成功，
失败发生在 CTK 插件读取采集结果这一步：**`response` extra 的类型不对**。

## 2. 证据

### 2.1 设备侧日志（16:17:23 - 16:17:26）

```text
[DBG] CAPTURE 请求体：{... "transactionId":"SBI1018-603" ...}
[INF] 采集成功：transactionId=SBI1018-603，轮询 5 次，质量分 100，耗时 2764 ms
[INF] CAPTURE 完成：bio 项数=1，返回条数=1
[INF] SBI Intent 处理完成：action=com.CompanyName.SbiAuthenticationBridge.Capture，content=true，耗时=3104 ms
```

设备、证书、采集全部正常，结果已通过 content URI 返回。

### 2.2 CTK 日志

```text
D MosipSbiCapacitorPlugin: onCaptureResult: ActivityResult{resultCode=RESULT_OK, ...}
W Bundle  : at io.mosip.capacitor.plugin.MosipSbiCapacitorPlugin.onCaptureResult(MosipSbiCapacitorPlugin.java:276)
D MosipSbiCapacitorPlugin: RCapture Exception: uri
```

### 2.3 CTK APK 字节码（`classes2.dex`）

```text
Bundle.getParcelable:(Ljava/lang/String;)Landroid/os/Parcelable;
check-cast v9, Landroid/net/Uri;
ContentResolver.openInputStream:(Landroid/net/Uri;)
```

插件把 `response` 当作 **Parcelable（android.net.Uri）** 读取。

### 2.4 链式结果

1. 我们放的是 **String**（`SbiIntentActivity.cs:88` `uri.ToString()`）；
2. `Bundle.getParcelable("response")` 类型不匹配 → 打印 `W Bundle` 警告并返回 `null`；
3. `openInputStream(null)` 抛 `NullPointerException("uri")`；
4. 插件 catch 后返回失败，`message = "uri"`；
5. CTK 前端显示 `connection Failure - Unable to connect to device / SBI`。
   `Unable to save / update test run details` 是这次失败测试运行的连带提示。

## 3. 方案

`SbiAuthenticationBridge.Android/SbiIntentActivity.cs`：
采集分支改为放入 `Android.Net.Uri`（实现 `IParcelable`），而非字符串。

```csharp
var uri = SbiIntentResponseStore.Write(this, SbiIntentAppId.ResponseAuthority, response.ContentPayload!);
result.PutExtra(SbiIntentProtocol.ResponseExtra, Android.Net.Uri.Parse(uri.ToString()));
result.AddFlags(ActivityFlags.GrantReadUriPermission);
```

已确认绑定存在：`Intent.PutExtra(string, IParcelable)`、`Uri.Parse(string)`。

发现/信息仍放 `byte[]`，插件用 `getByteArray` 读取，不受影响。

## 4. 不做的事

- 不改 ContentProvider、不改 URI authority；
- 不改 Intent filter / action；
- 不改发现、信息链路。

## 5. 文件清单

| 文件 | 改动 |
|---|---|
| `SbiAuthenticationBridge.Android/SbiIntentActivity.cs` | 采集结果 extra 由 String 改为 `Android.Net.Uri` |

## 6. 风险

| 风险 | 等级 | 说明 |
|---|---|---|
| 规范文本写“string” | 低 | MOSIP 文档描述为字符串，但 CTK 实际实现按 Parcelable 读取；以实际消费方为准 |
| 其他消费方按 String 读取 | 低 | Bundle 类型不匹配只会得到 null，不会崩溃；当前目标消费方只有 CTK |

## 7. 验证

1. 重新构建并安装 APK；
2. CTK 重跑 SBI1018 采集用例；
3. logcat 应不再出现 `RCapture Exception` / `W Bundle`，设备日志仍显示 `content=true`；
4. CTK 用例结果通过。

---
**等待指令：收到【同意plan，开始编码】后开始实施。**
