# Plan:修复 CTK 客户端扫描不到 Intent 服务

> 现象：Android CTK 客户端扫描不到本应用，未发起任何 Intent 请求。
> 依据：MOSIP SBI 0.9.5 Request/Response 规范、Android SBI 草案、当前生成的 AndroidManifest。

## 1. 扫描机制

CTK 的“扫描设备”不是发广播，而是通过 `PackageManager` 查询设备上声明了
SBI discovery action 的应用，再对选中的应用发起 `startActivityForResult`。
因此应用必须满足以下**全部**条件才会出现在扫描结果里：

1. 声明的 action 与 CTK 查询用的 action **完全一致**；
2. IntentFilter 能匹配 CTK 的查询 Intent（`category`、`data/mimeType` 都要匹配）；
3. 组件 `android:exported="true"`（已满足）。

当前实现只满足了第 3 条。

## 2. 三个不匹配点

### 2.1 discovery action 写错

CTK 使用的是 SBI Request/Response 规范 v0.9.5 的命名：

```text
io.mosip.device        发现
appId:Info             设备信息
appid.capture          认证采集
```

当前注册的是草案形式的 `io.sbi.device` / `{appId}.Info` / `{appId}.Capture`，
三处都对不上，扫描和后续请求都不会命中。

### 2.2 IntentFilter 声明了 MIME 类型

当前 manifest：

```xml
<intent-filter>
  <action android:name="io.sbi.device" />
  <data android:mimeType="application/json" />
</intent-filter>
```

Android 的匹配规则：**带 `<data android:mimeType>` 的 filter 只匹配设置了 type 的 Intent**。
CTK 查询包时若用 `new Intent("io.sbi.device")`（不带 type），本应用不匹配。

### 2.3 缺少 `android.intent.category.DEFAULT`

`PackageManager.queryIntentActivities(intent, MATCH_DEFAULT_ONLY)` 是扫描类场景的
常用写法，它要求 filter 声明 `android.intent.category.DEFAULT`。当前四个 filter
都没有声明。

## 3. 方案

### 3.1 放宽 IntentFilter 声明（P0）

`SbiIntentActivity` 上按“每个 action 两种 filter”声明：

| action 组 | 别名 |
|---|---|
| 发现 | `io.mosip.device` |
| 信息 | `{appId}:Info`（兼容 `{appId}.Info`） |
| 认证采集 | `{appId}.capture`（兼容 `{appId}.Capture`） |

每组声明：

1. 带 `android.intent.category.DEFAULT`、**不带** MIME；
2. 带 `android.intent.category.DEFAULT`、**带** `application/json`。

### 3.2 action 归一化（P0）

`SbiIntentProtocol` 增加 `Classify(action, appId)`：

- `io.mosip.device` → 发现；
- `{appId}:Info` / `{appId}.Info`（大小写不敏感）→ 信息；
- `{appId}.capture` / `{appId}.Capture`（大小写不敏感）→ 认证采集；
- 其它 → 未知，返回 102。

注册采集（rCapture）整体移除：v0.9.5 未定义 Android 实现，Android CTK 也不支持。
相应地删除 `SbiRegistrationIntentService`、`ISbiRegistrationService` 的 Intent 宿主注册，
以及桥里的自动启动和分发器的 rCapture 分支。

`SbiIntentDispatcher` 与 `SbiIntentErrorResponse` 改用分类结果，避免别名漏判。

### 3.3 收到请求时记录 action（P1）

`SbiIntentActivity` 用 `Android.Util.Log` 输出 `SBI Intent action=...`，
真机 `adb logcat` 可直接确认 CTK 实际使用的 action。

### 3.4 不做的事

- 不新增 Service/Receiver 形态的入口（草案明确用 Activity result 回传）；
- 不实现/不注册注册采集（rCapture），Android CTK 不支持；
- 不改业务分发与响应格式；
- 不改设备层。

## 4. 文件清单

| 文件 | 改动 |
|---|---|
| `SbiAuthenticationBridge.Android/SbiIntentActivity.cs` | 增加别名 action、DEFAULT category、无 MIME 的 filter；记录 action |
| `SbiAuthenticationBridge.Android/SbiIntentAppId.cs` | 增加 `io.mosip.device`、`{appId}:Info` 等别名常量 |
| `SbiAuthenticationBridge.Android/SbiIntentBridge.cs` | 只自动启动认证服务 |
| `SbiAuthenticationBridge.Host.Intent/SbiIntentProtocol.cs` | 新增 action 分类 |
| `SbiAuthenticationBridge.Host.Intent/SbiIntentDispatcher.cs` | 按分类路由，移除 rCapture 分支 |
| `SbiAuthenticationBridge.Host.Intent/SbiIntentErrorResponse.cs` | 按分类生成错误报文 |
| `SbiAuthenticationBridge.Host.Intent/SbiIntentServiceCollectionExtensions.cs` | 不再注册注册用途的 Intent 服务 |
| `SbiAuthenticationBridge.Host.Intent/SbiRegistrationIntentService.cs` | 删除 |

## 5. 架构与依赖自查

- 只改 Android 宿主壳与 Host.Intent 的协议匹配逻辑；
- 无 Core 接口变更、无新增 NuGet。

## 6. 风险

| 风险 | 等级 | 说明 |
|---|---|---|
| CTK 用 Service 而非 Activity 扫描 | 中 | 若真机仍扫描不到，再按 logcat/反编译确认，必要时补 Service 别名入口 |
| 别名过多 | 低 | 只是多几个 intent-filter，不影响显式调用 |
| 大小写不敏感匹配 | 低 | 仅用于 action 归类，不改变返回给调用方的数据 |

## 7. 验证方式

1. 构建后检查生成的 `AndroidManifest.xml`：
   每个 action 都有带/不带 MIME 两组 filter，且都含 `android.intent.category.DEFAULT`；
2. 真机安装后 CTK 重新扫描，应能看到设备；
3. `adb logcat` 出现 `SBI Intent action=<CTK 实际使用的 action>`；
4. 后续 `Info`/`Capture` 请求能正常返回。

---
**等待指令：收到【同意plan，开始编码】后开始实施。**
