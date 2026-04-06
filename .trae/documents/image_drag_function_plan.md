# 图片拖动功能实现计划

## 设计方案（基于官方文档）
根据 WinUI 3 ScrollViewer 官方文档，最佳实现方案是：
- **保持 ScrollViewer 滚动模式为 Disabled**（避免与自定义滚轮缩放冲突）
- 通过自定义 Pointer 事件（PointerPressed/PointerMoved/PointerReleased）实现拖动功能
- 使用 ScrollViewer.ChangeView() 编程控制滚动位置
- 实现边界感知：仅当图片尺寸超出视区时，对应轴才可拖动

---

## [x] 任务 1: 添加拖动状态变量
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 添加私有字段记录拖动状态：是否正在拖动、起始指针位置、起始滚动位置
- **Success Criteria**:
  - 拖动状态变量正确定义
- **Test Requirements**:
  - `programmatic` TR-1.1: 验证添加了必要的私有字段
- **Notes**: 确保线程安全（UI 线程访问）

## [x] 任务 2: 注册 Pointer 事件处理
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 在 XAML 中为 ImageScrollViewer 添加 PointerPressed、PointerMoved、PointerReleased、PointerCanceled 事件
  - 在代码中实现这些事件处理程序
- **Success Criteria**:
  - Pointer 事件正确注册和触发
- **Test Requirements**:
  - `human-judgement` TR-2.1: 验证按下鼠标/触摸时事件被触发
- **Notes**: 使用 AddHandler 确保即使其他控件标记为 Handled 也能接收事件

## [x] 任务 3: 实现边界感知拖动逻辑
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 
  - 在 PointerPressed 中捕获指针、记录起始位置、捕获指针
  - 在 PointerMoved 中：
    - 检查当前图片是否超出视区（水平/垂直方向）
    - 仅对超出视区的轴计算拖动偏移
    - 使用 ChangeView() 更新滚动位置，保持在边界内
  - 在 PointerReleased/PointerCanceled 中释放指针捕获、重置状态
- **Success Criteria**:
  - 只有超出视区的轴可以拖动
  - 拖动不会超出图片边界
- **Test Requirements**:
  - `human-judgement` TR-3.1: 验证未超出视区的轴无法拖动
  - `human-judgement` TR-3.2: 验证超出视区的轴可以拖动且不超出边界

## [x] 任务 4: 重置拖动状态
- **Priority**: P1
- **Depends On**: 任务 2
- **Description**: 
  - 在 PrepareContent 方法中重置拖动状态
  - 确保切换图片时不会继承之前的拖动状态
- **Success Criteria**:
  - 切换图片时拖动状态正确重置
- **Test Requirements**:
  - `programmatic` TR-4.1: 验证 PrepareContent 方法中重置了相关变量
- **Notes**: 确保与现有的缩放重置逻辑兼容
