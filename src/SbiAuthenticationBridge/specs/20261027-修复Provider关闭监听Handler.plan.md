# Plan:修复 Provider 关闭监听缺少 Handler

> 上一轮修复生效了：URI 授权已打通，CTK 能进入 Provider 读取。
> 现在失败在 Provider 的 `openFile` 内部。

## 1. 现象

CTK 日志：

```text
D MosipSbiCapacitorPlugin: onCaptureResult: ActivityResult{... dat=content://...sbi.responses/... flg=0x1 clip={text/uri-list ...} ...}
D MosipSbiCapacitorPlugin: RCapture Exception: Handler must not be null
```

系统侧堆栈：

```text
E DatabaseUtils: java.lang.IllegalArgumentException: Handler must not be null
E DatabaseUtils:  at android.os.ParcelFileDescriptor.open(ParcelFileDescriptor.java:267)
E DatabaseUtils:  at com.CompanyName.SbiAuthenticationBridge.SbiIntentResponseProvider.n_openFile(Native Method)
E DatabaseUtils:  at ...SbiIntentResponseProvider.openFile(SbiIntentResponseProvider.java:41)
E DatabaseUtils:  at android.content.ContentProvider.openAssetFile(ContentProvider.java:2319)
```

结果 Intent 已带 `dat=content://...`、`clip=...`、`flg=0x1`，
说明 System 已记录读授权（不再报 `Failed to find provider info`）。

## 2. 根因

`SbiIntentResponseProvider.OpenFile` 传入 `null` Handler 和非空 `OnCloseListener`：

```csharp
return ParcelFileDescriptor.Open(
    new File(path),
    ParcelFileMode.ReadOnly,
    null,                       // ← Handler
    new DeleteOnCloseListener(path));
```

Android 平台 `ParcelFileDescriptor.open()`（API 36，第 267 行）要求：
只要提供 `OnCloseListener`，Handler 就不允许为 null，否则抛
`IllegalArgumentException("Handler must not be null")`。

## 3. 方案

`SbiAuthenticationBridge.Android/SbiIntentResponseProvider.cs`：
传入主线程 Handler。

```csharp
return ParcelFileDescriptor.Open(
    new File(path),
    ParcelFileMode.ReadOnly,
    new Handler(Looper.MainLooper!),
    new DeleteOnCloseListener(path));
```

`Android.OS` 已在文件 using 中；关闭回调仍在主线程执行，只做文件删除，开销可忽略。

## 4. 不做的事

- 不改 Provider 的 exported/grantUriPermissions；
- 不改 Intent 的 data/ClipData；
- 不改响应文件读写格式。

## 5. 文件清单

| 文件 | 改动 |
|---|---|
| `SbiAuthenticationBridge.Android/SbiIntentResponseProvider.cs` | `ParcelFileDescriptor.Open` 传入主线程 Handler |

## 6. 风险

| 风险 | 等级 | 说明 |
|---|---|---|
| 回调线程变化 | 极低 | 由 binder 线程改为主线程，仅执行文件删除 |
| 主线程阻塞 | 极低 | 单次删除一个小 JSON 文件 |

## 7. 验证

1. 重新构建并安装 APK；
2. CTK 重跑采集用例；
3. logcat 不应再出现 `Handler must not be null`；
4. CTK 用例通过。

---
**等待指令：收到【同意plan，开始编码】后开始实施。**
