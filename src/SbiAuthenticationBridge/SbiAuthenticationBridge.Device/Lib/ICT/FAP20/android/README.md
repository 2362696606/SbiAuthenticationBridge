# ftmBridgeAPI — Android 原生 SDK

面向 .NET / Avalonia Android 上位机的原生库。产物为单一、自包含的
`libftmBridgeAPI.so`（libusb 1.0.26 已静态链接，不依赖 `libusb1.0.so`
或 `libc++_shared.so`），通过 P/Invoke 直接调用。

## 产物

| 文件 | 说明 |
|---|---|
| `build/libs/arm64-v8a/libftmBridgeAPI.so` | arm64-v8a 原生库（Android 21+，16KB 页对齐） |
| `sdk/` | 打包后的交付目录（`libs/` + `include/`） |

导出符号为 `ftm_*` C 接口（23 个），libusb 符号已隐藏。

## 构建

需要 Android NDK `27.2.12479018`（已安装到
`%LOCALAPPDATA%\Android\Sdk\ndk\27.2.12479018`）。

```bat
android\build.cmd
```

或直接调用：

```bat
%ANDROID_NDK_HOME%\ndk-build.cmd -C android NDK_PROJECT_PATH=. ^
    APP_BUILD_SCRIPT=jni/Android.mk NDK_APPLICATION_MK=jni/Application.mk ^
    NDK_OUT=build/obj NDK_LIBS_OUT=build/libs
```

打包交付目录：

```bat
android\package.cmd
```

## Avalonia / .NET Android 集成

### 1. 打包 .so

在 Avalonia 的 Android 目标框架下把原生库作为 `AndroidNativeLibrary`
打包（.NET Android SDK 会自动放进 APK 的 `lib/arm64-v8a/`）：

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net8.0-android'">
  <AndroidNativeLibrary Include="..\ftmBridgeAPI\android\sdk\libs\arm64-v8a\libftmBridgeAPI.so" />
</ItemGroup>
```

`DllImport("ftmBridgeAPI")` 会自动解析为 `libftmBridgeAPI.so`。

### 2. USB 权限与 fd 传递

非 root 的 Android 应用不能访问 `/dev/bus/usb`，必须由应用层完成：

1. `UsbManager.DeviceList` 按 VID/PID 找到设备；
2. `UsbManager.RequestPermission` 取得授权；
3. `UsbManager.OpenDevice(device)` 得到 `UsbDeviceConnection`；
4. `device.GetInterface(0)` 后 `connection.ClaimInterface(iface, true)`；
5. 把 `connection.FileDescriptor`（`getFileDescriptor()` 返回的 int）传给
   `ftm_open_fd()`。

```csharp
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

static class FtmNative
{
    const string Lib = "ftmBridgeAPI";

    [DllImport(Lib)] public static extern void ftm_set_config_path(string path);
    [DllImport(Lib)] public static extern void ftm_log_init_path(string path, int level);
    [DllImport(Lib)] public static extern IntPtr ftm_open_fd(int fd);
    [DllImport(Lib)] public static extern void ftm_close(IntPtr handle);
    [DllImport(Lib)] public static extern int ftm_discover(
        IntPtr h, byte[] request, int wlen, byte[] response, uint timeout);
    // 其余 ftm_* 接口同理，签名见 include/ftmBridgeAPI.h
}

// 打开设备
var usbManager = (UsbManager)context.GetSystemService(Context.UsbService)!;
var device = usbManager.DeviceList.Values.First(d => d.VendorId == vid && d.ProductId == pid);

// requestPermission 是异步的，需在 BroadcastReceiver / 回调里继续
var connection = usbManager.OpenDevice(device)
    ?? throw new InvalidOperationException("OpenDevice failed");

var iface = device.GetInterface(0);
if (!connection.ClaimInterface(iface, true))
    throw new InvalidOperationException("ClaimInterface failed");

var filesDir = context.FilesDir!.AbsolutePath;
FtmNative.ftm_set_config_path(Path.Combine(filesDir, "FingerPrintDebug.INI"));
FtmNative.ftm_log_init_path(Path.Combine(filesDir, "sdk_runtime.log"), 1);

IntPtr handle = FtmNative.ftm_open_fd(connection.FileDescriptor);
if (handle == IntPtr.Zero)
    throw new InvalidOperationException("ftm_open_fd failed");

// ... 调用 ftm_capture_auth 等 ...

// 关闭：先关 SDK，再关 UsbDeviceConnection
FtmNative.ftm_close(handle);
connection.ReleaseInterface(iface);
connection.Close();
```

### 3. 关键约束

- **fd 所有权属于 `UsbDeviceConnection`**。libusb 不会关闭传入的 fd
  （`LIBUSB_OPTION_NO_DEVICE_DISCOVERY` + `libusb_wrap_sys_device` 的
  `fd_keep` 语义），因此 `ftm_close()` 之后必须由 C# 侧关闭 connection。
- 调用 `ftm_open_fd()` 前请先 `ClaimInterface`；若接口已被平台层占用，
  库会记录警告并继续使用已有声明。
- `UsbDeviceConnection.FileDescriptor` 是 .NET Android 对 Java
  `getFileDescriptor()` 的绑定属性。若所用 .NET 版本没有该属性，
  可通过 JNI 调用 `getFileDescriptor()` 取得同一个 int。
- `ftm_set_config_path()` 只用于读取 `BULKOUT` / `BULKIN`（缺省
  `0x01` / `0x82`）。Android 上不解析 VID/PID，也不做设备枚举。
- 日志默认写当前目录（Android 下不可写），请调用
  `ftm_log_init_path()` 指向 `FilesDir`。
- `ftm_open()` 仅适用于可枚举 USB 的环境（桌面 / root 设备）。
- 采集响应缓冲区至少 124104 字节（`ftm_capture_auth`），
  超时建议 ≥ 30000 ms。

## 目录结构

```
android/
  jni/Android.mk          # libusb 静态库 + ftmBridgeAPI 共享库
  jni/Application.mk      # arm64-v8a / android-21 / c++_static
  third_party/libusb/     # libusb 1.0.26 源码（Android usbfs 后端）
  build.cmd               # 构建脚本
  package.cmd             # 生成 sdk/ 交付目录
  build/                  # 构建输出（obj / libs）
```
