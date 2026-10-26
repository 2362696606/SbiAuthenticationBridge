# Plan:实现 ServiceOperateView

> 对应 spec：`specs/20261026-实现ServiceOperateView.spec.md`
> 遵循 `arch.md`：ViewModel 只依赖 Core 抽象，不引用设备/宿主实现；端口等 HTTP 细节通过 `ISbiHttpService` 抽象判断；不新增 NuGet。

## 0. 前置决策

| 项 | 决策 / 说明 |
|---|---|
| 面板位置（已确认） | 右栏顶部固定行：`ServiceOperateView` 在 `DeviceOperateView` 上方，日志面板与分隔条保持原位置 |
| 服务抽象 | 注入 `IEnumerable<ISbiAuthenticationService>` / `IEnumerable<ISbiRegistrationService>`，取第一个。桌面端由 `AddSbiHttpServices()` 各注册一个；Android/iOS 暂无宿主实现时集合为空，面板显示“当前平台未提供”，避免 ViewModel 构造失败 |
| 端口来源 | 仅当服务实例实现 `ISbiHttpService` 时显示端口设置；非 HTTP 宿主隐藏端口 |
| 默认端口 | Authentication = 4501，Registration = 4502；若服务已有非 0 端口则沿用 |
| 端口校验 | `NumericUpDown` 限制 4501–4600；ViewModel 启动前再次校验；运行中端口输入只读 |
| 状态同步 | 首次附加视图时调用 `Refresh()` 读取服务当前 `IsRunning`/`Port`；之后由面板自身启停维护状态 |
| 日志 | 注入 `ILoggerFactory`，启动/停止记 `Information`，失败记 `Error`，经现有 Serilog 管线进入 LogView |
| 窗口尺寸 | `MainWindow` 增加默认 `Width="1200" Height="800"`，避免新增固定行后挤压设备操作区与日志区 |

## 1. 方案总览

```
ServiceOperateView (AutoWireViewModel)
 └─ ServiceOperateViewModel
      ├─ Authentication : SbiServiceControlViewModel
      │    ├─ ISbiAuthenticationService?（宿主无关，启动/停止）
      │    └─ ISbiHttpService?（可选，端口）
      └─ Registration   : SbiServiceControlViewModel
           ├─ ISbiRegistrationService?
           └─ ISbiHttpService?
```

- 两个服务控制复用同一个 `SbiServiceControlViewModel`，视图通过 `DataTemplate` 渲染两行。
- 每个控制独立维护：可用性、运行状态、端口、忙碌状态、错误信息、启停命令。
- 认证与注册互不影响，可分别启停；端口冲突由后启动者报错并显示在该控制行内。

## 2. 新增文件

| 文件 | 职责 |
|---|---|
| `SbiAuthenticationBridge/ViewModels/SbiServiceControlViewModel.cs` | 单个 SBI 服务控制：服务可用性、`IsRunning`、`Port`（仅 HTTP）、端口校验、启停命令、状态/错误文本、日志 |

## 3. 修改文件

| 文件 | 改动 |
|---|---|
| `SbiAuthenticationBridge/ViewModels/ServiceOperateViewModel.cs` | 替换空类：注入两个服务集合与 `ILoggerFactory`，创建 `Authentication` / `Registration` 两个控制；提供 `Refresh()` |
| `SbiAuthenticationBridge/Views/ServiceOperateView.axaml` | 完整视图：标题 + 两个服务控制的 `DataTemplate`（名称、状态、端口、启动、停止、校验/错误提示） |
| `SbiAuthenticationBridge/Views/ServiceOperateView.axaml.cs` | 附加到可视树时从容器解析 ViewModel 并调用一次 `Refresh()`（沿用现有 View 的模式） |
| `SbiAuthenticationBridge/Views/MainView.axaml` | 右栏 `RowDefinitions` 改为 `Auto,*,4,240`：Row0 服务操作、Row1 设备操作、Row2 分隔条、Row3 日志 |
| `SbiAuthenticationBridge/Views/MainWindow.axaml` | 增加默认 `Width="1200" Height="800"` |

## 4. 关键实现细节

### 4.1 `SbiServiceControlViewModel`

- 构造参数：`string displayName`、`ISbiService? service`、`int defaultPort`、`ILoggerFactory loggerFactory`。
- 若 `service is null`：
  - `IsAvailable = false`，`StatusText = "当前平台未提供"`；
  - 启动/停止命令均不可执行，端口隐藏。
- 若 `service is ISbiHttpService httpService`：
  - `HasPort = true`；
  - 初始端口：`httpService.Port` 在 4501–4600 内则沿用，否则使用 `defaultPort` 并写回服务；
  - 端口输入仅在 `!IsRunning && !IsBusy` 时可编辑。
- 状态属性：
  - `IsRunning`（observable）
  - `IsBusy`（observable）
  - `Port`（observable，`int`）
  - `ErrorMessage` / `HasError`
  - `StatusText`：不可用 / 已停止 / 运行中（端口） / 启动中 / 停止中
  - `PortValidationMessage` / `HasPortValidationError`
- 命令：
  - `StartCommand`：`CanExecute = IsAvailable && !IsRunning && !IsBusy && IsPortValid`
    - 启动前把 `Port` 写回 `ISbiHttpService`；
    - `await service.StartAsync()`；
    - 成功后从 `httpService.Port` 回读实际端口，更新 `IsRunning`；
    - 异常写入 `ErrorMessage` 并记录 `Error` 日志。
  - `StopCommand`：`CanExecute = IsAvailable && IsRunning && !IsBusy`
    - `await service.StopAsync()`；成功后更新 `IsRunning`；异常同样处理。
  - 命令执行期间 `IsBusy = true`，结束后刷新 CanExecute。
- `Refresh()`：读取 `service.IsRunning` 与 `httpService.Port`，用于视图首次附加时同步外部已启动的宿主。
- 不缓存 `CallbackId`、不直接引用 `SbiHttpService` 实现类。

### 4.2 端口校验

- 视图 `NumericUpDown`：`Minimum=4501`、`Maximum=4600`、`Increment=1`、`FormatString="0"`、`IsEnabled={Binding CanEditPort}`、`IsVisible={Binding HasPort}`。
- ViewModel：`IsPortValid => !HasPort || Port is >= 4501 and <= 4600`；非法时显示“端口须在 4501–4600 之间”，并禁用启动。
- 运行中端口不可改，避免与服务内部“运行期间禁止改端口”的约束冲突。
- 端口冲突（两服务同一端口）由 `StartAsync` 抛出的绑定异常体现，显示在对应控制行，不影响另一服务。

### 4.3 `ServiceOperateViewModel`

```csharp
public ServiceOperateViewModel(
    IEnumerable<ISbiAuthenticationService> authenticationServices,
    IEnumerable<ISbiRegistrationService> registrationServices,
    ILoggerFactory loggerFactory)
{
    Authentication = new SbiServiceControlViewModel(
        "认证服务", authenticationServices.FirstOrDefault(), 4501, loggerFactory);
    Registration = new SbiServiceControlViewModel(
        "注册服务", registrationServices.FirstOrDefault(), 4502, loggerFactory);
}
```

- `IEnumerable<T>` 在无注册时解析为空集合（DryIoc / MS DI 均支持），移动端不会因缺少宿主实现而构造失败。
- 不注册 ViewModel：沿用现有 Prism/DryIoc 具体类型解析模式（与 `DevicesViewModel`、`DeviceOperateViewModel` 一致）。

### 4.4 视图布局

- `ServiceOperateView`：`StackPanel`（间距 8）包含标题与两个 `ContentControl`；每个控制用共享 `DataTemplate` 渲染为紧凑单行：
  - 左：服务名称 + 状态文本
  - 中：端口（仅 HTTP 宿主）
  - 右：启动 / 停止按钮
  - 下方：端口校验提示 / 错误提示（按需显示）
- 两个控制上下排列，固定行整体高度尽量小；`DeviceOperateView` 保持 `MinHeight=200`。
- `MainView` 右栏：
  ```xml
  <Grid RowDefinitions="Auto,*,4,240">
      <views:ServiceOperateView />
      <views:DeviceOperateView Grid.Row="1" MinHeight="200" />
      <GridSplitter Grid.Row="2" ... />
      <Border Grid.Row="3"> <views:LogView /> </Border>
  </Grid>
  ```

### 4.5 视图生命周期

- `ServiceOperateView.axaml` 增加 `mvvm:ViewModelLocator.AutoWireViewModel="True"`。
- 代码后置沿用 `DevicesView`/`DeviceOperateView` 模式：
  - `OnAttachedToVisualTree` 中 `DataContext ??= ContainerLocator.Container.Resolve<ServiceOperateViewModel>()`；
  - 首次附加时调用一次 `Refresh()`（只执行一次，避免重复读取）。
- 设计模式下跳过容器解析。

### 4.6 日志

- 每个控制通过 `loggerFactory.CreateLogger("SbiServiceControlViewModel")` 获取日志器，消息中带 `DisplayName`。
- 启动成功：`Information`，包含服务名与端口。
- 停止成功：`Information`。
- 启停失败：`Error`（含异常），同时显示在界面错误行。
- 不记录敏感数据。

## 5. 依赖链路与架构自查

- ViewModel 只引用 `SbiAuthenticationBridge.Core`（`ISbiService`、`ISbiHttpService`、`ISbiAuthenticationService`、`ISbiRegistrationService`）与 `Microsoft.Extensions.Logging`（已有依赖）；不引用 Host/Host.Http/Device 实现。
- 端口通过 `ISbiHttpService` 抽象访问，非 HTTP 宿主自然隐藏。
- 无 ServiceLocator（View 代码后置的容器解析沿用现有模式，仅用于视图装配）。
- 无新增 NuGet、无 Core 契约变更、无数据库/IO 操作。
- 移动端无宿主实现时面板降级为“当前平台未提供”，不影响其余界面。

## 6. 风险与回滚

| 风险 | 等级 | 说明 / 缓解 |
|---|---|---|
| `NumericUpDown.Value`（decimal?）绑定 `int` 属性 | 低 | Avalonia 默认绑定转换支持数值互转；实现后实际运行验证，若失败改为 `decimal?` 属性 |
| 默认窗口高度下空间紧张 | 中 | 服务控制行设计紧凑，并将 MainWindow 默认尺寸调整为 1200×800；仍可拖动分隔条/调整窗口 |
| 外部启动/停止不同步 | 低 | 视图首次附加刷新一次；后续由本面板维护状态。服务接口暂无状态变更事件，如需实时同步另立工单 |
| 两个服务使用同一端口 | 低 | 后启动者绑定失败，错误显示在对应行，已运行服务不受影响 |
| 端口设置不持久化 | 低 | 本期使用默认端口（4501/4502）；持久化到 AppSetting 另立工单 |
| 移动端显示不可用 | 低 | 预期行为：Android/iOS 尚无 HTTP/Intent 宿主实现，面板禁用并提示 |

## 7. 验收对照

| spec 要求 | 实现点 |
|---|---|
| 包含 Registration 与 Authentication 两个服务控制 | `ServiceOperateViewModel.Authentication` / `Registration` |
| 单个控制包含启动/停止按钮 | `StartCommand` / `StopCommand`，按状态启用 |
| HTTP 宿主包含端口号设置 | `ISbiHttpService` 检测，`HasPort` 控制显示 |
| 端口验证，范围 4501–4600 | `NumericUpDown` Min/Max + VM `IsPortValid` + 启动前校验 |

## 8. 验证方式

1. `dotnet build` Shared / Desktop（Desktop 用临时输出目录避开运行中进程锁）。
2. 启动 Desktop：
   - 服务面板显示认证/注册两个控制，默认端口 4501/4502；
   - 启动认证服务，确认按钮状态、状态文本、日志与 `http://127.0.0.1:4501` 可访问；
   - 启动注册服务（4502），确认两服务可同时运行；
   - 停止服务，确认端口释放；
   - 端口输入 4500/4601 时启动被禁用并显示校验提示；
   - 两服务设为同一端口时，后启动者显示绑定错误，前者不受影响；
   - 运行中端口输入只读。
3. 确认移动端编译通过；无宿主实现时面板显示“当前平台未提供”。
4. 逐文件对照 arch.md 依赖红线。

## 9. 不在本次范围

- 端口配置持久化（AppSetting）。
- 服务状态变更事件/实时外部同步。
- Android/iOS 的 Intent 宿主实现。
- 服务运行日志级别、请求统计等运维功能。

---
**等待指令：收到【同意plan，开始编码】后开始实施。**
