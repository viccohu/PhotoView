# 高清预览功能修复计划

## 问题分析

根据日志和代码分析，目前存在以下问题：

### 1. 评级功能问题
- **现象**：高清预览中评级无法成功，日志显示 `跳过执行命令 (ViewModel null 或值未变化)`
- **原因**：`ImageRatingControl_ValueChanged` 方法检查 `sender.Value != ViewModel.Rating`，但值没有变化，导致命令被跳过
- **根本原因**：可能是绑定问题或值同步问题

### 2. 按键评级无响应
- **现象**：按键评级没响应
- **原因**：`OnKeyDown` 方法只处理了 Escape 键和方向键，没有处理数字键用于评级

### 3. 方向键仍然能切换图片
- **现象**：尽管 `OnKeyDown` 方法阻止了方向键，但方向键仍然能切换图片
- **原因**：可能是事件处理优先级问题或其他地方也在处理方向键事件

### 4. 无效指针错误
- **现象**：日志显示 `Microsoft.UI.Xaml.dll!00007FFB681014C7: 80004003 - 无效指针`
- **原因**：在处理按键事件时可能出现空指针问题

## 修复计划

### 1. 修复评级功能
- **文件**：`Controls/ImageViewerControl.xaml.cs`
- **修改**：
  - 检查 `ImageRatingControl` 的绑定设置
  - 确保 `ViewModel` 正确初始化
  - 优化 `ImageRatingControl_ValueChanged` 方法的逻辑

### 2. 添加按键评级支持
- **文件**：`Controls/ImageViewerControl.xaml.cs`
- **修改**：
  - 在 `OnKeyDown` 方法中添加数字键（1-5）处理逻辑
  - 实现按键评级功能，调用 `ViewModel.SetRatingCommand`

### 3. 修复方向键问题
- **文件**：`Controls/ImageViewerControl.xaml.cs`
- **修改**：
  - 确保 `OnKeyDown` 方法正确处理方向键事件
  - 检查是否有其他地方处理方向键事件

### 4. 修复无效指针错误
- **文件**：`Controls/ImageViewerControl.xaml.cs`
- **修改**：
  - 在 `OnKeyDown` 方法中添加空值检查
  - 确保所有访问的对象都不为 null

## 具体实现步骤

1. **检查并修复 ViewModel 绑定**
   - 确保 `ImageViewerControl` 中的 `ViewModel` 属性正确初始化
   - 检查 `ImageRatingControl` 的 `Value` 属性绑定

2. **优化评级事件处理**
   - 修改 `ImageRatingControl_ValueChanged` 方法，确保正确处理评级变化
   - 添加更多调试信息以定位问题

3. **实现按键评级**
   - 在 `OnKeyDown` 方法中添加数字键处理
   - 当按下 1-5 数字键时，设置对应的评级

4. **强化方向键处理**
   - 确保方向键事件被正确处理和标记为已处理
   - 检查事件冒泡和路由

5. **添加空值检查**
   - 在 `OnKeyDown` 方法中添加必要的空值检查
   - 防止无效指针错误

## 预期结果

- ✅ 高清预览中评级功能正常工作
- ✅ 按键评级功能正常响应
- ✅ 方向键在预览模式下不切换图片
- ✅ 无无效指针错误

## 风险评估

- **低风险**：修改仅涉及事件处理和逻辑优化，不影响核心功能
- **中等风险**：按键处理可能影响其他功能，需要仔细测试
- **低风险**：空值检查是防御性编程，不会引入新问题