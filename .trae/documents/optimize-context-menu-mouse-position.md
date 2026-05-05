# 优化全局右键菜单为跟随鼠标位置展开

## 问题描述

当前所有右键菜单使用 `FlyoutBase.ShowAttachedFlyout(element)` + `Placement="Bottom"` 的方式显示，菜单始终定位在目标元素的底部，而非鼠标右键点击的位置。这不符合用户对右键菜单的直觉预期——右键菜单应该出现在鼠标光标所在位置。

## 当前实现分析

代码库中共有 **4 个右键上下文菜单**（排除 DropDownButton 的非右键菜单），全部使用相同模式：

| # | 位置 | XAML Placement | 事件处理器 | 显示方式 |
|---|------|---------------|-----------|---------|
| 1 | MainPage 文件夹树 | `Placement="Bottom"` | `TreeViewItem_RightTapped` | `FlyoutBase.ShowAttachedFlyout(grid)` |
| 2 | MainPage 子文件夹 | `Placement="Bottom"` | `SubFolderItem_RightTapped` | `FlyoutBase.ShowAttachedFlyout(element)` |
| 3 | MainPage 图片缩略图 | `Placement="Bottom"` | `ImageItem_RightTapped` | `FlyoutBase.ShowAttachedFlyout(element)` |
| 4 | CollectPage 文件夹树 | `Placement="Bottom"` | `PreviewFolderTreeItem_RightTapped` | `FlyoutBase.ShowAttachedFlyout(grid)` |

## 修改方案

### 核心思路

将 `FlyoutBase.ShowAttachedFlyout(element)` 替换为 `MenuFlyout.ShowAt(targetElement, flyoutShowOptions)`，其中 `FlyoutShowOptions.Position` 设为鼠标右键点击位置（从 `RightTappedRoutedEventArgs.GetPosition` 获取）。

WinUI 3 API：
```csharp
var flyout = FlyoutBase.GetAttachedFlyout(element) as MenuFlyout;
var position = e.GetPosition(element);  // 相对于 element 的坐标
flyout.ShowAt(element, new FlyoutShowOptions { Position = position });
```

同时将 XAML 中的 `Placement="Bottom"` 改为 `Placement="Auto"`，让系统自动选择最佳展开方向（避免菜单超出屏幕边界）。

### 修改 1：MainPage.xaml — 3 个 MenuFlyout 的 Placement

**文件**：[MainPage.xaml](file:///d:/PhotoView/PhotoView/Views/MainPage.xaml)

将 3 处 `Placement="Bottom"` 改为 `Placement="Auto"`：

1. **文件夹树菜单**（约第 245 行）：
   ```xml
   <MenuFlyout Placement="Auto" Opening="FolderTreeMenuFlyout_Opening">
   ```

2. **子文件夹菜单**（约第 728 行）：
   ```xml
   <MenuFlyout Placement="Auto" Opening="SubFolderMenuFlyout_Opening">
   ```

3. **图片缩略图菜单**（约第 835 行）：
   ```xml
   <MenuFlyout Placement="Auto">
   ```

### 修改 2：MainPage.xaml.cs — 3 个 RightTapped 事件处理器

**文件**：[MainPage.xaml.cs](file:///d:/PhotoView/PhotoView/Views/MainPage.xaml.cs)

1. **TreeViewItem_RightTapped**（约第 1322 行）：

```csharp
private void TreeViewItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
{
    if (sender is not TreeViewItem treeViewItem)
        return;

    if (treeViewItem.Content is Grid grid && grid.Tag is FolderNode node)
    {
        _rightClickedFolderNode = node;
        var flyout = FlyoutBase.GetAttachedFlyout(grid) as MenuFlyout;
        if (flyout != null)
        {
            var position = e.GetPosition(grid);
            flyout.ShowAt(grid, new FlyoutShowOptions { Position = position });
        }
    }
    e.Handled = true;
}
```

2. **SubFolderItem_RightTapped**（约第 1335 行）：

```csharp
private void SubFolderItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
{
    if (sender is not FrameworkElement element)
        return;

    if (element.Tag is FolderNode node)
    {
        _rightClickedSubFolderNode = node;
        SubFolderGridView.SelectedItem = node;
        var flyout = FlyoutBase.GetAttachedFlyout(element) as MenuFlyout;
        if (flyout != null)
        {
            var position = e.GetPosition(element);
            flyout.ShowAt(element, new FlyoutShowOptions { Position = position });
        }
        e.Handled = true;
    }
}
```

3. **ImageItem_RightTapped**（约第 1304 行）：

```csharp
private void ImageItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
{
    if (sender is not FrameworkElement element)
        return;

    if (element.Tag is ImageFileInfo imageInfo)
    {
        _rightClickedImageInfo = imageInfo;
        if (!ImageGridView.SelectedItems.Contains(imageInfo))
        {
            ExecuteProgrammaticSelectionChange(() => ImageGridView.SelectedItem = imageInfo);
        }
    }

    var flyout = FlyoutBase.GetAttachedFlyout(element) as MenuFlyout;
    if (flyout != null)
    {
        var position = e.GetPosition(element);
        flyout.ShowAt(element, new FlyoutShowOptions { Position = position });
    }
    e.Handled = true;
}
```

### 修改 3：CollectPage.xaml — 1 个 MenuFlyout 的 Placement

**文件**：[CollectPage.xaml](file:///d:/PhotoView/PhotoView/Views/CollectPage.xaml)

将文件夹树菜单的 `Placement="Bottom"` 改为 `Placement="Auto"`（约第 145 行）：

```xml
<MenuFlyout Placement="Auto" Opening="FolderTreeMenuFlyout_Opening">
```

### 修改 4：CollectPage.xaml.cs — 1 个 RightTapped 事件处理器

**文件**：[CollectPage.xaml.cs](file:///d:/PhotoView/PhotoView/Views/CollectPage.xaml.cs)

**PreviewFolderTreeItem_RightTapped**（约第 269 行）：

```csharp
private void PreviewFolderTreeItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
{
    if (TryGetFolderNode(sender, out var node))
    {
        _rightClickedFolderNode = node;
        if (sender is TreeViewItem { Content: Grid grid })
        {
            var flyout = FlyoutBase.GetAttachedFlyout(grid) as MenuFlyout;
            if (flyout != null)
            {
                var position = e.GetPosition(grid);
                flyout.ShowAt(grid, new FlyoutShowOptions { Position = position });
            }
        }
        e.Handled = true;
    }
}
```

## 修改文件清单

| 文件 | 修改内容 |
|------|---------|
| `Views/MainPage.xaml` | 3 处 `Placement="Bottom"` → `Placement="Auto"` |
| `Views/MainPage.xaml.cs` | 3 个 RightTapped 处理器：`ShowAttachedFlyout` → `ShowAt` + 鼠标位置 |
| `Views/CollectPage.xaml` | 1 处 `Placement="Bottom"` → `Placement="Auto"` |
| `Views/CollectPage.xaml.cs` | 1 个 RightTapped 处理器：`ShowAttachedFlyout` → `ShowAt` + 鼠标位置 |
