# 图片拖动功能 - 官方最佳实践实现方案

## 官方最佳实践要点

根据 WinUI 3 官方文档，ScrollViewer 提供了内置的平移（panning）功能，我们应该利用这些功能而不是完全自定义实现：

### 核心建议
1. **启用 ScrollViewer 的平移模式** - 设置 `HorizontalScrollMode` 和 `VerticalScrollMode` 为 `Enabled`
2. **使用自由平移（Freeform）模式** - 对于图片查看器，禁用轨道限制（`IsHorizontalRailEnabled`/`IsVerticalRailEnabled` = `false`）
3. **保持滚动条隐藏** - 设置 `HorizontalScrollBarVisibility` 和 `VerticalScrollBarVisibility` 为 `Disabled`
4. **对于边界感知** - 利用内置功能，ScrollViewer 自动限制在边界内

---

## [ ] 任务 1: 启用 ScrollViewer 的内置平移功能
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 修改 XAML：设置 HorizontalScrollMode 和 VerticalScrollMode 为 Enabled
  - 禁用轨道限制（IsHorizontalRailEnabled/IsVerticalRailEnabled = false）
  - 保持滚动条可见性为 Disabled
  - 从 XAML 中移除自定义 Pointer 事件绑定
- **Success Criteria**:
  - ScrollViewer 内置平移功能正常工作
- **Test Requirements**:
  - `programmatic` TR-1.1: 验证 ScrollMode 已设置为 Enabled
  - `programmatic` TR-1.2: 验证 RailEnabled 已设置为 false
  - `human-judgement` TR-1.3: 验证可以通过鼠标/触摸拖动图片

## [ ] 任务 2: 移除自定义 Pointer 事件代码
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 移除自定义的 PointerPressed/PointerMoved/PointerReleased/PointerCanceled 事件处理程序
  - 移除拖动状态变量
  - 移除 EndDrag 方法
- **Success Criteria**:
  - 代码清理完成，使用内置平移功能
- **Test Requirements**:
  - `programmatic` TR-2.1: 验证自定义 Pointer 事件代码已移除

## [ ] 任务 3: 保持滚轮缩放功能
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 保持现有的 PointerWheelChanged 滚轮缩放功能
  - 确保与内置平移功能兼容
- **Success Criteria**:
  - 滚轮缩放和内置平移都能正常工作
- **Test Requirements**:
  - `human-judgement` TR-3.1: 验证滚轮缩放功能正常
  - `human-judgement` TR-3.2: 验证平移功能正常

## [ ] 任务 4: 验证边界感知
- **Priority**: P1
- **Depends On**: 任务 1
- **Description**: 
  - 验证 ScrollViewer 内置的边界限制功能
  - 确保图片不会被拖出边界
- **Success Criteria**:
  - 拖动被限制在图片边界内
- **Test Requirements**:
  - `human-judgement` TR-4.1: 验证拖动不会超出图片边界
