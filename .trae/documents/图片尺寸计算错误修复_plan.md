# 图片尺寸计算错误修复计划

## 问题概述
横构图显示成了竖构图，竖构图显示成了横构图，尺寸计算出现反转错误。

## 代码库分析结论

### 分析的关键文件
1. **ImageFileInfo.cs** - 包含 `UpdateDisplaySize` 和 `AspectRatio` 属性
2. **MainPage.xaml** - 使用 `DisplayWidth` 和 `DisplayHeight` 进行布局

### 发现的问题

#### 当前实现 (ImageFileInfo.cs:332-359)
```csharp
public void UpdateDisplaySize(ThumbnailSize size)
{
    var designHeight = size switch
    {
        ThumbnailSize.Small => 120d,
        ThumbnailSize.Medium => 256d,
        ThumbnailSize.Large => 512d,
        _ => 256d
    };

    var aspectRatio = AspectRatio;
    
    // ⚠️ 问题：总是固定高度，根据宽高比计算宽度
    var contentHeight = designHeight - borderPadding;
    var contentWidth = contentHeight * aspectRatio;
    
    DisplayWidth = contentWidth + borderPadding;
    DisplayHeight = designHeight;
}
```

#### AspectRatio 计算 (ImageFileInfo.cs:504)
```csharp
public double AspectRatio => Height == 0 ? 1.5 : (double)Width / Height;
```

### 问题根源
**宽高比没有考虑图片方向！** 当图片是竖构图时，应该：
- 固定宽度，根据宽高比计算高度
- 而不是固定高度，根据宽高比计算宽度

## 需要修改的文件
1. **Models/ImageFileInfo.cs** - 修复 UpdateDisplaySize 方法

## 修改步骤

### 步骤 1：修改 UpdateDisplaySize 方法
根据图片是横构图还是竖构图，决定是固定宽度还是固定高度：
- **横构图**（Width > Height）：固定高度，计算宽度
- **竖构图**（Width < Height）：固定宽度，计算高度

### 步骤 2：构建验证
确保修改后构建成功，没有错误

## 风险分析
### 低风险
- 只修改尺寸计算逻辑
- 不影响核心功能
- 计算逻辑简单清晰

## 验证标准
1. 横构图图片显示为横构图
2. 竖构图图片显示为竖构图
3. 尺寸比例正确，没有拉伸变形
---
