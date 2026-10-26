# spec:DeviceOperateView与DevicesView的刷新关联

# 目标
当DeviceOperateView连接设备时，DevicesView应刷新对应的设信息

## 实现方案
可以通过Prism时间聚合器关联或通知

## 验收标准
- 在DeviceOperateView连接设备或断开设备时，DevicesView中对应的设备应刷新状态