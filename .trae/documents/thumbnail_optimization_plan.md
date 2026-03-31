# 缩略图优化计划 - 使用系统缩略图 API

## 当前实现分析

### 现有问题
1. **MainPage.xaml 第 111 行**：`Source="{x:Bind ImageFile.Path}"` - 直接绑定文件路径，Image 控件会自己解码原图
2. **性能问题**：每次显示图片都要解码原图，内存占用高，加载慢
3. **未使用的代码**：`ImageFileInfo.GetImageThumbnailAsync()` 方法存在但未被使用

### Windows 缩略图 API
Windows.Storage API 提供了 `GetThumbnailAsync` 方法：
```csharp
StorageItemThumbnail thumbnail = await file.GetThumbnailAsync(
    ThumbnailMode.PicturesView,  // 缩略图模式
    requestedSize,               // 请求尺寸（像素）
    ThumbnailOptions             // 选项
);
```

### 缩略图尺寸建议
| 尺寸 | 像素 | 用途 |
|------|------|------|
| 小 | 96 | 列表视图、快速预览 |
| 中 | 200 | 缩略图网格（当前默认） |
| 大 | 400 | 详情预览 |

---

## 实施计划

### [ ] 任务 1：定义缩略图尺寸枚举
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 创建 `ThumbnailSize` 枚举，定义 Small、Medium、Large 三种尺寸
  - 定义对应的像素值
- **成功标准**：枚举可被其他类使用
- **测试要求**：
  - `programmatic` TR-1.1：枚举编译无错误

### [x] 任务 2：更新 ImageFileInfo 添加缩略图获取方法
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 添加 `GetThumbnailAsync(ThumbnailSize size)` 方法
  - 使用 `StorageFile.GetThumbnailAsync` 获取系统缓存的缩略图
  - 返回 `BitmapImage` 供 UI 绑定
- **成功标准**：可以获取指定尺寸的缩略图
- **测试要求**：
  - `programmatic` TR-2.1：方法返回有效的 BitmapImage
  - `human-judgment` TR-2.2：缩略图清晰度符合预期

### [x] 任务 3：创建缩略图转换器
- **优先级**：P0
- **依赖**：任务 2
- **描述**：
  - 创建 `ThumbnailConverter : IValueConverter`
  - 实现异步图片加载逻辑
  - 支持传入 ThumbnailSize 参数
- **成功标准**：转换器可在 XAML 中使用
- **测试要求**：
  - `programmatic` TR-3.1：转换器正确返回 BitmapImage
  - `human-judgment` TR-3.2：图片加载流畅

### [x] 任务 4：更新 MainPage.xaml 使用缩略图
- **优先级**：P0
- **依赖**：任务 3
- **描述**：
  - 将 `Source="{x:Bind ImageFile.Path}"` 改为使用转换器
  - 配置使用中等尺寸缩略图
- **成功标准**：页面显示缩略图而非原图
- **测试要求**：
  - `programmatic` TR-4.1：页面加载无错误
  - `human-judgment` TR-4.2：缩略图显示正常

### [x] 任务 5：添加缩略图缓存（可选优化）
- **优先级**：P1
- **依赖**：任务 2
- **描述**：
  - 在 `ImageFileInfo` 中缓存已加载的缩略图
  - 避免重复请求系统缩略图
- **成功标准**：同一图片不重复获取缩略图
- **测试要求**：
  - `programmatic` TR-5.1：滚动时不会重复获取缩略图

---

## 技术要点

### Windows 缩略图 API 使用
```csharp
// 获取缩略图
StorageItemThumbnail thumbnail = await file.GetThumbnailAsync(
    ThumbnailMode.PicturesView,
    requestedSize,
    ThumbnailOptions.UseCurrentScale
);

// 转换为 BitmapImage
var bitmapImage = new BitmapImage();
bitmapImage.SetSource(thumbnail);
thumbnail.Dispose();
```

### 转换器异步加载模式
```csharp
public object Convert(object value, Type targetType, object parameter, string language)
{
    if (value is StorageFile file)
    {
        var size = parameter is ThumbnailSize s ? s : ThumbnailSize.Medium;
        _ = LoadThumbnailAsync(file, size);
        return new BitmapImage(); // 先返回空图片
    }
    return null;
}
```

---

## 预期结果
1. 图片显示使用系统缓存的缩略图，加载更快
2. 内存占用大幅降低
3. 支持大中小三种缩略图尺寸
4. 滚动流畅度提升
