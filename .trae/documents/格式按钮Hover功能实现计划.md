# 格式按钮 Hover 显示/隐藏功能实现计划

## 需求分析

### 功能描述
1. 使用 `PointerEntered` 事件显示悬浮控件（Flyout）
2. 使用 `PointerExited` 事件关闭悬浮控件
3. 悬浮控件放在 Flyout 里
4. 如果 Button 没有所需事件，换一个有事件的组件

### 技术方案
- 使用 `Border` 替代 `Button`（Border 有完整的 Pointer 事件）
- Border 包裹 FontIcon
- Flyout 内容动态生成格式列表
- 跟踪鼠标是否在按钮或 Flyout 上

## [ ] 任务 1：将 Button 改为 Border
- **优先级**：P0
- **依赖**：None
- **描述**：
  - 将 Button 控件替换为 Border
  - Border 包裹 FontIcon
  - 保留原有的样式和位置
  - 添加 PointerEntered 和 PointerExited 事件
- **成功标准**：
  - Border 正确显示
  - 事件可以正常绑定
- **测试要求**：
  - `programmatic` TR-1.1：Border 渲染正常
  - `human-judgement` TR-1.2：样式与原 Button 一致

## [ ] 任务 2：在 C# 中实现 Hover 逻辑
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 添加 `_isMouseOverButton` 字段
  - 添加 `_isMouseOverFlyout` 字段
  - `PointerEntered`：显示 Flyout
  - `PointerExited`：检查是否需要关闭 Flyout
  - Flyout 的内容区域也添加 Pointer 事件
- **成功标准**：
  - 鼠标移入显示 Flyout
  - 鼠标移出按钮但在 Flyout 上时不关闭
  - 鼠标完全移出时关闭 Flyout
- **测试要求**：
  - `programmatic` TR-2.1：移入显示
  - `programmatic` TR-2.2：移出关闭
  - `human-judgement` TR-2.3：用户体验流畅

## [ ] 任务 3：动态生成 Flyout 内容
- **优先级**：P1
- **依赖**：任务 2
- **描述**：
  - 在 PointerEntered 时清空并重新生成 Flyout 内容
  - 显示当前格式
  - 显示其他可用格式
  - 使用 StackPanel 和 TextBlock 构建内容
- **成功标准**：
  - 格式列表正确显示
  - 内容动态生成
- **测试要求**：
  - `programmatic` TR-3.1：格式显示正确
  - `human-judgement` TR-3.2：内容布局美观

## [ ] 任务 4：测试验证
- **优先级**：P2
- **依赖**：任务 3
- **描述**：
  - 测试移入显示功能
  - 测试移出关闭功能
  - 测试在 Flyout 上停留不关闭
  - 测试不同缩略图大小下的表现
- **成功标准**：
  - 所有功能正常
  - 用户体验流畅
- **测试要求**：
  - `programmatic` TR-4.1：移入/移出正常
  - `human-judgement` TR-4.2：体验流畅

## 实现细节

### Border 替代 Button 的原因
- Button 的 Click 事件会干扰 Hover 行为
- Border 有完整的 Pointer 事件支持
- 更灵活的自定义样式

### Flyout 关闭逻辑
```
鼠标位置判断：
- 在按钮上 → 显示
- 在 Flyout 内容上 → 保持显示
- 都不在 → 关闭
```

### 防抖动
可以添加一个小的延迟（100-200ms）来避免快速移入移出时的闪烁
