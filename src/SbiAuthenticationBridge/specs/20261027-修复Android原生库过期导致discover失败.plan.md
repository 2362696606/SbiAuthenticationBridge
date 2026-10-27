# Plan:修复 Android 原生库过期导致 ftm_discover 返回 0x50

> 现象：Android 端调用 `ftm_discover` 返回 `0x50`。
> 依据：`E:\code\local\ftmBridgeAPI` 源码、仓库内旧 `.so`、构建产物对比。

## 1. 0x50 不是设备状态，而是帧长度

`0x50 = 80`。按 SDK 的帧格式计算 DISCOVER 请求帧长度：

```text
data_len = 23 + callbackLen          // payload
data_len++                            // 命令码占位
data_len = POS_CMDCODE(9) + data_len + 2
帧长度   = 6 + data_len = 41 + callbackLen
```

Android 的 callbackId 是 appId：

```text
com.CompanyName.SbiAuthenticationBridge = 39 字节
41 + 39 = 80 = 0x50
```

也就是说，返回的 `0x50` 正好等于 **发送帧的字节数**，不是设备报告的
“非应答包”（ERR_NOTRESPOND 虽然是 0x50，但按当前源码只会以负值返回）。

## 2. 根因：Android `.so` 是旧版 `fap.cpp` 编译的

对比 SDK 两份源码（`ftmBridgeAPI(new)` 与当前 `ftmBridgeAPI`）：

```diff
 // fap.cpp  bulkTransfer 末尾
-    return transferred;
+    return result;
```

```diff
 // FPScan.cpp send/receive 返回判断
-    if (ret != bufferLength) ...
-    if (ret != LEN_ACK) ...
+    if (ret != ERR_OK) ...
```

当前源码里 `FPScan` 按 `ERR_OK(0)` 判断，`Fap::bulkTransfer` 也返回 libusb 状态（0）。
而仓库里的 Android 原生库：

- `SbiAuthenticationBridge.Device.Android/native/arm64-v8a/libftmBridgeAPI.so`
  （2026-10-21 17:23 构建）
- `fap.cpp` 的修改时间 **2026-10-23 09:21**，晚于该 `.so`；
  同一天 09:22 重新构建的 Windows DLL 已包含修复。

用当前源码执行 `android\build.cmd` 时，增量构建**只重新编译了 `fap.cpp`**，
新产物与仓库内旧库大小相同、哈希不同，导出符号完全一致 —— 证实旧库仍是
`return transferred` 的版本。

因此 Android 上每次 `send` 都把“已发送字节数”（80）当成错误码返回，
`ftm_discover` 得到 0x50 并立即失败，设备根本没有收到解析结果。

## 3. 修复

1. 用当前源码重新构建的 `libftmBridgeAPI.so` 替换：
   - `SbiAuthenticationBridge.Device.Android/native/arm64-v8a/libftmBridgeAPI.so`
   - 同步更新 SDK 交付目录 `ftmBridgeAPI/android/sdk/libs/arm64-v8a/`
2. 在 `native/` 下补一份简短说明（构建来源、NDK 版本、重建命令），避免再次使用过期产物。
3. 不改 C# 逻辑、不改协议、不改导出接口。

## 4. 文件清单

| 文件 | 改动 |
|---|---|
| `SbiAuthenticationBridge.Device.Android/native/arm64-v8a/libftmBridgeAPI.so` | 替换为新构建 |
| `SbiAuthenticationBridge.Device.Android/native/README.md`（新增） | 构建来源与重建方法 |
| `E:\code\local\ftmBridgeAPI\android\sdk\libs\arm64-v8a\libftmBridgeAPI.so` | 同步新产物（仓库外） |

## 5. 架构与依赖自查

- 仅替换原生二进制与说明文档，无代码/接口变更；
- 导出符号与原库一致（已用 `llvm-nm` 比对）。

## 6. 风险

| 风险 | 等级 | 说明 |
|---|---|---|
| 旧库仍被缓存 | 中 | 需清理 Android 构建缓存并全量重建，确认 APK 内 `.so` 哈希为新版本 |
| 其它调用路径 | 低 | 修复的是 SDK 内部返回值语义，所有命令同时恢复 |
| 真机验证依赖 | 高 | 当前无 Android 设备，最终需真机跑一次 DISCOVER |

## 7. 验证方式

1. 重新构建 APK，确认 `lib/arm64-v8a/libftmBridgeAPI.so` 的 SHA-256 与新产物一致：
   `ed94f30e99ceea2f3c354db9baf5b495913a0dc9a92661f574e543fb2d139b83`；
2. 真机发 `io.sbi.device`，`ftm_discover` 返回 `0x0`，响应为合法 Discovery JSON；
3. 再用同一设备验证 `ftm_get_device_info`、`ftm_capture_auth` 不再返回帧长度值。

---
**等待指令：收到【同意plan，开始编码】后开始实施。**
