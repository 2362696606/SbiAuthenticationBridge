# Plan:DeviceOperateView 与 DevicesView 的刷新关联

> 对应 spec：`specs/20261026-添加DeviceOperateView与DevicesView的刷新关联.spec.md`
> 采用 spec 建议的 Prism 事件聚合器；仅改 ViewModel，不改视图结构，不新增 NuGet，不改 Core。

## 0. 前置决策

| 项 | 决策 / 说明 |
|---|---|
| 通知机制 | Prism `IEventAggregator` + `PubSubEvent<DeviceConnectionChangedEventArgs>`（Prism 已在 `PrismApplicationBase` 注册 `IEventAggregator` 单例） |
| 事件载荷 | 设备提供者内部标识 `DeviceId` + 操作后的 `IsConnected`；`DevicesView` 用 `DeviceId` 定位对应列表项 |
| 发布时机 | `ConnectAsync` / `DisconnectAsync` 的 `finally` 中、`SynchronizeConnectionState` 之后发布；失败也发布，让列表读取真实状态 |
| 刷新范围 | 只刷新对应设备项（`DeviceItemViewModel.RefreshAsync`），不触发整个设备列表刷新 |
| 线程 | 事件发布在 UI 线程；`DevicesViewModel` 仍用 `Dispatcher.UIThread` 防御跨线程场景 |
| 生命周期 | `DevicesViewModel.Dispose` 中取消订阅，避免容器/视图重建后重复刷新 |
| 日志 | `DevicesViewModel` 注入 `ILogger<DevicesViewModel>`；后台刷新异常记 `Warning`，取消记 `Debug` |

## 1. 方案总览

```
DeviceOperateView
  └─ DeviceOperateViewModel.ConnectAsync / DisconnectAsync
        └─ finally: Publish(DeviceConnectionChangedEvent(DeviceId, IsConnected))
                          │
                          ▼
                   IEventAggregator
                          │
                          ▼
DevicesViewModel.OnDeviceConnectionChanged
  ├─ FindDeviceItem(DeviceId)
  └─ DeviceItemViewModel.RefreshAsync()
        ├─ IDevice.IsConnected 同步
        └─ DiscoverAsync → 设备状态/信息刷新
```

- 认证/注册服务、设备选择、日志等均不受影响。
- 事件只承载“哪个设备需要刷新”，实际状态由 `DeviceItemViewModel` 重新读取，避免消息与真实状态不一致。

## 2. 新增文件

| 文件 | 职责 |
|---|---|
| `SbiAuthenticationBridge/Events/DeviceConnectionChangedEvent.cs` | `PubSubEvent<DeviceConnectionChangedEventArgs>` 定义 + 事件参数 `DeviceId` / `IsConnected`（presentation 层事件，不进 Core） |

## 3. 修改文件

| 文件 | 改动 |
|---|---|
| `SbiAuthenticationBridge/ViewModels/DeviceOperateViewModel.cs` | 注入 `IEventAggregator`；在 `ConnectAsync` / `DisconnectAsync` 的 `finally` 中发布连接状态变化事件（使用操作开始时捕获的 `DeviceId`） |
| `SbiAuthenticationBridge/ViewModels/DevicesViewModel.cs` | 注入 `IEventAggregator` 与 `ILogger<DevicesViewModel>`；构造时订阅事件；收到事件后按 `DeviceId` 找到对应 `DeviceItemViewModel` 并调用 `RefreshAsync`；`Dispose` 中取消订阅 |

> 不修改 `DevicesView.axaml` / `DeviceOperateView.axaml`：现有绑定已展示 `Status`、`IsConnected`、`ErrorInfo` 等字段。

## 4. 关键实现细节

### 4.1 事件定义

```csharp
public sealed class DeviceConnectionChangedEvent
    : PubSubEvent<DeviceConnectionChangedEventArgs>;

public sealed record DeviceConnectionChangedEventArgs(string DeviceId, bool IsConnected);
```

- `DeviceId` 使用 `DeviceOperateViewModel.SelectedDeviceId`（设备提供者内部标识），与 `DevicesViewModel` 列表项的 `InnerDeviceId` 对应。
- 事件参数包含 `IsConnected` 便于后续订阅方直接使用；本次 `DevicesView` 仍以重新读取为准。

### 4.2 发布点

- `ConnectAsync`：
  - 保持现有流程；
  - 在 `finally` 中 `SynchronizeConnectionState(selectedDeviceId)` 之后发布：
    ```csharp
    PublishConnectionChanged(selectedDeviceId, device.IsConnected);
    ```
- `DisconnectAsync`：同样在 `finally` 中发布。
- 操作期间选择可能变化：使用操作开始时捕获的 `selectedDeviceId` 与 `device`，确保刷新的是实际执行连接/断开的设备。
- `DeviceId` 为空时不发布。
- 失败也发布：列表刷新后显示设备真实状态（未连接/未就绪/错误），不会残留旧状态。

### 4.3 订阅与刷新

- `DevicesViewModel` 构造函数：
  ```csharp
  _eventAggregator.GetEvent<DeviceConnectionChangedEvent>()
      .Subscribe(OnDeviceConnectionChanged);
  ```
- 处理：
  ```csharp
  private void OnDeviceConnectionChanged(DeviceConnectionChangedEventArgs args)
  {
      if (string.IsNullOrEmpty(args.DeviceId)) return;

      if (Dispatcher.UIThread.CheckAccess())
          _ = RefreshDeviceAsync(args.DeviceId);
      else
          Dispatcher.UIThread.Post(() => _ = RefreshDeviceAsync(args.DeviceId));
  }
  ```
- `RefreshDeviceAsync`：
  - 在 UI 线程内 `FindDeviceItem(deviceId)`；设备已不在列表时直接返回；
  - `await item.RefreshAsync(CancellationToken.None)`；
  - `OperationCanceledException` 记 `Debug`；其他异常记 `Warning`（`DeviceItemViewModel.RefreshAsync` 已自行把设备错误写入 `ErrorInfo`，这里只兜底）。
- 若列表正在全量刷新（`IsRefreshing`），`DeviceItemViewModel.RefreshAsync` 的 `IsLoading` 会阻止同一项并发刷新，事件不会造成重复请求。
- 设备请求可能返回 `DeviceBusy`：列表项按现有逻辑显示错误，属于预期。

### 4.4 生命周期

- `DevicesViewModel.Dispose()` 增加：
  ```csharp
  _eventAggregator.GetEvent<DeviceConnectionChangedEvent>()
      .Unsubscribe(OnDeviceConnectionChanged);
  ```
- 保留现有 `IDeviceSelectionService.SelectionChanged` 退订逻辑。
- `DeviceOperateViewModel` 无新增订阅，不需要额外释放。

## 5. 依赖链路与架构自查

- 事件与两个 ViewModel 均位于 `SbiAuthenticationBridge`（表现层），不进入 Core。
- 依赖 Prism 事件聚合器（项目已有 Prism.DryIoc.Avalonia），不新增 NuGet。
- 不直接引用 Device/Host 实现；连接状态通过 Core 的 `IDevice` 读取。
- 无 ServiceLocator、无反向依赖；构造函数注入 `IEventAggregator` 与 `ILogger<T>`。
- 无空 catch；取消与异常均有日志。

## 6. 风险与回滚

| 风险 | 等级 | 说明 / 缓解 |
|---|---|---|
| 事件刷新触发设备发现，可能与正在进行的采集冲突 | 低 | `DeviceItemViewModel` 的 `IsLoading` 防重入；设备忙时按现有错误展示，不重试 |
| 连接失败仍发布事件 | 低 | 刷新后展示真实状态，避免旧状态残留；符合“连接/断开时刷新”的语义 |
| 选择在操作期间变化 | 低 | 使用操作开始时捕获的 `DeviceId` 与设备引用，刷新实际操作的设备项 |
| 事件订阅泄漏 | 低 | `Dispose` 显式退订；方法组订阅可被 `Unsubscribe` 精确移除 |
| `IEventAggregator` 未注册 | 低 | Prism 已默认注册；实现后通过运行时解析验证 |

## 7. 验收对照

| spec 要求 | 实现点 |
|---|---|
| 连接设备时 DevicesView 刷新对应设备 | `ConnectAsync` finally 发布事件 → `DevicesViewModel` 调 `RefreshAsync` |
| 断开设备时刷新对应设备 | `DisconnectAsync` 同样发布 |
| 使用 Prism 事件聚合器/通知 | `IEventAggregator` + `PubSubEvent<T>` |
| 只刷新对应设备 | 按 `DeviceId` 定位 `DeviceItemViewModel`，不做全量刷新 |

## 8. 验证方式

1. `dotnet build` Shared / Desktop（避开当前运行进程的文件锁）。
2. 临时程序（不提交仓库）：
   - 使用 `EventAggregator` + 假 `ISbiDeviceProvider` / 假设备构建设备列表；
   - 发布 `DeviceConnectionChangedEvent`，验证对应 `DeviceItemViewModel` 的连接状态/状态文本被刷新，其他项不受影响；
   - 验证未知 `DeviceId` 不抛异常。
3. 启动 Desktop：
   - 选择设备 → DeviceOperateView 连接 → 左侧设备卡片的连接状态/状态无需手动刷新即更新；
   - 断开 → 左侧对应卡片同步变为“未连接/未就绪”；
   - 多设备时确认只有目标设备刷新。
4. 逐文件对照 arch.md 依赖红线。

## 9. 不在本次范围

- 设备状态变更的全局广播（如采集过程中状态变化）。
- DevicesView 的全量刷新优化/增量订阅。
- 事件持久化或跨进程通知。

---
**等待指令：收到【同意plan，开始编码】后开始实施。**
