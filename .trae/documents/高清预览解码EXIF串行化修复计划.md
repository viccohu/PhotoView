# 高清预览解码和 EXIF 串行化修复计划

## 🎯 问题分析

### 当前流程（并行，导致崩溃）

在 `ImageViewerControl.xaml.cs:205` 和 `ImageViewerControl.xaml.cs:291`：

1. **PrepareContent** (第 205 行) → `_ = ViewModel.LoadFileInfoAsync(imageFileInfo)`
   - 调用 `LoadFileDetailsAsync()` → 加载 EXIF 信息

2. **ShowAfterAnimationAsync** (第 291 行) → `await SwitchToViewerLayerAsync()`
   - 调用 `LoadHighResolutionImageAsync()` → 解码高清图

**问题：** 这两个操作并行执行，导致程序崩溃。

---

## ✅ 修复方案

### 核心原则

> "先解码图片，完成后再加载 EXIF 信息"

### 修改内容

| 文件 | 修改项 |
|------|---------|
| **ImageViewerControl.xaml** | 添加 CommunityToolkit.WinUI.Controls.Primitives.Skeleton 到信息栏 |
| **ImageViewerControl.xaml.cs** | 添加 `_isLoadingExif` 标志，重构流程 |
| **ImageViewerViewModel.cs** | 分离 EXIF 加载为独立方法，添加加载状态 |

---

## 🔧 详细修改步骤

### 第一步：修改 ImageViewerControl.xaml.cs

#### 1.1 添加 `_isLoadingExif` 标志

```csharp
private bool _isLoadingExif = false;
```

#### 1.2 修改 `PrepareContent` - 移除立即加载 EXIF

```csharp
public void PrepareContent(ImageFileInfo imageFileInfo)
{
    // ... 现有代码 ...
    
    // 移除这一行
    // _ = ViewModel.LoadFileInfoAsync(imageFileInfo);
    
    // 改为只设置基本信息
    ViewModel.SetBasicInfo(imageFileInfo);
}
```

#### 1.3 修改 `SwitchToViewerLayerAsync` - 高清图加载完成后加载 EXIF

```csharp
private async Task SwitchToViewerLayerAsync(DecodeResult? highResResult)
{
    // ... 现有代码 ...
    
    // 高清图加载完成后，再加载 EXIF
    _ = LoadExifAfterImageAsync(imageFileInfo);
}

private async Task LoadExifAfterImageAsync(ImageFileInfo imageFileInfo)
{
    try
    {
        _isLoadingExif = true;
        await ViewModel.LoadFileDetailsAsync(imageFileInfo.ImageFile);
        _isLoadingExif = false;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"LoadExifAfterImageAsync error: {ex}");
        _isLoadingExif = false;
    }
}
```

### 第二步：修改 ImageViewerViewModel.cs

#### 2.1 添加 `IsLoadingExif` 可观察属性

```csharp
[ObservableProperty]
private bool _isLoadingExif = false;
```

#### 2.2 添加 `SetBasicInfo` 方法

```csharp
public void SetBasicInfo(ImageFileInfo imageFileInfo)
{
    if (imageFileInfo == null)
        return;

    _currentImage = imageFileInfo;
    ImageName = imageFileInfo.ImageName;
    Resolution = $"{imageFileInfo.Width} x {imageFileInfo.Height}";
    Rating = imageFileInfo.Rating;
    RatingSource = imageFileInfo.RatingSource.ToString();

    SetFileFormatInfo(imageFileInfo.ImageName);
    LoadFilePaths(imageFileInfo);
    
    // 标记 EXIF 正在加载
    IsLoadingExif = true;
}
```

#### 2.3 重构 `LoadFileInfoAsync` 并公开 `LoadFileDetailsAsync`

```csharp
public async Task LoadFileInfoAsync(ImageFileInfo imageFileInfo)
{
    SetBasicInfo(imageFileInfo);
    
    if (imageFileInfo.ImageFile != null)
    {
        await LoadFileDetailsAsync(imageFileInfo.ImageFile);
    }
}

// 将 LoadFileDetailsAsync 改为 public
public async Task LoadFileDetailsAsync(StorageFile file)
{
    try
    {
        var basicProps = await file.GetBasicPropertiesAsync();
        FileSize = FormatFileSize(basicProps.Size);
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"LoadFileDetailsAsync - 文件大小读取错误: {ex}");
    }

    try
    {
        var exifData = await _exifService.GetExifDataAsync(file);

        // ... 现有 EXIF 处理代码 ...

        // 标记 EXIF 加载完成
        IsLoadingExif = false;
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"LoadFileDetailsAsync - EXIF读取错误: {ex}");
        IsLoadingExif = false;
    }
}
```

### 第三步：修改 ImageViewerControl.xaml

#### 3.1 添加 CommunityToolkit 命名空间

```xml
xmlns:primitives="using:CommunityToolkit.WinUI.Controls.Primitives"
```

#### 3.2 在信息栏添加 Skeleton 占位

在每个信息项上根据 `ViewModel.IsLoadingExif` 显示 Skeleton：

```xml
<StackPanel Spacing="16">
    <!-- 文件名 -->
    <Button Style="{StaticResource InfoCardStyle}" Tapped="FileName_Tapped">
        <Grid ColumnSpacing="12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <FontIcon .../>
            <primitives:Skeleton 
                x:Name="FileNameSkeleton"
                Width="200"
                Height="20"
                VerticalAlignment="Center"
                Visibility="{x:Bind ViewModel.IsLoadingExif, Mode=OneWay}"/>
            <TextBlock 
                Text="{Binding ImageName, Mode=OneWay}"
                TextWrapping="Wrap"
                Style="{ThemeResource BodyTextBlockStyle}"
                VerticalAlignment="Center"
                Visibility="{x:Bind ViewModel.IsLoadingExif, Converter={StaticResource InverseBoolToVisibilityConverter}, Mode=OneWay}"/>
        </Grid>
    </Button>

    <!-- 其他信息项类似添加 Skeleton -->
</StackPanel>
```

---

## 🎯 最终效果

| 效果 | 说明 |
|------|------|
| ✅ 串行化 | 先解码图片，完成后再加载 EXIF |
| ✅ 不再崩溃 | 避免并行操作导致的竞态条件 |
| ✅ 加载状态 | 信息栏显示鱼骨（Skeleton）占位 |
| ✅ 用户体验 | 渐进式加载，视觉反馈好 |

---

## 📝 风险评估

| 风险 | 等级 | 说明 |
|------|------|------|
| 修改范围 | 中 | 涉及 3 个文件 |
| 兼容性 | 高 | 保持公共 API 不变 |
| 测试需要 | 高 | 需要测试高清预览和 EXIF 加载 |
