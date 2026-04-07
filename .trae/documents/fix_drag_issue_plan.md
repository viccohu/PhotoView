# 修复拖动无法成功的问题

## 问题原因
1. Image.Stretch 被改回 "Uniform"（应该是 "None"）
2. ScrollViewer 的滚动模式与自定义拖动可能冲突
3. 事件路由问题

## 修复任务

## [ ] 任务 1: 修正 Image.Stretch 为 "None"
- **Priority**: P0
- **Depends On**: None
- **Description**: 将 Image 的 Stretch 属性从 "Uniform" 改回 "None"
- **Success Criteria**: Image.Stretch="None"

## [ ] 任务 2: 将事件绑定到 ScrollViewer
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 将 Pointer 事件从 ImageContainer 移到 ScrollViewer 上，确保事件能够正确捕获
- **Success Criteria**: 事件绑定在 ScrollViewer 上

## [ ] 任务 3: 禁用 ScrollViewer 的内置滚动模式
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 将 HorizontalScrollMode 和 VerticalScrollMode 改为 "Disabled"，避免与自定义拖动冲突
- **Success Criteria**: ScrollViewer 的滚动模式为 Disabled

## [ ] 任务 4: 测试拖动功能
- **Priority**: P1
- **Depends On**: 任务 3
- **Description**: 验证拖动功能是否正常工作
- **Success Criteria**: 鼠标左键可以拖动图片
