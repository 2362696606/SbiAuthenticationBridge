# Architecture Constitution
> 所有代码生成、修改必须遵守本文件；AI不得自行修改本文件。

## 技术栈
- Runtime: .NET 10
- UI: Avalonia UI 12.1，MVU
- DI: Prism.DryIoc.Avalonia
- 跨进程/外部调用：Kestrel 内嵌 HTTP 服务、原生C++/SO DLL互操作
- 校验：FluentValidation
- 日志：Serilog，结构化日志

## 程序集结构 & 逻辑分层、依赖规则
> 本仓库采用「Core抽象底座 + 独立基础设施实现包 + 业务组合根 + 平台入口」的分包模式。
> 区分两类依赖：【DI组装依赖】（仅组合根注册时使用）、【业务调用依赖】（业务逻辑代码）
> 黄金总原则：Core 不依赖任何实现包；业务逻辑代码只依赖 Core 中定义的抽象，不直接依赖基础设施实现；仅组合根允许引用基础设施包用于服务注册。

### 项目清单与职责
1. **SbiAuthenticationBridge.Core**
   逻辑分层：Domain + Application 契约层（稳定抽象底座）
   职责：
   - 领域实体、ValueObject、领域事件
   - 全部业务接口抽象：IDeviceProvider, IBiometricHostService 等
   - Command / Query、DTO、校验器定义、业务异常定义
   - 领域业务规则
     允许引用：无内部业务实现项目；仅基础库（Microsoft.Extensions.*, FluentValidation等）
     禁止：任何硬件实现、HTTP实现、Avalonia UI、具体Repository实现、DLL互操作实现
     👉 所有其他项目都优先依赖本项目的抽象。

2. **SbiAuthenticationBridge.Device**
   逻辑分层：Infrastructure（硬件设备实现）
   职责：虹膜设备、MOSIP生物识别底层驱动、原生DLL互操作、设备会话实现
   允许引用：SbiAuthenticationBridge.Core
   禁止：UI、业务编排逻辑、Host HTTP服务实现
   对外暴露：仅实现 Core 定义的接口；不向外暴露底层硬件细节类型

3. **SbiAuthenticationBridge.Host**
   逻辑分层：Infrastructure（宿主核心实现）
   职责：身份认证会话管理、证书链校验、底层认证流程实现
   允许引用：SbiAuthenticationBridge.Core
   禁止：UI、Avalonia、HTTP路由、设备上层业务编排

4. **SbiAuthenticationBridge.Host.Http**
   逻辑分层：Infrastructure（HTTP网关实现）
   职责：Kestrel内嵌HTTP接口、请求路由、协议序列化
   允许引用：SbiAuthenticationBridge.Core、SbiAuthenticationBridge.Host
   禁止：ViewModel、UI、平台相关代码

5. **SbiAuthenticationBridge**
   逻辑分层：Application业务编排 + Presentation视图/ViewModel + **DI组合根**
   职责：
   - View、ViewModel、页面路由、用户交互状态（Avalonia MVU）
   - 业务流程编排、CQRS Handler实现
   - 全局DI容器注册、把 Core 接口绑定到 Device/Host/Host.Http 的实现（**这里是唯一允许引用基础设施实现包的地方**）
     允许引用：
      - SbiAuthenticationBridge.Core（抽象，业务调用）
      - SbiAuthenticationBridge.Device、SbiAuthenticationBridge.Host、SbiAuthenticationBridge.Host.Http（**仅限在模块注册/DI注册代码中使用**）
        强禁令：
      - ViewModel / Handler / 业务逻辑代码 **禁止直接 using Device/Host/Host.Http 内部实现类**
      - 业务代码只能通过 Core 中的接口进行调用
      - 所有基础设施实例注入，通过构造器注入 Core 接口，不直接注入实现类型

6. **SbiAuthenticationBridge.Desktop**
   逻辑分层：平台入口壳（Avalonia桌面启动层，极薄）
   职责：Program入口、App构建、窗口启动、平台特定初始化
   允许引用：SbiAuthenticationBridge
   禁止：编写业务、直接引用Core/Host.XXX以外的基础设施包、新增ViewModel/页面

7. **SbiAuthenticationBridge.Android**
   逻辑分层：平台入口壳（Android启动层，极薄）
   职责：Program入口、App构建、窗口启动、平台特定初始化
   允许引用：SbiAuthenticationBridge
   禁止：编写业务、直接引用Core/Host.XXX以外的基础设施包、新增ViewModel/页面

### 依赖红线汇总（AI必须自查）
✅ 合法：
- Device / Host / Host.Http → Core
- SbiAuthenticationBridge → Core（业务调用）
- SbiAuthenticationBridge → Device/Host
- Desktop → SbiAuthenticationBridge/Host.Http（注册特定平台服务宿主）

❌ 违规，禁止：
- Core 反向依赖 Device / Host / Host.Http / SbiAuthenticationBridge
- ViewModel / Handler 直接 new / 直接引用 Device、Host 里面的实现类
- Desktop 直接引用 Device、Host
- Device 依赖 Host.Http，Host.Http 依赖 ViewModel 等跨域乱依赖
- 在Core中写任何具体硬件、HTTP、平台相关实现

### 配套编码约定
1. 基础设施包对外只提供 `AddXXX()` 扩展方法，给组合根注册；实现类internal，尽量不对外可见
2. Handler、ViewModel 的构造函数入参，全部是 Core 中定义的接口（Ixxx），不能是实现类
3. 新增基础设施实现，只需要新增项目、新增Add扩展；SbiAuthenticationBridge注册代码更新，业务代码（ViewModel/Handler）不需要改动


## 通用编码规约
1. 命名
    - Entity：PascalCase；DTO/Command/Query 后缀统一：XXXDto / XXXCommand / XXXQuery
    - 接口 IXXX；实现 XXXRepository / XXXService
2. 异常
    - 禁止空 `catch { }`；禁止只打日志不处理
    - 业务异常：抛自定义 `BusinessException`，携带错误码+消息
    - 基础设施异常：`InfrastructureException`，区分硬件/IO/数据库错误
3. 入参校验
    - 所有 Command / DTO 必须配套 FluentValidation；边界入口不依赖运行时类型
4. DI
    - 所有服务、仓库走构造注入；禁止 ServiceLocator 模式
5. 外部原生DLL / 硬件调用
    - 全部封装在 Infrastructure，统一隔离；不散落各处
6. 测试
    - Domain 优先单元测试；Application 写集成测试；Presentation 尽量少写复杂测试

## AI 禁令清单（硬约束）
- [ ] 不允许新增跨层直接调用
- [ ] 不允许新增双向依赖
- [ ] 不允许注释屏蔽异常、Lint、类型警告来绕问题
- [ ] 不允许一次性大规模重构多个模块，必须按模块切片
- [ ] 不允许引入NuGet包，除非在spec中明确审批
- [ ] 改动核心接口/领域实体，必须在plan中标高风险，等待人工确认
