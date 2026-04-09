# 键盘事件处理修复计划

## 问题分析

根据日志和代码分析，目前存在以下问题：

### 1. 方向键在高清预览中仍然能切换图片
- **现象**：高清预览界面打开后，不点击任何地方时，方向键仍然能切换缩略图页面的图片
- **原因**：虽然 MainPage_KeyDown 方法中有阻止方向键的逻辑，但可能是事件处理优先级或焦点问题导致方向键事件没有被正确处理

### 2. 数字键评级不可用
- **现象**：在高清预览中按数字键没有反应，控制台无输出
- **原因**：MainPage_KeyDown 方法中，当 _currentViewer 存在时，数字键被标记为"未处理的按键"，但没有传递给 ImageViewerControl 处理

### 3. 缩略图页面评级没有更新
- **现象**：在高清预览中评级生效，但缩略图页面的评级显示没有更新
- **原因**：评级更新后没有通知 MainViewModel 或 ImageFileInfo 的 Rating 属性没有被正确同步

## 修复计划

### 1. 修复方向键问题
- **文件**：`Views/MainPage.xaml.cs`
- **修改**：
  - 确保当 _currentViewer 存在时，所有方向键事件都被正确处理
  - 强化方向键事件的处理逻辑

### 2. 修复数字键评级问题
- **文件**：`Views/MainPage.xaml.cs`
- **修改**：
  - 当 _currentViewer 存在时，将数字键事件传递给 ImageViewerControl 处理
  - 确保数字键评级在高清预览中正常工作

### 3. 修复缩略图页面评级更新问题
- **文件**：`ViewModels/ImageViewerViewModel.cs`
- **修改**：
  - 确保当评级更新时，ImageFileInfo 的 Rating 属性被正确更新
  - 可能需要添加事件通知机制，通知 MainViewModel 评级已更新

## 具体实现步骤

1. **强化 MainPage_KeyDown 方法**
   - 确保当 _currentViewer 存在时，所有方向键事件都被标记为已处理
   - 添加数字键处理逻辑，当 _currentViewer 存在时，将数字键事件传递给 ImageViewerControl

2. **优化 ImageViewerControl 的焦点处理**
   - 确保 ImageViewerControl 在显示时能够获取焦点
   - 可能需要在 ImageViewerControl 中添加焦点管理逻辑

3. **修复评级更新机制**
   - 确保 ImageViewerViewModel.SetRatingAsync 方法正确更新 ImageFileInfo 的 Rating 属性
   - 添加机制通知 MainViewModel 评级已更新，以便更新缩略图显示

4. **添加调试信息**
   - 在关键位置添加调试日志，以便更好地跟踪事件处理流程
   - 确保所有键盘事件都能被正确捕获和处理

## 预期结果

- ✅ 高清预览界面打开后，方向键不会切换缩略图页面的图片
- ✅ 数字键评级在高清预览中正常工作
- ✅ 缩略图页面的评级显示会随着高清预览中的评级操作而更新

## 风险评估

- **低风险**：修改仅涉及事件处理和逻辑优化，不影响核心功能
- **中等风险**：键盘事件处理可能影响其他功能，需要仔细测试
- **低风险**：评级更新机制的修改是局部的，不会影响其他功能