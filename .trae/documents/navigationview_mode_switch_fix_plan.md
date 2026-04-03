# NavigationView 模式切换崩溃修复计划

## 一、问题分析

### 崩溃原因
- **异常代码**：0xC000027B (应用程序内部异常)
- **触发时机**：打开设置页面时
- **可能原因**：
  1. 在非 UI 线程访问 NavigationView UI 元素
  2. NavigationView 还未初始化时访问其属性
  3. 直接在 SettingsViewModel 中操作 UI 元素

## 二、官方建议分析

根据 Microsoft Learn 文档和 WinUI 3 最佳实践：

1. **UI 元素必须在 UI 线程访问**
   - 所有修改 UI 的操作必须在 UI 线程执行
   - 可以使用 `DispatcherQueue.TryEnqueue` 确保在 UI 线程执行

2. **NavigationView.PaneDisplayMode 支持运行时修改**
   - 官方文档明确指出该属性可以在运行时修改
   - 修改后会立即生效

3. **MVVM 模式最佳实践**
   - ViewModel 应该与 UI 元素解耦
   - 应该通过服务或事件来通知 View 更新

## 三、实现计划

### [x] 任务 1：创建设置服务管理导航模式
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 创建 `ISettingsService` 接口和 `SettingsService` 实现
  - 添加 `NavigationViewMode` 属性
  - 实现设置保存和加载功能
  - 添加设置变化事件

- **成功标准**：
  - 设置服务能够保存和加载导航模式
  - 设置变化时能够触发事件通知

- **测试要求**：
  - `programmatic` TR-1.1：设置服务能够正确保存和加载导航模式
  - `programmatic` TR-1.2：设置变化时能够触发事件

### [x] 任务 2：修改 ShellPage 响应设置变化
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 在 ShellPage 中订阅设置变化事件
  - 当导航模式变化时，在 UI 线程上更新 NavigationView.PaneDisplayMode

- **成功标准**：
  - ShellPage 能够响应设置变化
  - NavigationView.PaneDisplayMode 在 UI 线程上更新

- **测试要求**：
  - `programmatic` TR-2.1：ShellPage 能够订阅设置变化事件
  - `human-judgement` TR-2.2：导航模式变化时 UI 能够正确更新

### [x] 任务 3：修改 SettingsViewModel 使用设置服务
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 修改 SettingsViewModel 构造函数，注入 `ISettingsService`
  - 修改 `SwitchNavigationViewModeCommand`，通过设置服务更新导航模式
  - 移除对 `INavigationViewService` 的依赖

- **成功标准**：
  - SettingsViewModel 不再直接访问 NavigationView
  - 通过设置服务更新导航模式

- **测试要求**：
  - `programmatic` TR-3.1：SettingsViewModel 能够通过设置服务更新导航模式
  - `programmatic` TR-3.2：SettingsViewModel 不再依赖 INavigationViewService

### [ ] 任务 4：测试和验证
- **优先级**：P1
- **依赖**：任务 1, 2, 3
- **描述**：
  - 测试打开设置页面是否崩溃
  - 测试切换导航模式是否生效
  - 测试应用重启后导航模式是否保存

- **成功标准**：
  - 打开设置页面不再崩溃
  - 切换导航模式能够实时生效
  - 应用重启后导航模式保持不变

- **测试要求**：
  - `human-judgement` TR-4.1：打开设置页面不崩溃
  - `human-judgement` TR-4.2：切换导航模式能够实时生效
  - `human-judgement` TR-4.3：应用重启后导航模式保持不变

## 四、技术实现细节

### 1. ISettingsService 接口

```csharp
public interface ISettingsService
{
    NavigationViewPaneDisplayMode NavigationViewMode {
        get;
        set;
    }

    event EventHandler<NavigationViewPaneDisplayMode> NavigationViewModeChanged;

    void SaveNavigationViewMode(NavigationViewPaneDisplayMode mode);
    NavigationViewPaneDisplayMode LoadNavigationViewMode();
}
```

### 2. SettingsService 实现

```csharp
public class SettingsService : ISettingsService
{
    private readonly ILocalSettingsService _localSettingsService;

    public event EventHandler<NavigationViewPaneDisplayMode> NavigationViewModeChanged;

    public NavigationViewPaneDisplayMode NavigationViewMode
    {
        get => LoadNavigationViewMode();
        set
        {
            SaveNavigationViewMode(value);
            NavigationViewModeChanged?.Invoke(this, value);
        }
    }

    public SettingsService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public void SaveNavigationViewMode(NavigationViewPaneDisplayMode mode)
    {
        _localSettingsService.SaveSettingAsync("NavigationViewMode", mode.ToString());
    }

    public NavigationViewPaneDisplayMode LoadNavigationViewMode()
    {
        var mode = _localSettingsService.ReadSettingAsync<string>("NavigationViewMode").Result;
        if (Enum.TryParse<NavigationViewPaneDisplayMode>(mode, out var result))
        {
            return result;
        }
        return NavigationViewPaneDisplayMode.Top; // 默认值
    }
}
```

### 3. ShellPage 修改

```csharp
public ShellPage(ShellViewModel viewModel, ISettingsService settingsService)
{
    ViewModel = viewModel;
    InitializeComponent();

    ViewModel.NavigationService.Frame = NavigationFrame;
    ViewModel.NavigationViewService.Initialize(NavigationViewControl);

    // 订阅设置变化事件
    settingsService.NavigationViewModeChanged += OnNavigationViewModeChanged;
    
    // 初始设置
    NavigationViewControl.PaneDisplayMode = settingsService.NavigationViewMode;
}

private void OnNavigationViewModeChanged(object sender, NavigationViewPaneDisplayMode mode)
{
    // 在 UI 线程上更新
    DispatcherQueue.TryEnqueue(() =>
    {
        NavigationViewControl.PaneDisplayMode = mode;
    });
}
```

### 4. SettingsViewModel 修改

```csharp
public class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ISettingsService _settingsService;
    private ElementTheme _elementTheme;
    private string _versionDescription;
    private NavigationViewPaneDisplayMode _navigationViewMode;

    public NavigationViewPaneDisplayMode NavigationViewMode
    {
        get => _navigationViewMode;
        set => SetProperty(ref _navigationViewMode, value);
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService, ISettingsService settingsService)
    {
        _themeSelectorService = themeSelectorService;
        _settingsService = settingsService;
        _elementTheme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();
        _navigationViewMode = _settingsService.NavigationViewMode;

        SwitchNavigationViewModeCommand = new RelayCommand<NavigationViewPaneDisplayMode>(
            (param) =>
            {
                if (NavigationViewMode != param)
                {
                    NavigationViewMode = param;
                    _settingsService.NavigationViewMode = param;
                }
            });
    }
}
```

## 五、预期效果

1. **打开设置页面不再崩溃**
2. **切换导航模式能够实时生效**
3. **应用重启后导航模式保持不变**
4. **代码结构更加清晰，符合 MVVM 模式**

## 六、注意事项

1. **线程安全**：确保所有 UI 操作都在 UI 线程执行
2. **依赖注入**：确保所有服务都正确注册
3. **错误处理**：添加适当的错误处理，避免崩溃
4. **性能**：设置变化事件应该高效处理，避免性能问题