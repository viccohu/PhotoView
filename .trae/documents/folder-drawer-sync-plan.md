# 文件夹抽屉联动计划（优化版）

## 目标
1. 当选中"这台电脑"节点时，右边的文件夹抽屉显示"常用文件夹"和"外接设备"
2. 外接设备如果没有子项，文件夹抽屉中不显示
3. 当外接设备响应到有变化时，文件夹抽屉也要通知更新

## 当前问题

### 问题 1: 子文件夹过滤
`SyncCurrentSubFoldersAsync` 方法中有过滤条件：
```csharp
var subFolders = folderNode.Children
    .Where(child => child.Folder != null)
    .ToList();
```
这会过滤掉 FavoritesRoot 和 ExternalDevice 节点。

### 问题 2: ExternalDevice 动态显示
ExternalDevice 节点需要根据是否有实际的外接设备来决定是否显示。

### 问题 3: 外接设备变化时的通知
`OnExternalDevicesChanged` 只刷新 ExternalDevice 节点本身，但如果当前选中的是 ThisPC，文件夹抽屉也需要更新。

## 实现步骤

### 步骤 1: 修改 MainViewModel.cs 的 `SyncCurrentSubFoldersAsync` 方法

修改过滤逻辑：
- FavoritesRoot 始终显示
- ExternalDevice 只有在有子项时才显示

```csharp
var subFolders = folderNode.Children
    .Where(child => child.Folder != null || 
           child.NodeType == NodeType.FavoritesRoot || 
           (child.NodeType == NodeType.ExternalDevice && child.HasSubFolders))
    .ToList();
```

### 步骤 2: 修改 MainViewModel.cs 的 `OnExternalDevicesChanged` 方法

在外接设备变化时，如果当前选中的是 ThisPC，需要刷新文件夹抽屉：

```csharp
private void OnExternalDevicesChanged(object? sender, EventArgs e)
{
    var dispatcher = App.MainWindow.DispatcherQueue;
    if (dispatcher == null)
        return;

    dispatcher.TryEnqueue(async () =>
    {
        if (AppLifetime.IsShuttingDown || _externalDeviceNode == null)
            return;

        // 刷新外接设备节点
        await _folderTreeService.RefreshExternalDevicesAsync(_externalDeviceNode);

        // 如果当前选中的是 ThisPC，需要刷新文件夹抽屉
        if (SelectedFolder?.NodeType == NodeType.ThisPC)
        {
            await SyncCurrentSubFoldersAsync(SelectedFolder, CancellationToken.None);
        }
        else if (!_externalDeviceNode.IsLoaded && !ReferenceEquals(SelectedFolder, _externalDeviceNode))
        {
            // 更新展开状态
            _externalDeviceNode.HasSubFolders = _externalDeviceNode.Children.Count > 0;
            _externalDeviceNode.RefreshExpandableState();
        }
        else if (ReferenceEquals(SelectedFolder, _externalDeviceNode))
        {
            // 如果当前选中的是外接设备节点，刷新其子文件夹
            await SyncCurrentSubFoldersAsync(_externalDeviceNode, CancellationToken.None);
        }
    });
}
```

### 步骤 3: 修改 MainPage.xaml.cs 的 `SubFolderGridView_DoubleTapped` 方法

允许 FavoritesRoot 和 ExternalDevice 节点双击导航：

```csharp
if (TryGetFolderNodeFromElement(e.OriginalSource, out var folderNode) && 
    (folderNode.Folder != null || 
     folderNode.NodeType == NodeType.FavoritesRoot || 
     folderNode.NodeType == NodeType.ExternalDevice))
```

### 步骤 4: 确保 ExternalDevice 节点的 HasSubFolders 正确

在 `FolderTreeService.RefreshExternalDevicesAsync` 中，刷新后需要更新 HasSubFolders：

```csharp
public async Task RefreshExternalDevicesAsync(FolderNode externalDevicesRoot)
{
    externalDevicesRoot.IsLoaded = false;
    externalDevicesRoot.Children.Clear();
    await LoadChildrenAsync(externalDevicesRoot);
    // HasSubFolders 会在 LoadChildrenAsync 中自动更新
}
```

## 文件修改清单

| 文件 | 修改内容 |
|------|----------|
| `ViewModels/MainViewModel.cs` | 修改 `SyncCurrentSubFoldersAsync` 过滤逻辑 |
| `ViewModels/MainViewModel.cs` | 修改 `OnExternalDevicesChanged` 通知逻辑 |
| `Views/MainPage.xaml.cs` | 修改 `SubFolderGridView_DoubleTapped` 导航逻辑 |

## 已确认正常的部分

1. ✅ `NodeTypeToIconConverter` 已正确处理 FavoritesRoot 和 ExternalDevice 的图标
2. ✅ `LoadChildrenAsync` 会正确更新 `HasSubFolders` 属性
3. ✅ 右键菜单功能已支持 ExternalDevice 节点

## 注意事项

1. ExternalDevice 节点的 `HasSubFolders` 属性由 `LoadChildrenAsync` 自动更新
2. 需要在外接设备变化时正确刷新文件夹抽屉
3. 双击 ExternalDevice 节点时应该展开并显示其子项
