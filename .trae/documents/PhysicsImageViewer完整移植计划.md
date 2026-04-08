# PhysicsImageViewer 完整移植计划

## 目标

移除当前自己移植的代码，完整接入参考项目的 PhysicsImageViewer 实现，
**只保留当前项目的鼠标坐标计算方式**。

---

## 参考项目核心优势

| 特性 | 说明 |
|------|------|
| **实际尺寸边界计算** | 使用 `_image.ActualWidth` / `_image.ActualHeight` |
| **完整 100% 吸附** | `_justSnappedTo100Percent` + `_snapStayCounter` |
| **DPI 缩放支持** | `XamlRoot.RasterizationScale` |
| **完整旋转/翻转** | 支持 Rotate/Flip 操作 |
| **游标切换** | Hand 手型游标 |
| **更精确的物理** | 边界回弹、惯性处理更流畅 |

---

## 移植方案

### 方案概述

不创建新控件，而是把 PhysicsImageViewer 的逻辑**完整移植到现有的 ImageViewerControl 中**。

### 需要保留的当前代码

**鼠标坐标计算方式**：
```csharp
// 保留当前的坐标计算方式
var containerCenterX = ImageContainer.ActualWidth / 2;
var containerCenterY = ImageContainer.ActualHeight / 2;
var mouseRelativeToCenterX = pointer.Position.X - containerCenterX;
var mouseRelativeToCenterY = pointer.Position.Y - containerCenterY;
_zoomAnchorImgX = (mouseRelativeToCenterX - _translateX) / _zoomScale;
_zoomAnchorImgY = (mouseRelativeToCenterY - _translateY) / _zoomScale;
```

### 需要替换的代码

用参考项目的以下逻辑替换：
1. `OnPhysicsRendering` - 完整的物理引擎
2. `OnPointerWheelChanged` - 完整的 100% 吸附
3. `OnPointerPressed/Moved/Released` - 完整的拖动处理
4. `ApplyBoundsWithDamping` - 完整的边界处理
5. `GetScaledImageSize` - 使用 ActualWidth/ActualHeight
6. 添加 100% 吸附相关变量
7. 添加 DPI 缩放支持
8. 添加游标切换

---

## 实现步骤

### 步骤 1：更新状态变量

**添加 100% 吸附变量**：
```csharp
private bool _justSnappedTo100Percent = false;
private int _snapStayCounter = 0;
private const double SnapThreshold = 0.05;
private const int SnapStayCount = 4;
```

### 步骤 2：更新 GetScaledImageSize

**使用 Image.ActualWidth**：
```csharp
private (double width, double height) GetScaledImageSize()
{
    if (MainImage == null) return (0, 0);
    return (MainImage.ActualWidth * _zoomScale, MainImage.ActualHeight * _zoomScale);
}
```

### 步骤 3：完整替换 OnPhysicsRendering

**使用参考项目的物理引擎**。

### 步骤 4：完整替换 OnPointerWheelChanged

**使用参考项目的 100% 吸附逻辑**，但保留当前的坐标计算方式。

### 步骤 5：完整替换 OnPointerPressed/Moved/Released

**使用参考项目的拖动处理**。

### 步骤 6：完整替换 ApplyBoundsWithDamping

**使用参考项目的边界处理**。

### 步骤 7：添加辅助方法

- `UpdateCursor()` - 游标切换
- `CanPan()` / `CanPanHorizontal()` / `CanPanVertical()` - 拖动判断
- 添加 DPI 缩放相关计算

---

## 完整的代码对比

### 当前项目 vs 参考项目

| 模块 | 当前项目 | 参考项目 | 操作 |
|------|---------|---------|------|
| 状态变量 | 基础 | 完整（含吸附） | 添加吸附变量 |
| GetScaledImageSize | 原图尺寸 | ActualWidth | 替换 |
| OnPhysicsRendering | 简化版 | 完整版 | 替换 |
| OnPointerWheelChanged | 无吸附 | 有吸附 | 替换（保留坐标计算） |
| OnPointerPressed/Moved/Released | 简化版 | 完整版 | 替换 |
| ApplyBoundsWithDamping | 部分实现 | 完整实现 | 替换 |
| ClampTranslation | 有 | 有 | 保留 |
| 游标切换 | 无 | 有 | 添加 |
| DPI 缩放 | 无 | 有 | 添加 |

---

## 文件修改清单

- **ImageViewerControl.xaml.cs** - 主要逻辑文件
- **ImageViewerControl.xaml** - 可能需要微调（保持不变）

---

## 风险评估

| 风险 | 说明 | 缓解措施 |
|------|------|---------|
| 代码量大 | 移植代码较多 | 分步骤测试 |
| 兼容性 | 需保持与现有 API 兼容 | 保留公共接口 |
| 测试 | 需要充分测试 | 逐项测试功能 |

---

## 测试清单

移植完成后需要测试：
- [ ] 缩放缓动流畅
- [ ] 鼠标中心缩放准确
- [ ] 100% 吸附正常
- [ ] 拖动惯性自然
- [ ] 边界阻尼合适
- [ ] 边界回弹流畅
- [ ] 缩放缓动期间可以拖动
- [ ] 拖动时边界限制正确

