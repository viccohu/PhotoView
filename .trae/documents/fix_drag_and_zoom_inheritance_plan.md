# 修复拖拽和缩放继承问题 - 基于官方文档

## 问题总结（来自文档）

### 1. 不能拖动的原因
- `ScrollViewer` 开启了默认滚动处理，与手动 `ChangeView` 冲突
- 事件绑定在 `ImageContainer` 上，可能被 ScrollViewer 拦截
- `ScrollMode="Enabled"` 会干扰自定义拖动

### 2. 缩放被继承的原因
- 在图片加载前就调用 `ChangeView`，此时内容大小为 0
- `_hasAppliedInitialZoom` 标志位时序问题
- 没有在 `ImageOpened` 事件后重置缩放

---

## 修复任务

## [ ] 任务 1: 修正 XAML 配置
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 将 Image.Stretch 从 "Uniform" 改回 "None"
  - 将 HorizontalScrollMode 和 VerticalScrollMode 改为 "Disabled"
  - 将 Pointer 事件从 ImageContainer 移到 ScrollViewer
- **Success Criteria**: XAML 配置正确

## [ ] 任务 2: 修复 PrepareContent 重置逻辑
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 调整 `ChangeView` 调用时机，先设置 Source，再强制 UpdateLayout，最后调用 ChangeView
  - 添加双重重置：先设为极小值，再设回 1.0
- **Success Criteria**: PrepareContent 重置逻辑正确

## [ ] 任务 3: 添加 ImageOpened 事件处理
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 
  - 在 XAML 中为 Image 添加 ImageOpened 事件
  - 在 ImageOpened 中再次确认重置缩放
- **Success Criteria**: 图片加载后确保缩放已重置

## [ ] 任务 4: 更新事件处理程序名称
- **Priority**: P0
- **Depends On**: 任务 3
- **Description**: 更新所有事件处理程序名称，从 ImageContainer_xxx 改为 ImageScrollViewer_xxx
- **Success Criteria**: 事件处理程序名称匹配

## [ ] 任务 5: 构建和测试
- **Priority**: P1
- **Depends On**: 任务 4
- **Description**: 构建项目并测试功能
- **Success Criteria**: 构建成功，拖拽和缩放重置功能正常
