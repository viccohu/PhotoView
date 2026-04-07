# 高清图完整链路检查

## 问题分析

### 问题现象
- 当前行为：
1. 点击图片 → 先展示 1x 低清（小）
2. 然后展示高清图替换

### 根因分析
1. **XAML 中 `Image.Stretch="None"` 导致缩略图按原始像素显示
2. 缩略图本身尺寸较小，所以显示得小
3. 高清图加载后替换，但问题

---

## 检查方案

## [ ] 任务 1: 理解官方方案验证
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 分析之前的官方文档中 `Image.Stretch="None"` 的真正用意
  - 确认当前行为是否符合预期
- **Success Criteria**: 明确理解 Stretch 配置

## [ ] 任务 2: 检查缩略图尺寸验证
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 查看 `ImageFileInfo.Thumbnail` 的实际尺寸是
  - 查看 `ThumbnailService` 的缩略图大小
- **Success Criteria**: 确认缩略图是否是小图

## [ ] 任务 3: 修复方案选择
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 
  - 选项 A：保持 `Stretch="None"`，但确保缩略图加载后调用 `ApplyInitialZoomToFit()`
  - 选项 B：临时把 `Stretch="Uniform"` 用于缩略图，等高清图加载后再改为 None
  - 选项 C：保持 `Stretch="Uniform"` 用于两者，但用 ScrollViewer 来处理缩放
- **Success Criteria**: 选择正确的修复方案

## [ ] 任务 4: 实施修复
- **Priority**: P0
- **Depends On**: 任务 3
- **Description**: 实施选定的修复方案
- **Success Criteria**: 修复完成

## [ ] 任务 5: 构建和测试
- **Priority**: P1
- **Depends On**: 任务 4
- **Description**: 构建项目并测试
- **Success Criteria**: 构建成功，显示正常
