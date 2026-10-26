# Plan:ServiceHandle 实现

> 对应 spec：`specs/20261026-ServiceHandle实现.spec.md`
> 遵循 `arch.md`：实现放 `SbiAuthenticationBridge.Host`（Host 层），只依赖 Core；组合根只在 `SbiAuthenticationBridge` 注册；不新增 NuGet；不修改 Core 契约。

## 0. 前置决策

| 项 | 决策 / 说明 |
|---|---|
| 实现位置 | `SbiAuthenticationBridge.Host`（上一期 HTTP 宿主约定：Host.Http 只路由，具体实现由 Host 提供） |
| 设备来源 | 注入 `ISbiDeviceProvider`，按其 `Devices` 值筛选：认证句柄取 `ISbiAuthenticationDevice`，注册句柄取 `ISbiRegistrationDevice`；字典键仅用于稳定排序，不参与业务匹配 |
| 回调标识 | 每个句柄按自身用途读取 `ISbiHostContext.GetCallbackId(DevicePurpose.Auth / Registration)`，不缓存 |
| 注册方式 | Host 提供 `AddSbiHostServices()`；`SbiAuthenticationBridge`（组合根）引用 Host 并在 `ConfigureServices()` 中注册。Host.Http 通过现有 `IServiceProvider` 桥接自动解析到这些实现 |
| Capture 结果合并 | 以请求 `Bio` 顺序逐项路由并追加设备返回的全部 `BiometricInfo`；**不强行压缩为 1:1**（FAP20 对 count>1 的单个 bio 项会返回多条，压缩会丢数据）。无设备/设备异常时在对应位置补错误记录 |
| 多设备路由 | 单设备直接路由；多设备时用设备 `DiscoverAsync` 的描述信息按 `DeviceId/DeviceCode`、`Type`、`DeviceSubId` 选择；无匹配返回 `DeviceNotFound` |
| 日志（已要求接入） | Host 使用 `Microsoft.Extensions.Logging` 抽象记录结构化日志，由组合根已有的 Serilog 管线（`LoggingBootstrapper`）输出到 Console/内存 LogView；异常路径不再只写 `Debug.WriteLine`。不直接依赖 Serilog 实现。**每次操作的请求体与响应体按 DTO 序列化为 JSON 记录（截断 4096 字符）；`/stream` 只记录请求体与流元信息，不记录二进制内容** |
| 新增 NuGet | `Microsoft.Extensions.Logging.Abstractions` 10.0.0、`Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.0（官方抽象包，与现有 10.x 主版本一致；后者用于 `AddSbiHostServices()`） |

## 1. 方案总览

```
Host.Http (认证/注册两个端口)
   │ 注入 ISbiAuthenticationServiceHandle / ISbiRegistrationServiceHandle
   ▼
SbiAuthenticationBridge.Host
   ├─ SbiServiceHandleBase
   │    ├─ CallbackId（按用途读 ISbiHostContext）
   │    ├─ DiscoverAsync / GetDeviceInfoAsync：遍历本用途设备并合并结果
   │    ├─ 设备筛选、排序、异常→SbiError 映射
   │    └─ ILogger（MEL）结构化日志
   ├─ SbiAuthenticationServiceHandle
   │    └─ CaptureAsync：按 bio 项路由 ISbiAuthenticationDevice
   └─ SbiRegistrationServiceHandle
        ├─ CaptureAsync：按 bio 项路由 ISbiRegistrationDevice
        └─ StreamAsync：选择注册设备并返回 M-JPEG 流
   │
   ▼
SbiAuthenticationBridge.Core
   ├─ ISbiDeviceProvider → Devices（ISbiDevice）
   ├─ ISbiAuthenticationDevice / ISbiRegistrationDevice
   └─ ISbiHostContext
```

- 认证与注册句柄只看到本用途设备，互不串用。
- 设备结果原样拼接，不修改 `data/hash/sessionKey/thumbprint` 等已签名或已加密内容。
- 设备层的错误（`BiometricInfo.Error`、`DiscoveryResponse.Error`）原样保留；服务层只处理“无设备、设备抛异常、返回空结果”这类无法由设备表达的情况。

## 2. 新增文件

| 文件 | 职责 |
|---|---|
| `SbiAuthenticationBridge.Host/SbiServiceHandleBase.cs` | `internal abstract`：注入 `ISbiDeviceProvider`、`ISbiHostContext`、`ILoggerFactory`；实现 `ISbiServiceHandle` 的 `CallbackId`、`DiscoverAsync`、`GetDeviceInfoAsync`；提供用途筛选、稳定排序、异常映射、错误 DTO/错误 biometric 构造、结构化日志 |
| `SbiAuthenticationBridge.Host/SbiAuthenticationServiceHandle.cs` | `internal sealed`，实现 `ISbiAuthenticationServiceHandle`；`CaptureAsync` 按 `bio` 项路由到 `ISbiAuthenticationDevice` 并合并 `Biometrics` |
| `SbiAuthenticationBridge.Host/SbiRegistrationServiceHandle.cs` | `internal sealed`，实现 `ISbiRegistrationServiceHandle`；`CaptureAsync` 路由到 `ISbiRegistrationDevice`；`StreamAsync` 选择设备并透传流 |
| `SbiAuthenticationBridge.Host/SbiDeviceSelector.cs` | `internal static`：按 `DeviceId/Type/DeviceSubId` 从候选设备中选择目标；单设备快速路径 + 多设备探测路径 |
| `SbiAuthenticationBridge.Host/SbiLogBody.cs` | `internal static`：请求/响应 DTO 序列化为 JSON（Web 默认、忽略 null），超过 4096 字符截断并标记 |
| `SbiAuthenticationBridge.Host/SbiHostServiceCollectionExtensions.cs` | `public static IServiceCollection AddSbiHostServices(...)`，注册两个句柄为单例 |

> 实现类 internal；Host 对外只暴露 `AddSbiHostServices()`。

## 3. 修改文件

| 文件 | 改动 |
|---|---|
| `SbiAuthenticationBridge.Host/SbiAuthenticationBridge.Host.csproj` | 增加对 `SbiAuthenticationBridge.Core` 的 `ProjectReference`；增加 `Microsoft.Extensions.Logging.Abstractions`、`Microsoft.Extensions.DependencyInjection.Abstractions` 两个 `PackageReference` |
| `Directory.Packages.props` | 新增上述两个包的 `PackageVersion`（10.0.13） |
| `SbiAuthenticationBridge/SbiAuthenticationBridge.csproj` | 增加对 `SbiAuthenticationBridge.Host` 的 `ProjectReference`（组合根引用，arch 允许） |
| `SbiAuthenticationBridge/App.axaml.cs` | `ConfigureServices()` 中调用 `_serviceCollection.AddSbiHostServices();`（`Populate` 之前；日志管线已由 `LoggingBootstrapper.Configure` 注册） |

> 不修改 Host.Http：其现有 DI 桥接会从组合根解析到新实现；不修改 Device 项目。

## 4. 关键实现细节

### 4.1 设备枚举与排序

- `Devices` 是 `IReadOnlyDictionary<string, ISbiDevice>`，按 `Key` 的 `Ordinal` 顺序排序后取 `Value`，保证发现/信息响应顺序稳定。
- 认证句柄筛选 `device is ISbiAuthenticationDevice`；注册句柄筛选 `device is ISbiRegistrationDevice`。
- 同一设备若同时实现两个接口，会同时出现在两个服务面（符合用途语义）。

### 4.2 Discover / Info 聚合

- 遍历本用途设备，逐个调用 `ISbiDevice.DiscoverAsync(request, ct)` / `GetDeviceInfoAsync(ct)`。
- 结果直接追加；每个设备可返回多条。
- 异常处理：
  - `OperationCanceledException`：原样抛出（请求取消）。
  - `SbiException`：追加一条携带 `ex.Error` 的错误响应。
  - 其他异常：`LogError` 记录（含异常），追加一条 `SbiErrorHelper.Custom(500, "Device failure")` 的错误响应。
- 本用途无设备时返回空列表（由调用方决定语义）；Capture/Stream 无设备时按 4.3/4.4 返回错误。

### 4.3 Capture 路由与合并

- `ArgumentNullException.ThrowIfNull(request)`。
- `request.Bio` 为空：返回 `CaptureResponse { Biometrics = [] }`（不凭空发起设备采集）。
- 按 `Bio` 顺序逐项处理（顺序执行；设备本身是单操作门，天然串行）：
  1. `SbiDeviceSelector.SelectAsync` 选目标设备；
  2. 无目标：无任何本用途设备 → `NoDeviceConnected`（202）；有设备但未匹配 → `DeviceNotFound`（106）；追加错误 biometric 后继续下一项；
  3. 构造仅含当前 `BioInfo` 的子请求，复制 `Env/Purpose/SpecVersion/Timeout/CaptureTime/DomainUri/TransactionId/CustomOpts`；
  4. 调用 `CaptureAsync`；
  5. 追加返回的全部 `BiometricInfo`；返回空则补 `TechnicalErrorDuringExtraction`（102）；
  6. `SbiException` → 追加其 `Error`；其他异常 → `LogError` + 追加 `Custom(500, "Device failure")`；取消原样抛出。
- 错误 biometric 形态与设备层一致：`SpecVersion = "0.9.5"`、`Data/Hash` 空串、`Error` 有值。
- 不修改设备返回的任何已签名/加密字段。
- 说明：FAP20 对 `count>1` 的单个 bio 项会循环采集并返回多条 biometric；本实现按设备实际返回追加，不强制 1:1。

### 4.4 Stream 路由

- 目标设备选择同 4.3，使用 `StreamRequest.DeviceId` / `DeviceSubId`。
- 无设备：`SbiException(NoDeviceConnected)`；有设备但 DeviceId 未匹配：`SbiException(DeviceNotFound)`。
- 设备返回 `null` 流：`SbiException(Custom(500, "Device returned no stream"))`。
- 设备抛 `SbiException` 原样传播；其他异常包装为 `SbiException(Custom(500, "Device failure"), ex)`。
- 成功时原样返回设备流，不做分帧改写。

### 4.5 设备选择规则（`SbiDeviceSelector`）

- 候选数为 0 → 返回 `null`。
- 候选数为 1 → 直接返回该设备（由设备自身校验 `DeviceId/Type/DeviceSubId`，避免额外的发现调用和设备忙误判）。
- 候选数 > 1：
  - 对每个候选调用 `DiscoverAsync`（Capture 传 `Type = bio.Type`，Stream 传空请求），跳过带 `Error` 的响应；
  - 若请求带 `DeviceId`：匹配 `DeviceId` 或 `DeviceCode`（`OrdinalIgnoreCase`）；
  - 否则若请求带 `Type`：选择第一个对该类型无错误的设备；若同时带 `DeviceSubId`，优先选择 `DeviceSubId` 包含该值的设备；
  - 否则取第一个候选；
  - 无匹配 → `null`。
- 说明：多设备路由依赖设备发现响应；设备忙时发现可能返回 111，此时按未匹配处理。当前仓库只有一台 FAP20（认证用途），常用路径为单设备直通。

### 4.6 错误映射

- 统一通过 `SbiErrorHelper` 构造，不手写错误码。
- 服务层可表达的错误：
  - 无任何设备：`NoDeviceConnected`
  - 指定设备不存在：`DeviceNotFound`
  - 设备返回空结果/异常：`TechnicalErrorDuringExtraction`（采集）或 `Custom(500, "Device failure")`（其他）
- 设备自身返回的错误码/描述原样透传。

### 4.7 日志

- 基类构造注入 `ILoggerFactory`，用 `loggerFactory.CreateLogger(GetType())` 创建日志器；类别为具体句柄类型（`SbiAuthenticationServiceHandle` / `SbiRegistrationServiceHandle`）。
- 日志级别：
  - `Debug`：**每次操作的请求体与响应体**；Discover/Info 的请求类型与设备数；Capture 每项的选路结果（`type/deviceId/deviceSubId -> 设备`）；设备返回的正常错误码（如 101）；流打开/关闭细节。
  - `Information`：Capture 完成（bio 项数、返回条数）、Stream 打开、Discover/Info 聚合完成（设备数、结果数）。
  - `Warning`：无设备、设备未找到、设备发现失败、设备返回非成功错误。
  - `Error`：未预期异常（含堆栈），随后转为 SBI 错误记录。
- 请求/响应体覆盖范围：
  - Discover：`DiscoveryRequest` → `IReadOnlyList<DiscoveryResponse>`
  - Info：无请求体 → `IReadOnlyList<DeviceInfoResponse>`
  - Capture（认证/注册）：`CaptureRequest` → `CaptureResponse`
  - Stream：`StreamRequest` → `<stream: {ContentType}>`（不记录二进制内容）
- 序列化：`SbiLogBody` 使用 `JsonSerializerDefaults.Web` + `WhenWritingNull`，与 HTTP 报文形态一致；超过 4096 字符截断并标记 `...(truncated)`。
- 通过 `_logger.IsEnabled(LogLevel.Debug)` 包裹，未启用 Debug 时不做序列化。
- 日志经组合根已有的 Serilog 管线输出到 Console 与内存 LogView；Host 不直接引用 Serilog 或 Avalonia。
- 句柄为单例，`ILogger` 可长期持有；不缓存 `ILoggerFactory` 之外的运行时状态。

### 4.8 DI 装配

```csharp
public static IServiceCollection AddSbiHostServices(this IServiceCollection services)
{
    services.AddSingleton<ISbiAuthenticationServiceHandle, SbiAuthenticationServiceHandle>();
    services.AddSingleton<ISbiRegistrationServiceHandle, SbiRegistrationServiceHandle>();
    return services;
}
```

- 在 `App.ConfigureServices()` 中调用，早于 `Populate`；句柄为懒加载单例。
- `ISbiDeviceProvider` 仍由平台注册（Desktop 的 `FAP20DeviceProvider`）。
- Host.Http 的 `IServiceProvider` 桥接在请求时解析这两个句柄，无需改动。

## 5. 依赖链路与架构自查

- `Host → Core`：合法；未引用 Device、Host.Http、UI、Prism、Avalonia、HTTP。
- `SbiAuthenticationBridge → Host`：合法（arch 允许组合根引用 Host 做 DI 注册）；新增引用仅为注册扩展方法。
- `SbiAuthenticationBridge → Host.Http`：上一期已按平台宿主注册加入 Desktop，本次不改。
- Host 内无 HTTP 路由、无 ViewModel、无硬件互操作；只做设备结果聚合与 SBI DTO 构造。
- 无反向依赖、无跨层短路、无 ServiceLocator；句柄全部构造注入。
- 无空 `catch`；所有异常均记录日志并转换为 SBI 错误或向上抛出。
- 日志只依赖 `Microsoft.Extensions.Logging` 抽象；实现（Serilog）由组合根提供，符合 arch 分层。
- 新增 NuGet 仅限两个官方抽象包（已在第 0 节列出）；不修改 Core 契约。

## 6. 风险与回滚

| 风险 | 等级 | 说明 / 缓解 |
|---|---|---|
| Capture 结果不满足“1:1”文字约定 | 中 | FAP20 的 count>1 会返回多条；按设备真实结果追加以避免丢数据。若评审要求严格 1:1，需要改为按 bio 项截取/聚合，另议 |
| 多设备选择依赖发现调用 | 中 | 设备忙时发现可能失败；当前仓库单设备走直通路径。后续多设备可增加描述缓存 |
| 注册设备暂无实现 | 中 | 注册句柄逻辑完整，但当前只有认证 FAP20；`/rcapture`、`/stream` 会返回 `NoDeviceConnected`，属预期 |
| 新增 NuGet 抽象包 | 中 | 仅引入官方 abstractions（10.0.0，与现有 10.x 主版本一致）；不引入 Serilog 实现。回滚 = 移除 PackageReference 并改回 `Debug.WriteLine` |
| 日志噪声 | 低 | 高频路径用 `Debug`，Capture/Stream 用 `Information`，仅异常/无设备用 `Warning/Error`；日志进入现有 2000 条环形缓冲 |
| 请求/响应体含敏感数据 | 中 | 按需求记录完整 DTO 序列化结果，Capture 响应可能包含 `data/hash/sessionKey/thumbprint`；日志文件与内存缓冲需限制访问。后续可增加脱敏开关（不在本次范围） |
| 共享应用引用 Host | 低 | Host 为纯 .NET 库，不影响 Android/iOS/Browser 编译；移动端未注册 `ISbiDeviceProvider` 时，只有实际解析句柄才会失败 |
| 每项 Capture 复用完整 timeout | 低 | 多 bio 项时总耗时可能超过客户端预期；与设备层逐项执行的语义一致，暂不拆分超时 |
| 设备异常信息不可见 | 低 | 错误记录只携带标准错误码，详细信息仅 `Debug.WriteLine`；避免泄露内部细节 |

## 7. 验收对照

| spec 要求 | 实现点 |
|---|---|
| 实现两个 ServiceHandle | `SbiAuthenticationServiceHandle` / `SbiRegistrationServiceHandle` |
| 服务功能完整，返回可用响应 DTO | Discover/Info 合并、Capture 合并、Stream 透传；错误 DTO/biometric 齐全 |
| Http 宿主注入具体实现并准确路由 | `AddSbiHostServices()` 注册；Host.Http 的桥接解析到对应句柄，认证/注册路由互不串用 |
| 接入 ISbiDevice 并整合部分结果为完整响应 | 通过 `ISbiDeviceProvider` 遍历设备，按 `bio` 项路由，追加设备返回结果并补齐错误记录 |

## 8. 验证方式

1. `dotnet build` Host、Shared、Desktop（Desktop 用临时输出目录避开正在运行的进程锁）。
2. 临时控制台程序（不提交仓库）：
   - 注册两个假设备（认证 + 注册）到 `ISbiDeviceProvider`；
   - 通过 DryIoc `Populate` + `AddSbiHostServices()` 解析句柄；
   - 启动 Host.Http 两个服务，验证：
     - `/device`、`/info` 聚合两个设备的结果；
     - `/capture` 按 `deviceId` 路由到认证设备并合并多条 biometrics；
     - `/rcapture`、`/stream` 路由到注册设备；
     - 无匹配设备时返回 `DeviceNotFound`/`NoDeviceConnected`；
     - 设备抛异常时返回错误记录，不影响其他 bio 项；
     - 日志通过 MEL 输出到临时控制台，验证 Discover/Capture/Stream 的请求体、响应体、结构化字段与级别；确认超长响应被截断。
3. 用真实 `FAP20DeviceProvider` 启动 Desktop（若设备在线）验证认证链路；设备不在线时至少确认 DI 解析与错误响应，并确认日志出现在 LogView。
4. 逐文件对照 arch.md 依赖红线。

## 9. 不在本次范围

- 新增注册设备实现（`ISbiRegistrationDevice` 的 FAP20 适配）。
- 证书链校验、认证会话管理、DKL1 流程。
- FluentValidation 入参校验器。
- 多设备描述缓存/性能优化。
- Host.Http 层的原始 HTTP 报文日志（含 JSON 解析失败的请求）；本次记录的是 ServiceHandle 边界的 DTO 序列化结果。
- 日志脱敏开关。

---
**等待指令：收到【同意plan，开始编码】后开始实施。**
