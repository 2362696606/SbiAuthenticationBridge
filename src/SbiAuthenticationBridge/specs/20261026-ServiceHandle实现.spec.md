# Spec:ServiceHandle实现

## 目标
实现ISbiRegistrationServiceHandle，ISbiAuthenticationServiceHandle，对接宿主层与设备层

## 验收标准
- 服务功能完整，能直接为宿主层返回可用的响应Dto
- Http宿主注入具体实现，能准确路由到具体方法
- ServiceHandle接入ISbiDevice,将收到的部分设备结果整合为完整Sbi响应Dto