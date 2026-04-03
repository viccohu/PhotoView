# ShellPage 优化实现计划

## 一、项目背景

当前 ShellPage 使用的是自定义的 AppTitleBar，不是官方推荐的 TitleBar 组件。用户希望：
1. 使用官方的 TitleBar + NavigationView 实现
2. 支持在设置中切换 NavigationView 的模式（左模式或顶模式）

## 二、实现计划

### [ ] 任务 1：替换自定义 AppTitleBar 为官方 TitleBar
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 移除自定义的 AppTitleBar Grid
  - 添加官方的 TitleBar 组件
  - 设置 ExtendsContentIntoTitleBar 和 SetTitleBar
  - 实现 TitleBar 的 BackRequested 和 PaneToggleRequested 事件

- **成功标准**：
  - 应用窗口顶部显示官方 TitleBar
  - 自定义标题栏正常工作
  - 窗口控制按钮（最小化、最大化、关闭）正常显示

- **测试要求**：
  - `programmatic` TR-1.1：TitleBar 组件正确添加到 XAML
  - `programmatic` TR-1.2：ExtendsContentIntoTitleBar 设置为 true
  - `human-judgement` TR-1.3：TitleBar 显示正确，窗口控制按钮可见

### [ ] 任务 2：修改 NavigationView 配置
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 修改 NavigationView 布局，使其位于 TitleBar 下方
  - 设置 IsBackButtonVisible="Collapsed"
  - 设置 IsPaneToggleButtonVisible="False"（由 TitleBar 控制）

- **成功标准**：
  - NavigationView 正确显示在 TitleBar 下方
  - 导航菜单正常工作

- **测试要求**：
  - `programmatic` TR-2.1：NavigationView 布局正确
  - `human-judgement` TR-2.2：NavigationView 显示正常，菜单可点击

### [ ] 任务 3：在设置中添加 NavigationView 模式切换选项
- **优先级**：P1
- **依赖**：任务 1 和 2
- **描述**：
  - 在 SettingsPage.xaml 中添加 NavigationView 模式切换选项
  - 选项包括：左侧模式（Left）和顶部模式（Top）
  - 保存用户选择到应用设置

- **成功标准**：
  - 设置页面显示 NavigationView 模式切换选项
  - 用户选择能正确保存

- **测试要求**：
  - `programmatic` TR-3.1：设置选项正确添加
  - `human-judgement` TR-3.2：设置界面显示正常

### [ ] 任务 4：实现 NavigationView 模式切换逻辑
- **优先级**：P1
- **依赖**：任务 3
- **描述**：
  - 在 ShellPage.xaml.cs 中实现模式切换逻辑
  - 根据用户设置更改 NavigationView.PaneDisplayMode
  - 处理不同模式下的布局调整

- **成功标准**：
  - 应用启动时使用保存的模式
  - 切换模式后 NavigationView 布局正确变化

- **测试要求**：
  - `programmatic` TR-4.1：模式切换逻辑正确实现
  - `human-judgement` TR-4.2：切换模式后界面布局正确

### [ ] 任务 5：测试和优化
- **优先级**：P2
- **依赖**：所有其他任务
- **描述**：
  - 测试不同模式下的界面布局
  - 测试窗口大小变化时的响应
  - 优化界面美观度和用户体验

- **成功标准**：
  - 所有模式下界面布局正确
  - 窗口大小变化时布局正常调整
  - 用户体验流畅

- **测试要求**：
  - `human-judgement` TR-5.1：不同模式下界面美观
  - `human-judgement` TR-5.2：窗口大小变化时布局稳定

## 三、技术实现细节

### 1. TitleBar 实现

```xaml
<TitleBar
    x:Name="titleBar"
    BackRequested="TitleBar_BackRequested"
    IsBackButtonVisible="Collapsed"
    IsPaneToggleButtonVisible="True"
    PaneToggleRequested="TitleBar_PaneToggleRequested" />
```

```csharp
public ShellPage()
{
    this.InitializeComponent();
    this.ExtendsContentIntoTitleBar = true;
    this.SetTitleBar(titleBar);
}

private void TitleBar_BackRequested(TitleBar sender, BackRequestedEventArgs args)
{
    // 处理返回逻辑
}

private void TitleBar_PaneToggleRequested(TitleBar sender, PaneToggleRequestedEventArgs args)
{
    NavigationViewControl.IsPaneOpen = !NavigationViewControl.IsPaneOpen;
}
```

### 2. NavigationView 模式切换

```csharp
private void LoadNavigationViewMode()
{
    var mode = SettingsService.GetNavigationViewMode();
    NavigationViewControl.PaneDisplayMode = mode;
}

public void SetNavigationViewMode(NavigationViewPaneDisplayMode mode)
{
    NavigationViewControl.PaneDisplayMode = mode;
    SettingsService.SaveNavigationViewMode(mode);
}
```

## 四、预期效果

1. **界面美观**：使用官方 TitleBar，界面更符合 Windows 11 设计规范
2. **功能完整**：支持 NavigationView 模式切换
3. **用户友好**：设置界面直观，切换模式方便
4. **稳定可靠**：不同模式下布局稳定，窗口大小变化时响应正常

## 五、注意事项

1. **兼容性**：确保在不同 Windows 版本上都能正常工作
2. **性能**：模式切换不应影响应用性能
3. **用户体验**：切换模式后应保持当前导航状态
4. **代码质量**：遵循 MVVM 模式，保持代码清晰可维护