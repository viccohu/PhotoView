# 性能模式设置优化计划

## 目标
将设置页面中的 "Concurrent loaders" 改为 "性能模式" 开关，包含两个选项：
- **智能模式**（默认）：根据 CPU 核心数自动调整并发数
- **稳定模式**：使用固定的 4 并发

同时移除 "Thumbnail cache" 设置项（因为没有后端实现）。

---

## 实施任务

### [ ] 任务 1: 添加性能模式枚举和相关属性到 ISettingsService
- **优先级**: P0
- **描述**: 在 ISettingsService 接口中添加性能模式相关属性
- **操作**:
  - 创建 `PerformanceMode` 枚举（Smart, Stable）
  - 添加 `PerformanceMode` 属性
  - 添加 `PerformanceModeChanged` 事件
  - 添加 `SavePerformanceModeAsync` 和 `LoadPerformanceModeAsync` 方法
- **验证**: 接口定义正确，无编译错误

### [ ] 任务 2: 实现 SettingsService 中的性能模式逻辑
- **优先级**: P0
- **描述**: 在 SettingsService 中实现性能模式的存储和加载
- **操作**:
  - 添加 `_performanceMode` 私有字段，默认值为 `PerformanceMode.Smart`
  - 实现 `PerformanceMode` 属性
  - 实现 `SavePerformanceModeAsync` 方法
  - 实现 `LoadPerformanceModeAsync` 方法，默认返回 `PerformanceMode.Smart`
- **验证**: 设置可以正确保存和加载

### [ ] 任务 3: 修改 ThumbnailService 支持动态并发数
- **优先级**: P0
- **描述**: 修改 ThumbnailService 使其能够根据性能模式调整并发数
- **操作**:
  - 添加 `ISettingsService` 依赖注入
  - 添加 `UpdateConcurrency` 方法
  - 智能模式：`Math.Max(4, Environment.ProcessorCount / 2)`
  - 稳定模式：固定 4
  - 监听 `PerformanceModeChanged` 事件动态调整
- **验证**: 并发数能够根据设置动态调整

### [ ] 任务 4: 更新 SettingsViewModel 添加性能模式属性
- **优先级**: P0
- **描述**: 在 SettingsViewModel 中添加性能模式相关属性和命令
- **操作**:
  - 添加 `PerformanceMode` 属性
  - 添加 `SetPerformanceModeCommand` 命令
- **验证**: 属性绑定正确

### [ ] 任务 5: 更新 SettingsPage.xaml UI
- **优先级**: P0
- **描述**: 修改设置页面 UI
- **操作**:
  - 移除 "Thumbnail cache" 设置项
  - 将 "Concurrent loaders" 改为 "Performance mode"
  - 使用 RadioButtons 提供两个选项：Smart / Stable
  - 智能模式为默认选中
  - 添加描述说明两种模式的区别
- **验证**: UI 显示正确

### [ ] 任务 6: 更新 SettingsPage.xaml.cs 事件处理
- **优先级**: P0
- **描述**: 添加性能模式选择变更的事件处理
- **操作**:
  - 添加 `PerformanceModeRadioButtons_SelectionChanged` 事件处理方法
  - 初始化时加载当前设置（默认为智能模式）
- **验证**: 设置变更能够正确保存

---

## 详细实施步骤

### 步骤 1: 创建 PerformanceMode 枚举
```csharp
namespace PhotoView.Models;

public enum PerformanceMode
{
    Smart,    // 智能模式 - 根据 CPU 核心数自动调整（默认）
    Stable    // 稳定模式 - 固定 4 并发
}
```

### 步骤 2: 修改 ISettingsService
```csharp
PerformanceMode PerformanceMode { get; set; }
event EventHandler<PerformanceMode>? PerformanceModeChanged;
Task SavePerformanceModeAsync(PerformanceMode mode);
Task<PerformanceMode> LoadPerformanceModeAsync();
```

### 步骤 3: 修改 ThumbnailService
```csharp
public class ThumbnailService : IThumbnailService
{
    private readonly ISettingsService _settingsService;
    private SemaphoreSlim _decodeGate;
    
    public ThumbnailService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _settingsService.PerformanceModeChanged += OnPerformanceModeChanged;
        _decodeGate = new SemaphoreSlim(GetConcurrencyCount(), GetConcurrencyCount());
    }
    
    private int GetConcurrencyCount()
    {
        return _settingsService.PerformanceMode == PerformanceMode.Smart
            ? Math.Max(4, Environment.ProcessorCount / 2)
            : 4;
    }
    
    private void OnPerformanceModeChanged(object? sender, PerformanceMode mode)
    {
        var newCount = GetConcurrencyCount();
        _decodeGate = new SemaphoreSlim(newCount, newCount);
    }
}
```

### 步骤 4: 修改 SettingsPage.xaml
```xml
<!-- 移除 Thumbnail cache 设置项 -->

<toolkit:SettingsCard Description="Adjust thumbnail loading performance" Header="Performance mode">
    <toolkit:SettingsCard.HeaderIcon>
        <FontIcon Glyph="&#xE9D9;" />
    </toolkit:SettingsCard.HeaderIcon>
    <RadioButtons x:Name="PerformanceModeRadioButtons" SelectionChanged="PerformanceModeRadioButtons_SelectionChanged">
        <RadioButton Content="Smart (auto-adjust based on CPU)" Tag="Smart" IsChecked="True" />
        <RadioButton Content="Stable (4 concurrent)" Tag="Stable" />
    </RadioButtons>
</toolkit:SettingsCard>
```

---

## 预期结果
1. 设置页面移除 Thumbnail cache 设置项
2. Concurrent loaders 改为 Performance mode
3. 用户可以选择 Smart 或 Stable 模式
4. 默认为 Smart 模式
5. Smart 模式根据 CPU 核心数自动调整并发数
6. Stable 模式固定 4 并发
7. 设置可以持久化保存
