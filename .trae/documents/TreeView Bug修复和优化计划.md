# TreeView Bug 修复和优化计划

## 问题分析

### Bug 1：本地驱动器显示成 U 盘图标
**原因**：`NodeTypeToIconConverter` 中 `NodeType.Drive` 和 `NodeType.ExternalDevice` 使用相同图标 `\xE88E`

**当前代码**：
```csharp
NodeType.Drive => "\xE88E",      // U 盘图标
NodeType.ExternalDevice => "\xE88E",  // U 盘图标
```

**问题**：没有区分本地驱动器和可移动驱动器

### 优化点 1：预加载子节点信息
**当前问题**：每个节点都添加 dummy child，用户无法知道目录下是否真的有子节点

**当前代码**：
```csharp
childNode.Children.Add(new FolderNode(null, NodeType.Folder, childNode) { Name = "" });
childNode.HasDummyChild = true;
```

### 优化点 2：双击展开
**当前问题**：只在单击已选中节点时切换展开，双击没有处理

---

## 实现步骤

### [ ] 任务 1：修复驱动器图标 Bug
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 在 `FolderNode` 模型中添加 `IsRemovable` 属性
  - 在 `LoadChildrenAsync` 创建驱动器节点时设置 `IsRemovable`
  - 修改 `NodeTypeToIconConverter`，根据 `IsRemovable` 返回不同图标
- **Success Criteria**:
  - 本地驱动器显示硬盘图标
  - 可移动驱动器显示 U 盘图标
- **Test Requirements**:
  - `human-judgement` TR-1.1: 本地 C: 盘显示硬盘图标
  - `human-judgement` TR-1.2: U 盘显示 U 盘图标

### [ ] 任务 2：预加载子节点信息（可选实现）
- **Priority**: P2
- **Depends On**: 任务 1
- **Description**:
  - 方案 A：在后台线程预加载一级子节点信息
  - 方案 B：使用 `StorageFolder.TryGetItemAsync` 检查是否有子文件夹
  - 方案 C：保持当前 dummy child 方式，但添加 `HasChildren` 属性
- **注意**：预加载可能影响性能，需要权衡
- **Success Criteria**:
  - 用户能直观看到节点是否有子节点
- **Test Requirements**:
  - `human-judgement` TR-2.1: 空文件夹不显示展开三角

### [ ] 任务 3：双击展开已选中节点
- **Priority**: P1
- **Depends On**: None
- **Description**:
  - 在 `FolderTreeView_ItemInvoked` 中记录点击时间
  - 如果两次点击间隔小于 300ms，识别为双击
  - 双击时切换展开状态
- **Success Criteria**:
  - 快速双击已选中节点能展开/关闭
- **Test Requirements**:
  - `human-judgement` TR-3.1: 双击已选中节点能切换展开状态

---

## 涉及文件

| 文件 | 修改类型 |
|------|---------|
| `Models/FolderNode.cs` | 添加 `IsRemovable` 属性 |
| `ViewModels/MainViewModel.cs` | 设置 `IsRemovable` 值 |
| `Converters/NodeTypeToIconConverter.cs` | 修改图标逻辑 |
| `Views/MainPage.xaml.cs` | 添加双击检测 |

---

## 技术细节

### 1. FolderNode 添加 IsRemovable 属性
```csharp
public bool IsRemovable { get; set; }
```

### 2. MainViewModel 设置 IsRemovable
```csharp
var driveNode = new FolderNode(storageFolder, NodeType.Drive, node)
{
    Name = $"{drive.Name} ({drive.VolumeLabel})",
    IsRemovable = drive.DriveType == DriveType.Removable
};
```

### 3. NodeTypeToIconConverter 修改
```csharp
// 需要修改转换器，接收 FolderNode 对象而非 NodeType
// 或者添加多值绑定
```

**更好的方案**：修改 XAML 绑定，直接绑定到 FolderNode
```xml
<FontIcon Glyph="{x:Bind Converter={StaticResource FolderNodeToIconConverter}}"/>
```

### 4. 双击检测
```csharp
private DateTime _lastClickTime;
private FolderNode? _lastClickedNode;

private async void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
{
    if (args.InvokedItem is FolderNode node)
    {
        var now = DateTime.Now;
        var isDoubleClick = (now - _lastClickTime).TotalMilliseconds < 300 
                           && _lastClickedNode == node;
        _lastClickTime = now;
        _lastClickedNode = node;

        if (FolderTreeView.SelectedItem == node || isDoubleClick)
        {
            var container = FolderTreeView.ContainerFromItem(node);
            if (container is TreeViewItem item)
            {
                item.IsExpanded = !item.IsExpanded;
            }
        }

        await ViewModel.LoadImagesAsync(node);
    }
}
```

---

## 图标参考（Segoe Fluent Icons）

| 类型 | Glyph | 说明 |
|------|-------|------|
| 本地硬盘 | \xE8DA | 硬盘驱动器 |
| U 盘/可移动 | \xE88E | USB 驱动器 |
| 文件夹 | \xE8B7 | 文件夹 |
| 此电脑 | \xE774 | 电脑 |
