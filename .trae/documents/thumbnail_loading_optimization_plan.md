# 缩略图尺寸和加载逻辑优化计划

## 需求分析

### 1. 缩略图尺寸调整
| 选项 | 当前高度 | 目标高度 |
|------|----------|----------|
| 小 | 128px | 140px |
| 中 | 256px | 256px（不变） |
| 大 | 512px | 512px（不变） |

宽度按图片正比例智能调整（已实现）

### 2. 缩略图加载逻辑优化
**目标流程**：
```
开始加载 → 得到所有图片尺寸 → 渲染所需图片框（空，占位）→ 异步获取缩略图填入占位框 → 调整大小后，先调整占位框的大小，再智能获取新缩略图填入
```

**当前问题**：
1. 没有占位框概念，加载时图片区域为空白
2. 缩略图缓存没有考虑尺寸变化，切换大小后可能显示旧尺寸的缓存缩略图
3. 调整大小时，布局先变化，但缩略图还是旧的

---

## 实施计划

### [ ] 任务 1：更新 ThumbnailSize 枚举
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 将 `Small` 的值从 128 改为 140
- **成功标准**：枚举值与需求一致
- **测试要求**：
  - `programmatic` TR-1.1：枚举编译无错误

### [ ] 任务 2：添加占位框显示逻辑
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 在 MainPage.xaml 中为 Image 添加占位背景色
  - 显示加载中的视觉反馈（灰色背景或加载动画）
- **成功标准**：加载时显示占位效果
- **测试要求**：
  - `human-judgment` TR-2.1：加载时有明显的占位视觉效果

### [ ] 任务 3：优化缩略图缓存机制
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 修改 `ImageFileInfo` 的缓存机制
  - 缓存按尺寸存储，支持多个尺寸的缓存
  - 或在尺寸变化时清除旧缓存
- **成功标准**：切换大小时获取正确尺寸的缩略图
- **测试要求**：
  - `programmatic` TR-3.1：切换大小后缩略图尺寸正确

### [ ] 任务 4：优化调整大小的流程
- **优先级**：P0
- **依赖**：任务 3
- **描述**：
  - 调整大小时，先更新布局（占位框大小）
  - 然后异步获取新尺寸的缩略图
  - 确保布局变化和缩略图加载解耦
- **成功标准**：调整大小时体验流畅
- **测试要求**：
  - `human-judgment` TR-4.1：调整大小时布局先变化，缩略图随后加载
  - `programmatic` TR-4.2：布局变化和缩略图加载不阻塞 UI

### [x] 任务 1：更新 ThumbnailSize 枚举
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 将 `Small` 的值从 128 改为 140
- **成功标准**：枚举值与需求一致
- **测试要求**:
  - `programmatic` TR-1.1：枚举编译无错误

### [x] 任务 2：添加占位框显示逻辑
- **优先级**:P0
- **依赖**:无
- **描述**：
  - 在 MainPage.xaml 中为 Image 添加占位背景色
  - 显示加载中的视觉反馈（灰色背景或 ProgressRing 加载指示器）
- **成功标准**：加载时显示占位效果
- **测试要求**:
  - `human-judgment` TR-2.1: 加载时有明显的占位视觉效果

### [x] 任务 3：优化缩略图缓存机制
- **优先级**:P0
- **依赖**:任务 1
- **描述**：
  - 修改 `ImageFileInfo` 的缓存机制
  - 缓存按尺寸存储，支持多个尺寸的缓存
  - 或在尺寸变化时清除旧缓存
- **成功标准**:切换大小时获取正确尺寸的缩略图
- **测试要求**:
  - `programmatic` TR-3.1: 切换大小后缩略图尺寸正确

### [x] 任务 4：优化调整大小的流程
- **优先级**:P0
- **依赖**:任务 3
- **描述**：
  - 调整大小时，先更新布局（占位框大小）
  - 然后异步获取新尺寸的缩略图
- **成功标准**:调整大小时体验流畅
- **测试要求**:
  - `human-judgment` TR-4.1: 调整大小时布局先变化，缩略图随后加载
  - `programmatic` TR-4.2: 布局变化和缩略图加载不阻塞 UI

### [x] 任务 5：添加加载状态指示
- **优先级**:P1
- **依赖**:任务 2
- **描述**：
  - 在图片框上显示加载中的进度指示器
  - 加载完成后隐藏指示器
- **成功标准**:用户能看到加载进度
- **测试要求**:
  - `human-judgment` TR-5.1: 加载时有进度指示

---

## 技术要点

### 占位框实现
```xml
<Grid Background="{ThemeResource CardBackgroundFillColorSecondaryBrush}">
    <Image x:Name="ThumbnailImage" Stretch="UniformToFill"/>
    <ProgressRing IsActive="True" Width="24" Height="24" 
                  x:Name="LoadingIndicator"/>
</Grid>
```

### 多尺寸缓存
```csharp
private Dictionary<ThumbnailSize, BitmapImage> _thumbnailCache = new();

public async Task<BitmapImage> GetThumbnailAsync(ThumbnailSize size)
{
    if (_thumbnailCache.TryGetValue(size, out var cached))
    {
        return cached;
    }
    
    var thumbnail = await LoadThumbnailAsync(size);
    _thumbnailCache[size] = thumbnail;
    return thumbnail;
}
```

### 加载状态管理
```csharp
private async void ImageRepeater_ElementPrepared(...)
{
    // 显示加载指示器
    ShowLoadingIndicator(control);
    
    // 异步加载缩略图
    var thumbnail = await imageInfo.GetThumbnailAsync(ViewModel.ThumbnailSize);
    
    // 更新图片并隐藏指示器
    thumbnailImage.Source = thumbnail;
    HideLoadingIndicator(control);
}
```

---

## 预期结果
1. 缩略图小尺寸高度为 140px
2. 加载时显示占位框和加载指示器
3. 切换大小时布局先变化，缩略图异步加载
4. 多尺寸缓存，切换大小时不会重复加载已有缓存的尺寸
