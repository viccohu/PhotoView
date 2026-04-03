# 设置页面布局优化计划 - 模仿官方 WinUI Gallery 设置页面

## 目标
将当前设置页面布局改为模仿官方 WinUI Gallery 的设置页面样式，使用 CommunityToolkit.WinUI.Controls 中的 SettingsCard 和 SettingsExpander 控件。

## 当前状态分析

### 官方示例特点
1. 使用 `CommunityToolkit.WinUI.Controls` 的 `SettingsCard` 和 `SettingsExpander` 控件
2. 使用 `ScrollView` 替代 `ScrollViewer`
3. 使用 `StackPanel.Spacing` 统一控制间距
4. 使用 `RepositionThemeTransition` 动画
5. 使用 `MaxWidth="1064"` 限制内容宽度
6. 使用 `Padding="36,0,36,0"` 设置页面边距
7. 包含导航模式切换功能（Left/Top）

### 当前实现问题
1. 使用自定义 Border 样式模拟卡片效果
2. 使用 ScrollViewer 而非 ScrollView
3. 手动设置每个卡片的 Margin
4. 缺少 RepositionThemeTransition 动画
5. 缺少导航模式切换功能

---

## 实施任务

### [ ] 任务 1: 添加 CommunityToolkit.WinUI.Controls NuGet 包
- **优先级**: P0
- **描述**: 在项目中添加 CommunityToolkit.WinUI.Controls 包引用
- **操作**: 
  - 在 PhotoView.csproj 中添加 `<PackageReference Include="CommunityToolkit.WinUI.Controls" Version="8.2.2" />`
- **验证**: 构建成功，无编译错误

### [ ] 任务 2: 更新 SettingsPage.xaml 布局
- **优先级**: P0
- **描述**: 按照官方示例重构设置页面布局
- **操作**:
  - 添加 `xmlns:toolkit="using:CommunityToolkit.WinUI.Controls"` 命名空间
  - 将 `ScrollViewer` 替换为 `ScrollView`
  - 添加 `MaxWidth="1064"` 限制内容宽度
  - 使用 `SettingsCard` 替代自定义 Border 样式
  - 添加 `RepositionThemeTransition` 动画
  - 使用 `SettingsSectionHeaderTextBlockStyle` 样式
- **验证**: 页面布局与官方示例一致

### [ ] 任务 3: 添加导航模式切换功能
- **优先级**: P1
- **描述**: 在设置页面添加导航模式切换选项
- **操作**:
  - 在 SettingsPage.xaml 中添加导航模式设置卡片
  - 在 SettingsPage.xaml.cs 中添加导航模式切换事件处理
  - 在 SettingsViewModel.cs 中添加导航模式相关属性和命令
- **验证**: 可以在设置中切换左侧/顶部导航模式

### [ ] 任务 4: 更新 SettingsPage.xaml.cs 代码
- **优先级**: P0
- **描述**: 更新代码后台以支持新的布局和功能
- **操作**:
  - 添加导航模式初始化代码
  - 添加导航模式切换事件处理
- **验证**: 主题切换和导航模式切换功能正常

### [ ] 任务 5: 更新 SettingsViewModel.cs
- **优先级**: P1
- **描述**: 添加导航模式相关属性和命令
- **操作**:
  - 添加 `NavigationViewMode` 属性
  - 添加 `SwitchNavigationModeCommand` 命令
  - 注入 `ISettingsService` 服务
- **验证**: 导航模式设置可以正确保存和加载

---

## 详细实施步骤

### 步骤 1: 修改 PhotoView.csproj
```xml
<PackageReference Include="CommunityToolkit.WinUI.Controls" Version="8.2.2" />
```

### 步骤 2: 修改 SettingsPage.xaml
按照官方示例结构：
```xml
<Page
    xmlns:toolkit="using:CommunityToolkit.WinUI.Controls">
    <Page.Resources>
        <x:Double x:Key="SettingsCardSpacing">4</x:Double>
        <Style x:Key="SettingsSectionHeaderTextBlockStyle" ...>
    </Page.Resources>
    <Grid>
        <TextBlock Text="Settings" Style="{StaticResource TitleTextBlockStyle}" />
        <ScrollView>
            <StackPanel MaxWidth="1064" Spacing="{StaticResource SettingsCardSpacing}">
                <StackPanel.ChildrenTransitions>
                    <RepositionThemeTransition />
                </StackPanel.ChildrenTransitions>
                
                <!-- Appearance & behavior -->
                <TextBlock Style="{StaticResource SettingsSectionHeaderTextBlockStyle}" Text="Appearance &amp; behavior" />
                <toolkit:SettingsCard Header="App theme" Description="Select which app theme to display">
                    <toolkit:SettingsCard.HeaderIcon>
                        <FontIcon Glyph="&#xE790;" />
                    </toolkit:SettingsCard.HeaderIcon>
                    <ComboBox x:Name="themeMode" SelectionChanged="themeMode_SelectionChanged">
                        <ComboBoxItem Content="Light" Tag="Light" />
                        <ComboBoxItem Content="Dark" Tag="Dark" />
                        <ComboBoxItem Content="Use system setting" Tag="Default" />
                    </ComboBox>
                </toolkit:SettingsCard>
                
                <toolkit:SettingsCard Header="Navigation style">
                    <toolkit:SettingsCard.HeaderIcon>
                        <FontIcon Glyph="&#xF594;" />
                    </toolkit:SettingsCard.HeaderIcon>
                    <ComboBox x:Name="navigationLocation" SelectionChanged="navigationLocation_SelectionChanged">
                        <ComboBoxItem Content="Left" />
                        <ComboBoxItem Content="Top" />
                    </ComboBox>
                </toolkit:SettingsCard>
                
                <!-- Performance -->
                ...
                
                <!-- About -->
                ...
            </StackPanel>
        </ScrollView>
    </Grid>
</Page>
```

### 步骤 3: 修改 SettingsPage.xaml.cs
添加导航模式切换处理：
```csharp
private int lastNavigationSelectionMode = 0;

private void OnSettingsPageLoaded(object sender, RoutedEventArgs e)
{
    // 初始化主题选择
    var currentTheme = ViewModel.ElementTheme;
    switch (currentTheme)
    {
        case ElementTheme.Light:
            themeMode.SelectedIndex = 0;
            break;
        case ElementTheme.Dark:
            themeMode.SelectedIndex = 1;
            break;
        case ElementTheme.Default:
            themeMode.SelectedIndex = 2;
            break;
    }

    // 初始化导航模式选择
    if (App.MainWindow.NavigationView.PaneDisplayMode == NavigationViewPaneDisplayMode.Left)
    {
        navigationLocation.SelectedIndex = 0;
    }
    else
    {
        navigationLocation.SelectedIndex = 1;
    }
    lastNavigationSelectionMode = navigationLocation.SelectedIndex;
}

private void navigationLocation_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (navigationLocation.SelectedIndex != lastNavigationSelectionMode)
    {
        var isLeftMode = navigationLocation.SelectedIndex == 0;
        ViewModel.SwitchNavigationModeCommand.Execute(isLeftMode ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.Top);
        lastNavigationSelectionMode = navigationLocation.SelectedIndex;
    }
}
```

### 步骤 4: 修改 SettingsViewModel.cs
添加导航模式支持：
```csharp
private readonly ISettingsService _settingsService;
private NavigationViewPaneDisplayMode _navigationViewMode;

public NavigationViewPaneDisplayMode NavigationViewMode
{
    get => _navigationViewMode;
    set => SetProperty(ref _navigationViewMode, value);
}

public ICommand SwitchNavigationModeCommand { get; }

public SettingsViewModel(IThemeSelectorService themeSelectorService, ISettingsService settingsService)
{
    _themeSelectorService = themeSelectorService;
    _settingsService = settingsService;
    _elementTheme = _themeSelectorService.Theme;
    _navigationViewMode = _settingsService.NavigationViewMode;
    
    SwitchNavigationModeCommand = new RelayCommand<NavigationViewPaneDisplayMode>((param) =>
    {
        if (NavigationViewMode != param)
        {
            NavigationViewMode = param;
            _settingsService.NavigationViewMode = param;
        }
    });
}
```

---

## 预期结果
1. 设置页面布局与官方 WinUI Gallery 一致
2. 支持主题切换（Light/Dark/Default）
3. 支持导航模式切换
4. 页面动画流畅
5. 响应式布局，最大宽度 1064px
