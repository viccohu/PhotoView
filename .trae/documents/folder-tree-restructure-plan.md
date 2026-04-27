# 路径结构调整计划

## 目标
1. 把"常用文件夹"(FavoritesRoot)和"外接设备"(ExternalDevice)放到"这台电脑"(ThisPC)下面作为子节点
2. MainPage 的初始地址设为"这台电脑-常用文件夹"

## 当前结构
```
FolderTree:
├── 常用文件夹 (FavoritesRoot)
├── 这台电脑 (ThisPC)
│   ├── 桌面
│   ├── 图片
│   ├── 下载
│   └── 驱动器...
└── 外接设备 (ExternalDevice)
    └── 可移动驱动器...
```

## 目标结构
```
FolderTree:
└── 这台电脑 (ThisPC)
    ├── 常用文件夹 (FavoritesRoot)
    │   ├── 固定文件夹
    │   └── 最近文件夹
    ├── 外接设备 (ExternalDevice)
    │   └── 可移动驱动器...
    ├── 桌面
    ├── 图片
    ├── 下载
    └── 驱动器...
```

## 实现步骤

### 步骤 1: 修改 FolderTreeService.cs

**1.1 修改 `CreateRootNodesAsync()` 方法**
- 改为只返回 ThisPC 节点
- 在创建 ThisPC 节点时，预先将 FavoritesRoot 和 ExternalDevice 作为子节点添加
- 返回类型改为 `Task<FolderNode>` 或保持元组但改变语义

**1.2 修改 `LoadChildrenAsync()` 方法**
- 修改 `NodeType.ThisPC` 的处理逻辑
- 在 `PopulateThisPcChildrenAsync()` 中，先添加 FavoritesRoot 和 ExternalDevice，再添加其他已知文件夹和驱动器

**1.3 新增辅助方法**
- 添加 `GetFavoritesRootFromThisPc()` 方法
- 添加 `GetExternalDeviceFromThisPc()` 方法

### 步骤 2: 修改 MainViewModel.cs

**2.1 修改字段和属性**
- `_favoritesRootNode` 和 `_externalDeviceNode` 改为从 ThisPC 子节点获取

**2.2 修改 `LoadDrivesAsync()` 方法**
- 只添加 ThisPC 节点到 FolderTree
- 从 ThisPC 子节点中获取 FavoritesRoot 和 ExternalDevice 引用

**2.3 修改相关方法**
- `RefreshFavoriteFoldersAsync()` - 从 ThisPC 子节点获取 FavoritesRoot
- `PinFolderAsync()` / `UnpinFolderAsync()` - 同上
- `RefreshExternalDevicesAsync()` - 从 ThisPC 子节点获取 ExternalDevice

### 步骤 3: 修改 CollectViewModel.cs

**3.1 修改 `LoadDrivesAsync()` 方法**
- 只添加 ThisPC 节点到 FolderTree

**3.2 修改 `GetFavoritesRootNode()` 方法**
- 从 ThisPC 子节点中查找 FavoritesRoot

### 步骤 4: 修改 MainPage.xaml.cs

**4.1 修改 `TryRestoreLastFolderAsync()` 方法**
- 如果 `RememberLastFolder` 为 false 或 `LastFolderPath` 为空，默认导航到 ThisPC 下的 FavoritesRoot 节点

**4.2 修改 `TryFindAndLoadNodeByPathAsync()` 方法**
- 适配新的树结构，从 ThisPC 根节点开始搜索

### 步骤 5: 检查并修改 CollectPage.xaml.cs

**5.1 检查节点查找逻辑**
- 确保 `IsNodeUnderExternalDevices()` 等方法正常工作

## 文件修改清单

| 文件 | 修改内容 |
|------|----------|
| `Services/FolderTreeService.cs` | 重构节点创建和加载逻辑 |
| `ViewModels/MainViewModel.cs` | 修改节点引用获取方式 |
| `ViewModels/CollectViewModel.cs` | 修改节点引用获取方式 |
| `Views/MainPage.xaml.cs` | 添加默认导航到 FavoritesRoot |

## 注意事项

1. **向后兼容**: 需要确保现有的路径恢复功能正常工作
2. **节点展开状态**: ThisPC 节点应该默认展开，以便用户能看到常用文件夹
3. **图标显示**: 确保节点图标正确显示
4. **右键菜单**: 确保右键菜单功能正常（刷新外接设备等）
