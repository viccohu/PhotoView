# 信息栏 Shimmer 骨架闪光遮罩实现计划

## 现状分析

### 已有资源
1. **ShimmerControl**：项目已成熟的骨架控件 (`d:\PhotoView\PhotoView\Controls\ShimmerControl.xaml/.cs`)
2. **ImageViewerViewModel**：已有 IsLoadingExif 属性
3. **ImageViewerControl.xaml**：当前已把整个信息栏包在一个 ShimmerControl 里

### 属性加载分析
| 属性分类 | 属性列表 | 加载时机 |
|---------|---------|---------|
| **已有（不需要骨架）** | ImageName, Resolution, Rating, RatingSource, FileFormat, FileFormatColor, FilePaths | SetBasicInfo |
| **待加载（需要骨架）** | FileSize, CaptureYear/CaptureMonth/CaptureDay/CaptureTimeOfDay, Dpi, BitDepth, DeviceInfo, LensModel, FocalLength, ExposureTime, FNumber, Iso | LoadFileDetailsAsync |

---

## 实现方案

### 方案选择
由于所有 EXIF 相关属性都是**同时加载完成**的，我们不需要为每个属性单独标记。

**使用单一 IsLoadingExif 标志：**
- SetBasicInfo 时：IsLoadingExif = true
- LoadFileDetailsAsync 完成：IsLoadingExif = false
- 根据这个标志，对每个待加载的属性节点使用 ShimmerControl

---

## 实施步骤

### 1. 修改 ImageViewerControl.xaml

**当前状态：**
- 整个信息栏被一个 ShimmerControl 包裹
- 需要改为：只对**待加载的属性**使用 ShimmerControl

**修改位置：**
```
文件信息区域（大小、DPI、色深）
设备信息区域（相机、镜头、焦距、快门、光圈、ISO）
```

**具体修改：**
1. 移除包裹整个信息栏的 ShimmerControl
2. 在以下数据节点上添加 ShimmerControl：
   - 文件大小
   - 日期时间（年/月/日/时间）
   - DPI
   - 色深
   - 相机信息
   - 镜头
   - 焦距
   - 快门
   - 光圈
   - ISO
3. ShimmerControl.IsLoading 绑定到 ViewModel.IsLoadingExif

---

## 修改清单

| 文件 | 修改内容 |
|-----|---------|
| `ImageViewerControl.xaml` | 移除全局 ShimmerControl，在各数据节点添加 ShimmerControl |

---

## 效果说明

### 展示逻辑
- **已有数据**：直接显示，无遮罩
- **待加载数据**：显示 Shimmer 骨架闪光遮罩
- **加载完成后**：骨架遮罩消失，显示真实数据
