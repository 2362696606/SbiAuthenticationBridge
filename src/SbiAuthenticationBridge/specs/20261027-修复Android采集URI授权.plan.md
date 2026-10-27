# Plan:修复 Android 采集 URI 未授权

> 上一轮修复后错误已变化：不再是「response 类型不对」，
> 而是 CTK 拿不到 ContentProvider 的读取授权。

## 1. 当前现象

```text
D MosipSbiCapacitorPlugin: onCaptureResult: ActivityResult{resultCode=RESULT_OK, data=Intent { flg=0x1 ... }}
E ActivityThread: Failed to find provider info for com.CompanyName.SbiAuthenticationBridge.sbi.responses
D MosipSbiCapacitorPlugin: RCapture Exception: No content provider: content://com.CompanyName.SbiAuthenticationBridge.sbi.responses/9e12...
```

设备侧采集成功、`content=true`，CTK 收到 `RESULT_OK` 且带 `flg=0x1`（FLAG_GRANT_READ_URI_PERMISSION），
但读取 Provider 时被系统拒绝。

## 2. 根因

Android 的 `FLAG_GRANT_READ_URI_PERMISSION` **只会对 Intent 的 `data` 或 `ClipData` 中的 URI 生效**，
不会扫描 extras 里的任意 URI。

当前代码只把 URI 放进 `response` extra：

```csharp
result.PutExtra(SbiIntentProtocol.ResponseExtra, global::Android.Net.Uri.Parse(uri.ToString()));
result.AddFlags(ActivityFlags.GrantReadUriPermission);
```

因此系统没有记录任何授权；而 Provider 声明为 `exported=false`，
CTK（uid 10322）无权限访问 → 系统按"找不到 Provider"返回。

### 证据

1. Provider 已正确注册：
   ```text
   ContentProvider Authorities:
     [com.CompanyName.SbiAuthenticationBridge.sbi.responses]:
       Provider{... com.CompanyName.SbiAuthenticationBridge/.SbiIntentResponseProvider}
   ```
2. 临时授权表中没有该 authority 的任何记录：
   `dumpsys activity uri-permissions | grep sbi.responses` → 无输出。
3. CTK 收到结果 Intent 的 `flg=0x1` 证明 flag 已带上，但 URI 不在 data/ClipData 中，flag 空转。

## 3. 方案

`SbiAuthenticationBridge.Android/SbiIntentActivity.cs` 采集分支，在放入 extra 的同时把 URI 设置为
Intent 的 `data` 与 `ClipData`：

```csharp
var androidUri = global::Android.Net.Uri.Parse(uri.ToString());
result.PutExtra(SbiIntentProtocol.ResponseExtra, androidUri);   // CTK 插件按 getParcelable 读取
result.SetData(androidUri);                                      // 让系统识别 URI，记录读授权
result.ClipData = global::Android.Content.ClipData.NewRawUri(null, androidUri);
result.AddFlags(ActivityFlags.GrantReadUriPermission);
```

Provider 保持 `exported=false`、`grantUriPermissions=true`，符合 Android SBI 规范。

## 4. 不做的事

- 不把 Provider 改成 exported=true；
- 不改发现/信息链路（byte[]）；
- 不改 Provider 与响应文件存储逻辑。

## 5. 文件清单

| 文件 | 改动 |
|---|---|
| `SbiAuthenticationBridge.Android/SbiIntentActivity.cs` | 采集结果补充 `SetData` + `ClipData` |

## 6. 风险

| 风险 | 等级 | 说明 |
|---|---|---|
| 结果 Intent 增加 data | 低 | 插件只读 extras，data 不影响其解析；系统据此记录授权 |
| 授权范围 | 低 | 仅授权本次返回的单个 URI，读取后可回收 |

## 7. 验证

1. 重新构建并安装 APK；
2. CTK 重跑采集用例；
3. logcat 不应再出现 `Failed to find provider info`；
4. `dumpsys activity uri-permissions | grep sbi.responses` 应出现临时授权记录（读取后消失）；
5. CTK 用例通过。

---
**等待指令：收到【同意plan，开始编码】后开始实施。**
