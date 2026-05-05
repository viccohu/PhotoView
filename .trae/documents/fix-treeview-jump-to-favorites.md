# 修复：打开新路径文件夹时 TreeView 弹到「常用文件夹」

## 问题描述

在 MainPage 中打开新的路径文件夹时，TreeView 会自动跳转到「常用文件夹」位置，而不是在「这台电脑」下展开对应路径。

## 根因分析

### 根因 1（主要原因）：`FindNodeByPath` 优先搜索 FavoritesRoot

[MainViewModel.cs:534-546](file:///d:/PhotoView/PhotoView/ViewModels/MainViewModel.cs#L534-L546) 中 `FindNodeByPath` 按 `FolderTree` 顺序遍历：

```csharp
public FolderNode? FindNodeByPath(string path)
{
    foreach (var rootNode in FolderTree)  // 顺序：FavoritesRoot → ThisPC → ExternalDevice
    {
        var found = FindNodeByPathRecursive(rootNode, path);
        if (found != null)
            return found;
    }
    return null;
}
```

`FolderTree` 的顺序是：
1. `FavoritesRoot`（常用文件夹）— 默认 `IsExpanded = true`
2. `ThisPC`（这台电脑）
3. `ExternalDevice`（外接设备）

当目标路径同时存在于「常用文件夹」（作为固定/最近文件夹）和「这台电脑」下时，`FindNodeByPath` 会先在 FavoritesRoot 下找到节点。随后 `ExpandTreeViewPathAsync` 沿 Parent 链回溯时，路径经过 FavoritesRoot 而非 ThisPC，导致 TreeView 导航到「常用文件夹」。

**影响场景**：`TryFindAndLoadNodeByPathAsync` → `TryRestoreLastFolderAsync`（启动恢复上次文件夹）

### 根因 2（次要原因）：`RecordFolderVisitAsync` 在导航过程中修改 FavoritesRoot 子节点

[MainViewModel.cs:386-410](file:///d:/PhotoView/PhotoView/ViewModels/MainViewModel.cs#L386-L410) 中 `LoadImagesAsync` 的执行顺序：

1. `SelectedFolder = folderNode` → 触发 `SelectedFolderChanged` → `ExpandTreeViewPathAsync` 开始异步执行
2. `await RecordFolderVisitAsync(folderNode)` → 调用 `RefreshFavoriteFoldersAsync` → `ApplyFavoriteFolderChildrenAsync`

[FolderTreeService.cs:356-366](file:///d:/PhotoView/PhotoView/Services/FolderTreeService.cs#L356-L366) 中 `ApplyFavoriteFolderChildrenAsync` 的实现：

```csharp
favoritesRoot.Children.Clear();       // 清空所有子节点
foreach (var node in desiredNodes)
{
    favoritesRoot.Children.Add(node); // 重新添加
}
```

这种 Clear + Add 的方式会导致 TreeView 重新渲染整个「常用文件夹」区域。如果此时 `ExpandTreeViewPathAsync` 正在导航到 ThisPC 下的节点，FavoritesRoot 子节点的变化可能触发 TreeView 的滚动位置重置或视觉跳动。

### 根因 3（交互原因）：点击 FavoritesRoot 子节点后导航始终经过「常用文件夹」

当用户点击「常用文件夹」下的固定/最近文件夹时，`FolderTreeView_ItemInvoked` 或 `FolderTreeView_Expanding` 被调用，后续导航的 Parent 链经过 FavoritesRoot。之后用户双击子文件夹进入更深目录时，Parent 链仍然经过 FavoritesRoot，TreeView 始终停留在「常用文件夹」区域。

## 修复方案

### 修复 1：`FindNodeByPath` 跳过 FavoritesRoot

在 `FindNodeByPath` 中跳过 FavoritesRoot 的递归搜索，因为 FavoritesRoot 下的节点只是快捷方式/别名，真实路径一定在 ThisPC 或 ExternalDevice 下存在。

**修改文件**：[MainViewModel.cs](file:///d:/PhotoView/PhotoView/ViewModels/MainViewModel.cs)

```csharp
public FolderNode? FindNodeByPath(string path)
{
    if (string.IsNullOrEmpty(path))
        return null;

    foreach (var rootNode in FolderTree)
    {
        // 跳过 FavoritesRoot，其子节点是快捷方式，不应作为导航目标
        if (rootNode.NodeType == NodeType.FavoritesRoot)
            continue;

        var found = FindNodeByPathRecursive(rootNode, path);
        if (found != null)
            return found;
    }
    return null;
}
```

### 修复 2：`ApplyFavoriteFolderChildrenAsync` 使用差异更新替代 Clear+Add

将 `favoritesRoot.Children.Clear()` + 逐个 Add 的方式改为差异更新：只添加新增节点、只移除不再需要的节点、更新已存在节点的属性。这样可以避免 TreeView 的大规模重渲染。

**修改文件**：[FolderTreeService.cs](file:///d:/PhotoView/PhotoView/Services/FolderTreeService.cs)

```csharp
private static async Task ApplyFavoriteFolderChildrenAsync(
    FolderNode favoritesRoot,
    IReadOnlyList<(string Path, NodeType NodeType)> desiredItems)
{
    var desiredNodes = new List<FolderNode>();
    foreach (var desiredItem in desiredItems)
    {
        if (!Directory.Exists(desiredItem.Path))
            continue;

        var existingNode = favoritesRoot.Children
            .FirstOrDefault(child => IsSamePath(child.FullPath, desiredItem.Path));

        if (existingNode == null)
        {
            var storageFolder = await StorageFolder.GetFolderFromPathAsync(desiredItem.Path);
            existingNode = new FolderNode(storageFolder, desiredItem.NodeType, favoritesRoot);
            existingNode.CheckHasSubFolders();
        }

        existingNode.Parent = favoritesRoot;
        existingNode.NodeType = desiredItem.NodeType;
        existingNode.Name = GetDisplayNameForPath(desiredItem.Path);
        desiredNodes.Add(existingNode);
    }

    // 差异更新：只移除不再需要的节点
    for (int i = favoritesRoot.Children.Count - 1; i >= 0; i--)
    {
        if (!desiredNodes.Any(dn => IsSamePath(dn.FullPath, favoritesRoot.Children[i].FullPath)))
        {
            favoritesRoot.Children.RemoveAt(i);
        }
    }

    // 差异更新：按目标顺序插入/移动节点
    for (int i = 0; i < desiredNodes.Count; i++)
    {
        var desired = desiredNodes[i];
        var currentIndex = -1;
        for (int j = i; j < favoritesRoot.Children.Count; j++)
        {
            if (IsSamePath(favoritesRoot.Children[j].FullPath, desired.FullPath))
            {
                currentIndex = j;
                break;
            }
        }

        if (currentIndex == i)
            continue;

        if (currentIndex >= 0)
        {
            favoritesRoot.Children.RemoveAt(currentIndex);
        }
        favoritesRoot.Children.Insert(i, desired);
    }
}
```

### 修复 3：点击 FavoritesRoot 子节点时重定向到 ThisPC 对应节点

当用户点击「常用文件夹」下的节点时，在 `FolderTreeView_ItemInvoked` 和 `FolderTreeView_Expanding` 中，查找 ThisPC 下对应的节点并使用该节点进行导航，而不是使用 FavoritesRoot 下的节点。

**修改文件**：[MainPage.xaml.cs](file:///d:/PhotoView/PhotoView/Views/MainPage.xaml.cs)

在 `FolderTreeView_ItemInvoked` 和 `FolderTreeView_Expanding` 中，如果点击的节点在 FavoritesRoot 下，则查找 ThisPC 下的对应节点：

```csharp
private FolderNode? FindThisPcNodeForFavoritesNode(FolderNode favoritesNode)
{
    if (string.IsNullOrEmpty(favoritesNode.FullPath))
        return null;
    return ViewModel.FindNodeByPath(favoritesNode.FullPath);
}
```

然后在 `FolderTreeView_Expanding` 中：

```csharp
private async void FolderTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
{
    if (args.Item is FolderNode node)
    {
        // 如果是常用文件夹下的节点，重定向到这台电脑下的对应节点
        var navigationNode = ViewModel.IsNodeUnderFavoritesRoot(node)
            ? (ViewModel.FindNodeByPath(node.FullPath) ?? node)
            : node;

        FolderTreeView.SelectedItem = navigationNode;
        await ViewModel.LoadChildrenAsync(navigationNode);
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;
        ThrottleLoadImages(navigationNode);
    }
}
```

类似地修改 `FolderTreeView_ItemInvoked`。

**注意**：此修复需要将 `IsNodeUnderFavoritesRoot` 方法从 `private` 改为 `internal` 或通过 ViewModel 暴露。

## 修改文件清单

| 文件 | 修改内容 |
|------|---------|
| `ViewModels/MainViewModel.cs` | `FindNodeByPath` 跳过 FavoritesRoot；`IsNodeUnderFavoritesRoot` 可访问性调整 |
| `Services/FolderTreeService.cs` | `ApplyFavoriteFolderChildrenAsync` 改为差异更新 |
| `Views/MainPage.xaml.cs` | `FolderTreeView_Expanding` 和 `FolderTreeView_ItemInvoked` 中重定向 FavoritesRoot 节点到 ThisPC |
