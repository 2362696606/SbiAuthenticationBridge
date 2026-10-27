# libftmBridgeAPI.so（arm64-v8a）

FAP20 原生 SDK 的 Android 产物，来自 `E:\code\local\ftmBridgeAPI`。

## 来源

| 项 | 值 |
|---|---|
| 源码 | `ftmBridgeAPI/android`（libusb 1.0.26 静态链接） |
| 构建命令 | `android\build.cmd` |
| NDK | 27.2.12479018 |
| ABI | arm64-v8a（Android 21+，16KB 页对齐） |
| 构建时间 | 2026-10-27 |
| SHA-256 | `ed94f30e99ceea2f3c354db9baf5b495913a0dc9a92661f574e543fb2d139b83` |

## 重新构建

```bat
cd E:\code\local\ftmBridgeAPI
android\build.cmd
```

然后把 `android\build\libs\arm64-v8a\libftmBridgeAPI.so` 覆盖到本目录。

## 注意

- **必须用当前源码重新构建**。2026-10-21 的旧产物是用旧版 `fap.cpp` 编译的，
  `bulkTransfer` 返回已发送字节数而不是 libusb 状态，导致所有命令返回帧长度
  （例如 `ftm_discover` 返回 `0x50` = 80 字节帧长）。
- 该库只包含 arm64-v8a；x86_64 模拟器无法加载。
