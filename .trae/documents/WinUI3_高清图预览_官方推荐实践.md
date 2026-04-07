# WinUI 3 高清图预览 - 官方推荐实践

## 目录
- [一、当前实现分析](#一当前实现分析)
- [二、官方推荐实践](#二官方推荐实践)
  - [1. ScrollView vs ScrollViewer](#1-scrollview-vs-scrollviewer)
  - [2. 图片自适应展示（正比例完整显示）](#2-图片自适应展示正比例完整显示)
  - [3. 缩放和平移](#3-缩放和平移)
  - [4. ConnectedAnimation 连接动画](#4-connectedanimation-连接动画)
  - [5. 组件生命周期管理](#5-组件生命周期管理)
- [三、具体实现建议](#三具体实现建议)
- [四、参考代码示例](#四参考代码示例)

---

## 一、当前实现分析

### 当前代码结构
- **ImageViewerControl.xaml**: 使用 `ScrollView` 控件
- **当前问题**:
  - `Image.Stretch` 设置为 `None`，图片不会自适应
  - `ScrollView.ContentOrientation` 未设置，默认是 `Vertical`
  - 手动实现了滚轮缩放和拖动平移，而不是使用内置功能

---

## 二、官方推荐实践

### 1. ScrollView vs ScrollViewer

| 特性 | ScrollView（推荐用于 WinUI 3） | ScrollViewer |
|-----|------------------------------|--------------|
| 基础 | 基于 InteractionTracker | 传统实现 |
| 新特性 | 支持动画驱动的视图变化 | 不支持 |
| ItemsRepeater | 设计为与 ItemsRepeater 完全兼容 | 兼容性一般 |
| 推荐度 | ⭐⭐⭐⭐⭐ 官方推荐 | ⭐⭐⭐ 传统使用 |

**官方结论**: WinUI 3 中优先使用 **ScrollView** 控件。

### 2. 图片自适应展示（正比例完整显示）

#### 核心需求
图片需要**正比例**地在可见区域内**完整展示**，宽和高都要考虑。

#### 官方推荐方案（使用 ScrollView）

根据官方文档，要实现图片初始时自适应视口完整显示：

```xaml
<ScrollView 
    Width="500" 
    Height="400"
    ContentOrientation="None"  <!-- 关键：限制内容不超出视口 -->
    ZoomMode="Enabled">
    <Image Source="Assets/rainier.jpg"/>
</ScrollView>
```

**关键点说明**:
- `ContentOrientation="None"`: 约束内容到 ScrollView 的视口，图片会自动缩放以完整显示
- `ZoomMode="Enabled"`: 启用缩放功能
- 这样图片初始时会完整显示在视口内（正比例缩放），用户可以放大查看细节

#### 另一种方案：使用 Viewbox + ScrollViewer（适用于 ScrollViewer）

如果使用 ScrollViewer，官方文档提到：

> 在 ScrollViewer 中，必须将滚动条可见性设置为 Disabled，以便将图像限制为视区大小。但是，这意味着用户在放大后无法滚动图像。
> 
> 若要在 ScrollViewer 中启用所需的行为，需要让滚动条保持启用状态，然后插入一个 Viewbox 并将其 MaxHeight/MaxWidth 绑定到 ScrollViewer 的 ViewportHeight/ViewportWidth。

但这个方案比较复杂，**推荐使用 ScrollView + ContentOrientation="None"**。

### 3. 缩放和平移

#### 官方推荐：使用内置功能，不要手动实现！

**ScrollView 内置功能**:
- ✅ 捏合手势缩放（触摸）
- ✅ Ctrl+鼠标滚轮缩放
- ✅ 触摸/笔平移
- ✅ 鼠标拖动（需要配置）
- ✅ `MinZoomFactor` / `MaxZoomFactor` 控制缩放范围
- ✅ `ZoomTo()` / `ZoomBy()` 程序化缩放
- ✅ `ScrollTo()` / `ScrollBy()` 程序化滚动

**不需要手动实现的功能**:
- ❌ 不要自己写 `PointerWheelChanged` 处理缩放
- ❌ 不要自己写 `PointerPressed`/`PointerMoved`/`PointerReleased` 处理平移

#### 推荐配置

```xaml
<ScrollView
    x:Name="ImageScrollView"
    ContentOrientation="None"
    ZoomMode="Enabled"
    MinZoomFactor="0.1"
    MaxZoomFactor="10.0"
    HorizontalScrollBarVisibility="Auto"
    VerticalScrollBarVisibility="Auto">
    <Image
        x:Name="mainImage"
        Stretch="Uniform"  <!-- 保持比例 -->
        HorizontalAlignment="Center"
        VerticalAlignment="Center"/>
</ScrollView>
```

**注意**: 
- `Stretch="Uniform"`: 保持宽高比缩放
- `ContentOrientation="None"`: 初始时约束到视口
- 当用户放大后，滚动条会自动出现

### 4. ConnectedAnimation 连接动画

#### 官方推荐实践

**关键要点**:
1. 调用 `TryStart` 时，目标元素应该在布局中已确定大小
2. 如果是 ListView/GridView 项目，可能需要手动更新内容以准备动画
3. 避免与 `NavigationThemeTransition` 和 `EntranceThemeTransition` 一起使用

#### 当前实现分析

你的代码（MainPage.xaml.cs）已经做得很好：

```csharp
// ✅ 每次打开都创建新实例
var newViewer = new Controls.ImageViewerControl();
ViewerContainer.Content = newViewer;
_currentViewer = newViewer;

// ✅ 准备动画
if (ImageGridView.ContainerFromItem(imageFileInfo) is GridViewItem container)
{
    ImageGridView.PrepareConnectedAnimation("ForwardConnectedAnimation", imageFileInfo, "thumbnailImage");
}

// ✅ 启动动画
var imageAnimation = ConnectedAnimationService.GetForCurrentView().GetAnimation("ForwardConnectedAnimation");
if (imageAnimation != null)
{
    imageAnimation.TryStart(newViewer.GetMainImage(), newViewer.GetCoordinatedElements());
}
```

**改进建议**: 可以设置动画配置

```csharp
// 向前导航（进入详情页）使用 Gravity 配置
if (imageAnimation != null)
{
    imageAnimation.Configuration = new GravityConnectedAnimationConfiguration();
    imageAnimation.TryStart(newViewer.GetMainImage(), newViewer.GetCoordinatedElements());
}

// 向后导航（返回列表页）使用 Direct 配置
var backAnimation = ConnectedAnimationService.GetForCurrentView().GetAnimation("BackConnectedAnimation");
if (backAnimation != null)
{
    backAnimation.Configuration = new DirectConnectedAnimationConfiguration();
    await ImageGridView.TryStartConnectedAnimationAsync(backAnimation, _storedImageFileInfo, "thumbnailImage");
}
```

### 5. 组件生命周期管理

#### 你的需求
> 关闭时销毁本次组件对象，下次图片重新用新实例来展示图片

#### 当前实现（已正确！）

```csharp
// MainPage.xaml.cs 中：
private async void ImageViewer_Closed(object? sender, EventArgs e)
{
    if (_currentViewer != null && _storedImageFileInfo != null)
    {
        // ... 返回动画 ...
        
        await _currentViewer.CompleteCloseAsync();

        // ✅ 清理资源
        _currentViewer.Closed -= ImageViewer_Closed;
        ViewerContainer.Content = null;  // 从视觉树移除
        _currentViewer = null;            // 释放引用
    }
}

// 打开时：
var newViewer = new Controls.ImageViewerControl();  // ✅ 每次新建
```

**这是正确的做法**！每次打开新图片都创建新的 ImageViewerControl 实例，关闭时完全清理。

---

## 三、具体实现建议

### 建议的修改清单

#### 1. ImageViewerControl.xaml 简化

```xaml
<UserControl
    x:Class="PhotoView.Controls.ImageViewerControl"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid
        x:Name="BackgroundOverlay"
        Background="#80000000"
        Opacity="0"
        Tapped="BackgroundOverlay_Tapped">
        
        <Grid
            x:Name="AnimationContainer"
            Opacity="0">
            
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="400"/>
            </Grid.ColumnDefinitions>

            <!-- 使用官方推荐的 ScrollView 配置 -->
            <ScrollView
                x:Name="ImageScrollView"
                Grid.Column="0"
                Background="{ThemeResource SolidBackgroundFillColorBaseBrush}"
                ContentOrientation="None"
                ZoomMode="Enabled"
                MinZoomFactor="0.1"
                MaxZoomFactor="10.0"
                HorizontalScrollBarVisibility="Auto"
                VerticalScrollBarVisibility="Auto">
                <Image
                    x:Name="mainImage"
                    Stretch="Uniform"
                    HorizontalAlignment="Center"
                    VerticalAlignment="Center"
                    DoubleTapped="mainImage_DoubleTapped"/>
            </ScrollView>

            <!-- 信息面板保持不变 -->
            <Border
                x:Name="InfoPanel"
                Grid.Column="1"
                Background="{ThemeResource AcrylicBackgroundFillColorDefaultBrush}"
                Padding="24"
                Tapped="InfoPanel_Tapped">
                <!-- ... 现有内容 ... -->
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

#### 2. ImageViewerControl.xaml.cs 简化

删除以下手动实现的事件处理（使用 ScrollView 内置功能）：

- ❌ `ImageScrollViewer_PointerWheelChanged` - 不需要，ScrollView 内置 Ctrl+滚轮缩放
- ❌ `ImageScrollViewer_PointerPressed` - 不需要
- ❌ `ImageScrollViewer_PointerMoved` - 不需要
- ❌ `ImageScrollViewer_PointerReleased` - 不需要
- ❌ `ImageScrollViewer_PointerCanceled` - 不需要

保留：
- ✅ `mainImage_DoubleTapped` - 双击切换 1:1 / 自适应
- ✅ `ApplyInitialZoomToFit` - 可以用 ScrollView 内置功能替代

#### 3. 双击切换实现

```csharp
private void mainImage_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
{
    if (_is1To1Scale)
    {
        // 切换到自适应模式
        ImageScrollView.ZoomTo(_originalZoomFactor, null);
        _is1To1Scale = false;
    }
    else
    {
        // 切换到 1:1 模式
        _originalZoomFactor = ImageScrollView.ZoomFactor;
        ImageScrollView.ZoomTo(1.0f, null);
        _is1To1Scale = true;
    }
    e.Handled = true;
}
```

#### 4. 初始自适应实现

```csharp
private void ApplyInitialZoomToFit()
{
    if (_imageFileInfo == null)
        return;

    // 等待布局完成
    ImageScrollView.SizeChanged += (s, e) =>
    {
        if (_hasAppliedInitialZoom)
            return;

        var viewerWidth = ImageScrollView.ViewportWidth;
        var viewerHeight = ImageScrollView.ViewportHeight;

        if (viewerWidth <= 0 || viewerHeight <= 0)
            return;

        var imageWidth = (double)_imageFileInfo.Width;
        var imageHeight = (double)_imageFileInfo.Height;

        if (imageWidth <= 0 || imageHeight <= 0)
            return;

        // 计算缩放比例，让图片完整显示在视口内
        var scaleX = viewerWidth / imageWidth;
        var scaleY = viewerHeight / imageHeight;
        var fitScale = Math.Min(scaleX, scaleY);
        fitScale = Math.Max(0.1, fitScale);

        // 使用 ScrollView.ZoomTo 方法
        ImageScrollView.ZoomTo((float)fitScale, null);
        _originalZoomFactor = fitScale;
        _is1To1Scale = false;
        _hasAppliedInitialZoom = true;
    };
}
```

---

## 四、参考代码示例

### 完整的 ScrollView 图片查看器（官方示例）

```xaml
<!-- 来自官方文档的示例 -->
<Page
    x:Class="PhotoView.DemoPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid>
        <StackPanel Spacing="16" Padding="16">
            <Slider 
                x:Name="ZoomSlider"
                Header="Zoom"
                Minimum="0.1"
                Maximum="10.0"
                StepFrequency="0.1"
                Value="1.0"
                ValueChanged="ZoomSlider_ValueChanged"/>
            
            <ScrollView
                x:Name="ImageScrollView"
                Width="600"
                Height="400"
                ContentOrientation="None"
                ZoomMode="Enabled"
                MinZoomFactor="0.1"
                MaxZoomFactor="10.0">
                <Image 
                    Source="Assets/sample-image.jpg"
                    Stretch="Uniform"/>
            </ScrollView>
        </StackPanel>
    </Grid>
</Page>
```

```csharp
private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
{
    if (ImageScrollView != null)
    {
        ImageScrollView.ZoomTo((float)e.NewValue, null);
    }
}
```

---

## 总结

### 关键要点
1. ✅ **使用 ScrollView**，而不是 ScrollViewer
2. ✅ **ContentOrientation="None"** 实现初始自适应
3. ✅ **Stretch="Uniform"** 保持比例
4. ✅ **使用内置缩放和平移**，不要手动实现
5. ✅ **每次新建实例 + 关闭时完全清理**
6. ✅ **ConnectedAnimation 配置**：向前用 Gravity，向后用 Direct

### 参考文档
- [滚动查看器控件 - Microsoft Learn](https://learn.microsoft.com/zh-cn/windows/apps/design/controls/scroll-controls)
- [连接动画 - Microsoft Learn](https://learn.microsoft.com/zh-cn/windows/apps/design/motion/connected-animation)
- [WinUI 3 Gallery](https://aka.ms/winui3gallery) - 建议下载查看实际示例

---

**最后更新**: 2026-04-07
