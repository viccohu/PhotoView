# 图片拖动功能 - 混合实现方案（结合官方最佳实践）

## 问题分析

根据 GitHub issue #9818 和官方文档，WinUI 3 ScrollViewer 的限制：
- ✅ **支持触控/手写笔拖动**（内置平移功能）
- ✅ **支持拖动滚动条滑块**
- ✅ **支持鼠标滚轮滚动**
- ❌ **不支持鼠标左键按住拖动**（这是我们需要的主要功能！）

## 混合实现方案

结合官方最佳实践和自定义实现：
1. **启用 ScrollViewer 内置平移模式** - 让触控/手写笔正常工作
2. **自定义鼠标左键拖动** - 补充官方缺失的功能
3. **保持滚轮缩放功能** - 与现有功能兼容

---

## [ ] 任务 1: 启用 ScrollViewer 的内置平移模式
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 修改 XAML：设置 HorizontalScrollMode 和 VerticalScrollMode 为 Enabled
  - 禁用轨道限制（IsHorizontalRailEnabled/IsVerticalRailEnabled = false）
  - 保持滚动条可见性为 Disabled
  - 保持 ZoomMode = Enabled
- **Success Criteria**:
  - ScrollViewer 内置平移功能正常工作（触控/手写笔）
- **Test Requirements**:
  - `programmatic` TR-1.1: 验证 ScrollMode 已设置为 Enabled
  - `human-judgement` TR-1.2: 验证触控可以拖动图片

## [ ] 任务 2: 实现鼠标左键拖动（自定义）
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 保留拖动状态变量
  - 保留 PointerPressed/PointerMoved/PointerReleased/PointerCanceled 事件
  - 在 PointerPressed 中仅对鼠标左键触发
  - 使用代码方式 AddHandler 注册事件（handledEventsToo=true）
  - 实现边界感知：仅对超出视区的轴允许拖动
- **Success Criteria**:
  - 鼠标左键可以拖动图片
- **Test Requirements**:
  - `human-judgement` TR-2.1: 验证鼠标左键可以拖动
  - `human-judgement` TR-2.2: 验证未超出视区的轴无法拖动

## [ ] 任务 3: 确保与内置平移共存
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 
  - 在 PointerPressed 中检查 PointerDeviceType
  - 仅对 Mouse 类型的指针触发自定义拖动
  - 对 Touch/Pen 类型的指针不处理，让内置平移接管
- **Success Criteria**:
  - 鼠标用自定义拖动，触控用内置平移
- **Test Requirements**:
  - `human-judgement` TR-3.1: 验证触控使用内置平移
  - `human-judgement` TR-3.2: 验证鼠标使用自定义拖动

## [ ] 任务 4: 保持滚轮缩放功能
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 保持现有的 PointerWheelChanged 滚轮缩放功能
  - 确保与平移功能兼容
- **Success Criteria**:
  - 滚轮缩放和拖动都能正常工作
- **Test Requirements**:
  - `human-judgement` TR-4.1: 验证滚轮缩放功能正常
  - `human-judgement` TR-4.2: 验证缩放后可以拖动
