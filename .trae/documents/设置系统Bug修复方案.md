# 设置系统 Bug 修复方案

## 问题分析

### Bug 现象
设置页面切换其他页面后再切回设置页面，设置页面的值恢复默认值。

### 根本原因分析

| 组件 | 生命周期 | 问题 |
|------|----------|------|
| `SettingsService` | Singleton | ✅ 单例，值应该保持 |
| `SettingsViewModel` | Transient | ⚠️ 每次进入设置页面都创建新实例 |
| `SettingsPage` | Transient | 每次进入都创建新实例 |

**问题根源**：
1. `LoadPerformanceModeAsync()` 没有在应用启动时调用
2. `SettingsService._performanceMode` 始终是默认值 `PerformanceMode.Smart`
3. 每次进入设置页面，新的 `SettingsViewModel` 从 `SettingsService` 读取值
4. 由于 `SettingsService` 内部值没有从存储加载，显示的是默认值

### 设置加载流程对比

| 设置 | 启动时加载 | 保存时更新 | 状态 |
|------|------------|------------|------|
| NavigationViewMode | ✅ `LoadNavigationViewModeAsync()` | ✅ | 正常 |
| BatchSize | ✅ `LoadBatchSizeAsync()` | ✅ | 正常 |
| PerformanceMode | ❌ 未调用 | ✅ | **Bug** |

---

## 解决方案

### [ ] 任务 1：在 ShellPage 启动时加载 PerformanceMode
- **优先级**：P0
- **描述**：
  - 修改 `InitializeNavigationViewModeAsync()` 方法
  - 添加 `_ = await _settingsService.LoadPerformanceModeAsync();`
- **修改文件**：`ShellPage.xaml.cs`
- **成功标准**：
  - PerformanceMode 在应用启动时正确加载
  - SettingsService._performanceMode 包含保存的值

### [ ] 任务 2：验证设置保存和加载
- **优先级**：P1
- **描述**：
  - 测试设置保存
  - 测试切换页面后设置页面显示正确值
  - 测试重启后保留设置

---

## 代码修改

### ShellPage.xaml.cs

```csharp
private async Task InitializeNavigationViewModeAsync()
{
    var mode = await _settingsService.LoadNavigationViewModeAsync();
    _ = await _settingsService.LoadBatchSizeAsync();
    _ = await _settingsService.LoadPerformanceModeAsync();  // 添加这行
    
    // 在 UI 线程上更新
    DispatcherQueue.TryEnqueue(() =>
    {
        NavigationViewControl.PaneDisplayMode = mode;
        NavigationViewControl.IsPaneToggleButtonVisible = mode == NavigationViewPaneDisplayMode.Left;
    });
}
```

---

## 验证清单

- [ ] 修改 PerformanceMode 设置后保存
- [ ] 切换到其他页面，再切回设置页面，显示正确的值
- [ ] 重启应用，设置保留
