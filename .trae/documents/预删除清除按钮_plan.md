# 预删除清除按钮 - 实现计划

## [ ] 任务 1: 在预删除统计框中添加小叉按钮
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 将预删除统计的 Border 改为 Grid，包含 TextBlock 和 Button
  - 添加小叉按钮（使用 Close 图标）
  - 按钮点击调用清除全部预删除标记
- **Success Criteria**: 预删除统计框右上角有小叉按钮
- **Test Requirements**:
  - `programmatic` TR-1.1: XAML 中有小叉按钮
  - `human-judgement` TR-1.2: 按钮在统计框右上角，布局合理
- **Notes**: 修改 MainPage.xaml

## [ ] 任务 2: 实现小叉按钮点击事件
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 实现按钮点击事件处理
  - 调用 ViewModel.ClearAllPendingDelete()
- **Success Criteria**: 点击按钮清除全部预删除标记
- **Test Requirements**:
  - `programmatic` TR-2.1: 点击按钮后 PendingDeleteCount 变为 0
  - `human-judgement` TR-2.2: 所有图片的预删除标记被清除
- **Notes**: 修改 MainPage.xaml.cs

## 整体成功标准
- ✅ 预删除统计框右上角有小叉按钮
- ✅ 点击小叉按钮一键清除全部预删除标记
- ✅ 布局美观合理
