# EXIF信息处理服务整合计划

## 目标
将整个EXIF信息处理整合成一个统一的ExifService，所有图片通过BitmapDecoder获取基本EXIF信息，同时整合评级功能。

## 现状分析

### 当前EXIF处理分散在多处：
1. **ImageViewerControl.xaml.cs**：
   - `LoadImagePropertiesFromWinRTAsync` - 使用ImageProperties读取JPG等文件的EXIF
   - `LoadImagePropertiesFromWicAsync` - 使用BitmapDecoder读取RAW文件的EXIF

2. **MainViewModel.cs**：
   - `LoadImageInfoSafeAsync` - 混合使用ImageProperties和BitmapDecoder获取图片尺寸和方向

3. **RatingService.cs**：
   - 独立处理评级信息的读取和写入

### 问题：
- EXIF处理逻辑分散，代码重复
- 不同文件类型使用不同的API，维护困难
- 评级功能与EXIF信息分离，不够统一

## 实施计划

### 第一步：创建ExifData模型类
**文件**：`Models/ExifData.cs`

创建一个统一的EXIF数据模型，包含：

#### 基本属性：
- 拍摄日期（DateTaken）
- 相机制造商（CameraManufacturer）
- 相机型号（CameraModel）
- 图片宽度（Width）
- 图片高度（Height）
- 方向（Orientation）

#### 拍摄参数：
- 曝光时间（ExposureTime）- 快门速度，单位：秒
- 光圈值（FNumber）- F值，如f/2.8、f/4等
- ISO感光度（ISOSpeed）
- 焦距（FocalLength）- 单位：毫米
- 35mm等效焦距（FocalLengthInFilm）
- 曝光程序（ExposureProgram）- 手动、光圈优先、快门优先等
- 曝光补偿（ExposureBias）
- 最大光圈（MaxAperture）

#### 其他信息：
- 闪光灯状态（Flash）
- 测光模式（MeteringMode）
- 白平衡（WhiteBalance）
- 镜头型号（LensModel）

#### 评级信息：
- 评级（Rating）
- 评级来源（RatingSource）

#### 辅助方法：
```csharp
public string GetFormattedExposureTime()
{
    if (ExposureTime == null) return string.Empty;
    
    double time = ExposureTime.Value;
    if (time < 1.0)
    {
        int denominator = (int)Math.Round(1.0 / time);
        return $"1/{denominator}s";
    }
    else
    {
        return $"{time:F1}s";
    }
}

public string GetFormattedFNumber()
{
    if (FNumber == null) return string.Empty;
    return $"f/{FNumber.Value:F1}";
}

public string GetFormattedFocalLength()
{
    if (FocalLength == null) return string.Empty;
    return $"{FocalLength.Value:F0}mm";
}

public string GetFormattedExposureProgram()
{
    return ExposureProgram switch
    {
        1 => "Manual",
        2 => "Normal",
        3 => "Aperture Priority",
        4 => "Shutter Priority",
        5 => "Creative",
        6 => "Action",
        7 => "Portrait",
        8 => "Landscape",
        _ => "Unknown"
    };
}
```

### 第二步：创建ExifService服务
**文件**：`Services/ExifService.cs`

整合所有EXIF相关功能：

#### 1. 基本EXIF信息读取（使用BitmapDecoder）
```csharp
public async Task<ExifData> GetExifDataAsync(StorageFile file, CancellationToken cancellationToken = default)
```
- 使用BitmapDecoder统一读取所有图片格式的基本EXIF信息
- 包括：拍摄日期、相机制造商、相机型号、宽度、高度、方向
- 拍摄参数：曝光时间、光圈值、ISO、焦距、曝光程序、闪光灯状态等

#### 读取的EXIF属性列表：
```csharp
private static readonly string[] ExifPropertyNames = {
    // 基本属性
    "System.Photo.DateTaken",
    "System.Photo.CameraManufacturer",
    "System.Photo.CameraModel",
    "System.Photo.Orientation",
    
    // 拍摄参数
    "System.Photo.ExposureTime",
    "System.Photo.FNumber",
    "System.Photo.ISOSpeed",
    "System.Photo.FocalLength",
    "System.Photo.FocalLengthInFilm",
    "System.Photo.ExposureProgram",
    "System.Photo.ExposureBias",
    "System.Photo.MaxAperture",
    
    // 其他信息
    "System.Photo.Flash",
    "System.Photo.MeteringMode",
    "System.Photo.WhiteBalance",
    "System.Photo.LensModel"
};
```

#### 2. 评级信息读取（整合RatingService逻辑）
```csharp
public async Task<(uint Rating, RatingSource Source)> GetRatingAsync(StorageFile file)
```
- 对于支持的文件格式（.jpg, .jpeg, .tif, .tiff, .png, .heic, .heif, .dng），使用ImageProperties API
- 对于不支持的文件格式（RAW格式），使用RatingCacheService从本地缓存读取

#### 3. 评级信息写入（整合RatingService逻辑）
```csharp
public async Task SetRatingAsync(StorageFile file, uint rating)
```
- 对于支持的文件格式，使用ImageProperties.Rating并调用SavePropertiesAsync()
- 对于不支持的文件格式，保存到本地缓存文件rating_cache.json

#### 4. 完整EXIF数据获取（包含评级）
```csharp
public async Task<ExifData> GetFullExifDataAsync(StorageFile file, CancellationToken cancellationToken = default)
```
- 获取基本EXIF信息 + 评级信息

### 第三步：更新依赖注入配置
**文件**：`App.xaml.cs`

注册ExifService服务，保持RatingCacheService作为依赖。

### 第四步：重构ImageViewerControl
**文件**：`Controls/ImageViewerControl.xaml.cs`

修改`LoadFileInfoAsync`方法：
- 移除`LoadImagePropertiesFromWinRTAsync`和`LoadImagePropertiesFromWicAsync`
- 使用ExifService统一获取EXIF数据
- 简化代码逻辑

更新UI显示：
- 添加拍摄参数显示面板
- 显示快门速度、光圈、ISO、焦距等信息
- 使用友好的格式化显示（如 1/250s、f/2.8、ISO 100）

### UI设计建议：

#### 拍摄参数显示面板布局：
```xml
<StackPanel x:Name="ShootingParamsPanel" Visibility="Collapsed">
    <TextBlock Text="拍摄参数" Style="{StaticResource SubtitleTextBlockStyle}"/>
    
    <Grid ColumnSpacing="8">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        
        <!-- 快门速度 -->
        <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="4">
            <FontIcon Glyph="&#xE91B;" FontSize="14"/>
            <TextBlock x:Name="ShutterSpeedTextBlock"/>
        </StackPanel>
        
        <!-- 光圈 -->
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="4">
            <FontIcon Glyph="&#xE91C;" FontSize="14"/>
            <TextBlock x:Name="ApertureTextBlock"/>
        </StackPanel>
        
        <!-- ISO -->
        <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4">
            <TextBlock Text="ISO" FontWeight="Bold"/>
            <TextBlock x:Name="ISOTextBlock"/>
        </StackPanel>
        
        <!-- 焦距 -->
        <StackPanel Grid.Column="3" Orientation="Horizontal" Spacing="4">
            <FontIcon Glyph="&#xE91D;" FontSize="14"/>
            <TextBlock x:Name="FocalLengthTextBlock"/>
        </StackPanel>
    </Grid>
    
    <!-- 其他参数 -->
    <StackPanel Orientation="Horizontal" Spacing="16">
        <TextBlock x:Name="ExposureProgramTextBlock"/>
        <TextBlock x:Name="FlashTextBlock"/>
        <TextBlock x:Name="MeteringModeTextBlock"/>
    </StackPanel>
</StackPanel>
```

### 第五步：重构MainViewModel
**文件**：`ViewModels/MainViewModel.cs`

修改`LoadImageInfoSafeAsync`方法：
- 使用ExifService获取图片尺寸、方向等信息
- 移除重复的EXIF读取代码

### 第六步：更新ImageFileInfo
**文件**：`Models/ImageFileInfo.cs`

修改`LoadRatingAsync`和`SetRatingAsync`方法：
- 使用ExifService替代RatingService

### 第七步：更新MainPage
**文件**：`Views/MainPage.xaml.cs`

修改`UpdateRatingAsync`方法：
- 使用ExifService替代RatingService

### 第八步：保留RatingService作为兼容层（可选）
如果其他地方仍在使用RatingService，可以保留它作为ExifService的包装器，或者完全移除。

## 技术细节

### BitmapDecoder读取EXIF的优势：
1. 统一的API，支持所有图片格式
2. 高效读取，无需完全解码图像
3. 支持选择性读取特定属性

### 支持的EXIF属性：

#### 基本属性：
- `System.Photo.DateTaken` - 拍摄日期
- `System.Photo.CameraManufacturer` - 相机制造商
- `System.Photo.CameraModel` - 相机型号
- `System.Photo.Orientation` - 图片方向
- `System.Image.HorizontalSize` - 图片宽度
- `System.Image.VerticalSize` - 图片高度

#### 拍摄参数：
- `System.Photo.ExposureTime` - 曝光时间（快门速度，单位：秒）
- `System.Photo.FNumber` - 光圈值（F值）
- `System.Photo.ISOSpeed` - ISO感光度
- `System.Photo.FocalLength` - 焦距（单位：毫米）
- `System.Photo.FocalLengthInFilm` - 35mm等效焦距
- `System.Photo.ExposureProgram` - 曝光程序模式（手动、光圈优先、快门优先等）
- `System.Photo.ExposureBias` - 曝光补偿
- `System.Photo.MaxAperture` - 最大光圈
- `System.Photo.ShutterSpeed` - 快门速度（APEX单位）

#### 其他信息：
- `System.Photo.Flash` - 闪光灯状态
- `System.Photo.MeteringMode` - 测光模式
- `System.Photo.WhiteBalance` - 白平衡
- `System.Photo.LensModel` - 镜头型号

### 并发控制：
- 使用SemaphoreSlim限制并发访问（建议4-8个并发）
- 确保线程安全

### 数据格式化：

#### 曝光时间格式化：
- 小于1秒：显示为分数，如 1/250s
- 大于等于1秒：显示为小数，如 2.5s

#### 光圈值格式化：
- 显示为 f/数值，如 f/2.8、f/4

#### 焦距格式化：
- 显示为 数值mm，如 50mm、85mm

#### 曝光程序枚举值：
- 0: Unknown
- 1: Manual（手动）
- 2: Normal（正常）
- 3: Aperture Priority（光圈优先）
- 4: Shutter Priority（快门优先）
- 5: Creative Program（创意程序）
- 6: Action Program（运动程序）
- 7: Portrait Mode（人像模式）
- 8: Landscape Mode（风景模式）

#### 闪光灯状态：
- 0: No Flash（未闪光）
- 1: Fired（已闪光）
- 其他值包含更多信息（强制闪光、防红眼等）

## 文件变更清单

### 新增文件：
1. `Models/ExifData.cs` - EXIF数据模型
2. `Services/ExifService.cs` - EXIF服务

### 修改文件：
1. `App.xaml.cs` - 注册ExifService
2. `Controls/ImageViewerControl.xaml.cs` - 使用ExifService
3. `Controls/ImageViewerControl.xaml` - 添加拍摄参数显示UI
4. `ViewModels/MainViewModel.cs` - 使用ExifService
5. `Models/ImageFileInfo.cs` - 使用ExifService
6. `Views/MainPage.xaml.cs` - 使用ExifService

### 可选删除文件：
1. `Services/RatingService.cs` - 如果完全迁移到ExifService

## 测试计划

1. 测试JPG文件的EXIF读取
2. 测试RAW文件的EXIF读取
3. 测试拍摄参数读取（快门速度、光圈、ISO、焦距等）
4. 测试评级读取和写入（支持和不支持的文件格式）
5. 测试并发访问
6. 测试异常处理
7. 测试数据格式化（曝光时间、光圈值等）
8. 测试缺失EXIF数据的文件

## 预期效果

1. 代码更简洁，EXIF处理逻辑统一
2. 维护更容易，只需修改一处代码
3. 性能优化，统一使用高效的BitmapDecoder
4. 功能完整，支持所有图片格式的EXIF读取和评级管理
5. 拍摄参数完整，包括快门速度、光圈、ISO、焦距等关键信息
6. 数据格式化友好，用户可读性强
