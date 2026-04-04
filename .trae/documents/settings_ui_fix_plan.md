# 设置页面 UI 修复计划

## 目标
1. 恢复分批加载控制项（BatchSize 设置）
2. 将性能模式设置改为 ComboBox 格式，与其他设置项保持一致

## 当前状态
- ✅ 后端代码完整（ISettingsService、SettingsService、SettingsViewModel 都有 BatchSize 和 PerformanceMode 支持）
- ❌ SettingsPage.xaml 中缺少 BatchSize 设置项
- ❌ Performance mode 使用 RadioButtons，需改为 ComboBox
- ❌ SettingsPage.xaml.cs 中缺少 BatchSize 事件处理

## 实施步骤

### 步骤 1：修改 SettingsPage.xaml
1. 将 Performance mode 的 RadioButtons 改为 ComboBox：
   - 选项：Smart (auto-adjust based on CPU)、Stable (4 concurrent)
   - 使用 Tag 标记值：Smart、Stable
2. 在 Performance 部分添加 Batch size 设置项：
   - 使用 ComboBox
   - 选项：10、30、50、100（Tag 标记数值）
   - 添加 SelectionChanged 事件

### 步骤 2：修改 SettingsPage.xaml.cs
1. 恢复 `UpdateBatchSizeSelection()` 方法
2. 恢复 `BatchSizeComboBox_SelectionChanged` 事件处理
3. 在 `OnSettingsPageLoaded` 中调用 `UpdateBatchSizeSelection()`
4. 修改 `PerformanceModeRadioButtons_SelectionChanged` 为 ComboBox 版本

### 步骤 3：构建验证
运行 `dotnet build` 确保无错误
