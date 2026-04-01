# 标题栏主题颜色 Bug 修复计划

## 问题分析

### 当前问题
1. **标题栏文字颜色未跟随主题变化**：在深色主题下，标题栏文字仍然是黑色
2. **hover 状态不正确**：鼠标悬停时显示的是白色主题的样式
3. **主题切换时未更新**：切换主题后，标题栏颜色没有及时更新

### 根本原因
1. `App.xaml` 中只定义了 `WindowCaptionBackground` 资源，未定义 `WindowCaptionForeground` 相关资源
2. `ShellPage.xaml.cs` 中的 `MainWindow_Activated` 方法使用的资源不是主题感知的资源
3. 缺少主题变化时的标题栏颜色更新逻辑
4. 标题栏按钮颜色未正确设置

## 修复方案

### 方案概述
使用 WinUI 3 官方推荐的主题资源，确保标题栏颜色随主题自动变化。

### 修复步骤

#### 步骤 1：更新 App.xaml 资源定义
在 `App.xaml` 中添加标题栏前景色主题资源：
- 使用 `ThemeResource` 引用系统主题颜色
- 定义 `WindowCaptionForeground` 和 `WindowCaptionForegroundDisabled` 资源

#### 步骤 2：修改 ShellPage.xaml 标题栏样式
更新 `AppTitleBarText` 的样式：
- 使用 `ThemeResource` 而不是静态资源
- 确保前景色使用主题感知的颜色

#### 步骤 3：更新 ShellPage.xaml.cs 逻辑
修改 `MainWindow_Activated` 方法：
- 使用正确的主题资源键
- 添加主题变化监听
- 在主题变化时更新标题栏颜色

#### 步骤 4：添加主题变化处理
在 `ShellPage.xaml.cs` 中：
- 监听 `ActualThemeChanged` 事件
- 在主题变化时更新标题栏颜色

#### 步骤 5：设置标题栏按钮颜色
使用 `AppWindow.TitleBar` API 设置标题栏按钮颜色：
- 设置 `ButtonForegroundColor`、`ButtonHoverForegroundColor` 等
- 确保按钮颜色随主题变化

## 技术细节

### WinUI 3 标题栏颜色资源
根据官方文档，应使用以下资源：
- 激活状态前景色：`TextFillColorPrimaryBrush` 或 `WindowCaptionForeground`
- 未激活状态前景色：`TextFillColorDisabledBrush` 或 `WindowCaptionForegroundDisabled`
- 背景色：`Transparent`（已正确设置）

### 标题栏按钮颜色 API
使用 `AppWindow.TitleBar` 属性：
- `ButtonForegroundColor` - 按钮前景色
- `ButtonHoverForegroundColor` - 悬停时前景色
- `ButtonPressedForegroundColor` - 按下时前景色
- `ButtonBackgroundColor` - 按钮背景色
- `ButtonHoverBackgroundColor` - 悬停时背景色
- `ButtonPressedBackgroundColor` - 按下时背景色

## 实施步骤

### 1. 修改 App.xaml
添加标题栏前景色资源定义。

### 2. 修改 ShellPage.xaml
更新标题栏文本样式，使用主题资源。

### 3. 修改 ShellPage.xaml.cs
- 更新 `MainWindow_Activated` 方法
- 添加主题变化监听
- 添加标题栏按钮颜色设置方法

### 4. 修改 ThemeSelectorService.cs
在主题切换时触发标题栏颜色更新。

## 预期结果
- 深色主题下标题栏文字显示为浅色
- 浅色主题下标题栏文字显示为深色
- 切换主题时标题栏颜色立即更新
- 标题栏按钮颜色正确显示
- hover 状态颜色正确
