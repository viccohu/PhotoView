# 修复图片拖动完全不动问题

## 问题分析
经过排查，发现以下问题：
1. ScrollViewer 的 HorizontalScrollMode 和 VerticalScrollMode 设置为 Disabled，导致 ChangeView 无法正常工作
2. Tapped 事件标记了 e.Handled=true，可能影响 Pointer 事件传递
3. 需要使用 AddHandler 并设置 handledEventsToo=true 确保事件能被正确捕获

---

## [ ] 任务 1: 启用 ScrollViewer 的滚动模式
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 将 HorizontalScrollMode 和 VerticalScrollMode 从 Disabled 改为 Enabled
  - 保持滚动条可见性为 Disabled（不显示滚动条）
- **Success Criteria**:
  - ScrollViewer 可以通过 ChangeView 进行滚动
- **Test Requirements**:
  - `programmatic` TR-1.1: 验证 ScrollMode 已设置为 Enabled

## [ ] 任务 2: 使用代码注册事件并设置 handledEventsToo
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 在构造函数中使用 AddHandler 注册 Pointer 事件
  - 设置 handledEventsToo 参数为 true
  - 从 XAML 中移除事件绑定
- **Success Criteria**:
  - 即使其他控件标记事件为 Handled，Pointer 事件也能被捕获
- **Test Requirements**:
  - `human-judgement` TR-2.1: 验证拖动现在可以正常工作

## [ ] 任务 3: 增强调试输出（可选）
- **Priority**: P2
- **Depends On**: None
- **Description**: 
  - 添加调试输出，方便排查 ExtentWidth/ExtentHeight/ViewportWidth/ViewportHeight 的值
- **Success Criteria**:
  - 可以在输出窗口看到拖动相关的调试信息
