# PhysicsImageViewer 使用说明

## 概述

`PhysicsImageViewer` 是一个独立的、可复用的 WinUI 3 图片查看器控件，支持物理驱动的交互体验。

## 文件位置

- **控件代码**: `Controls/PhysicsImageViewer.cs`
- **控件样式**: `Controls/Themes/Generic.xaml`

## 移植到其他项目

只需复制以下文件到目标项目：
1. `Controls/PhysicsImageViewer.cs`
2. `Controls/Themes/Generic.xaml`

然后在 `App.xaml` 中添加资源字典引用：

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Controls/Themes/Generic.xaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

## 使用方法

### XAML 中使用

```xml
<controls:PhysicsImageViewer x:Name="ImageViewer"
    Source="{x:Bind CurrentImage}"
    OriginalWidth="{x:Bind CurrentItem.Width}"
    OriginalHeight="{x:Bind CurrentItem.Height}"
    MinZoom="0.1"
    MaxZoom="8.0"
    ZoomChanged="OnZoomChanged" />
```

### 代码中使用

```csharp
// 设置图片源
ImageViewer.Source = bitmapImage;

// 设置原图尺寸（用于正确的缩放百分比计算）
ImageViewer.OriginalWidth = photoItem.Width;
ImageViewer.OriginalHeight = photoItem.Height;

// 重置缩放
ImageViewer.ResetZoom();

// 设置缩放
ImageViewer.SetZoom(2.0);

// 旋转
ImageViewer.RotateLeft();
ImageViewer.RotateRight();

// 翻转
ImageViewer.FlipHorizontal();
ImageViewer.FlipVertical();
```

## 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Source` | BitmapImage | 图片源 |
| `OriginalWidth` | double | 原图宽度（用于缩放百分比计算） |
| `OriginalHeight` | double | 原图高度 |
| `MinZoom` | double | 最小缩放倍数（默认 0.1） |
| `MaxZoom` | double | 最大缩放倍数（默认 8.0） |
| `ZoomPercentage` | int | 当前缩放百分比（只读） |
| `CurrentZoomScale` | double | 当前缩放值（只读） |
| `CurrentRotation` | double | 当前旋转角度（只读） |
| `IsFlippedHorizontal` | bool | 是否水平翻转（只读） |
| `IsFlippedVertical` | bool | 是否垂直翻转（只读） |

## 事件

| 事件 | 说明 |
|------|------|
| `ZoomChanged` | 缩放百分比变化时触发，参数为新的缩放百分比 |
| `Reset` | 重置时触发 |

## 方法

| 方法 | 说明 |
|------|------|
| `ResetZoom()` | 重置缩放、旋转、翻转 |
| `SetZoom(double scale)` | 设置目标缩放值（平滑过渡） |
| `RotateLeft()` | 向左旋转 90° |
| `RotateRight()` | 向右旋转 90° |
| `FlipHorizontal()` | 水平翻转 |
| `FlipVertical()` | 垂直翻转 |

## 特性

### 惯性拖动
- 松手后图片继续滑动
- 速度逐渐衰减直到停止
- 衰减系数：0.92

### 鼠标中心缩放
- 缩放时鼠标位置保持对准图片同一点
- 缩放过程平滑跟手
- 缓动系数：0.15

### 阻尼边界
- 拖动超出边界时有阻力感（阻尼系数：0.3）
- 松手后平滑回弹到边界
- 回弹系数：0.12

### 100% 吸附
- 缩放经过 100% 时自动吸附
- 需要继续滚动才能离开 100%
- 吸附阈值：5%

### 平滑动画
- 所有变换都有缓动效果
- 双击重置有平滑动画

## 物理参数调整

如需调整手感，可修改以下常量：

```csharp
private const double InertiaDamping = 0.92;      // 惯性衰减（越大滑得越远）
private const double ZoomEasingFactor = 0.15;    // 缩放缓动（越大响应越快）
private const double BoundaryDamping = 0.3;      // 边界阻尼（越大越难拖出边界）
private const double SpringBackFactor = 0.12;    // 回弹速度（越大回弹越快）
private const double VelocityThreshold = 0.5;    // 速度阈值（低于此值停止）
```

## 依赖

- WinUI 3 (Microsoft.WindowsAppSDK)
- .NET 9.0 或更高版本
