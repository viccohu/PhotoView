# MainViewModel 性能优化方案

## 问题分析

当前 `MainViewModel.cs` 存在以下性能问题：

| 优先级 | 问题 | 影响 |
|--------|------|------|
| 🔥 高 | 图片同步逐个加载，阻塞 UI 线程 | 卡顿、滚动掉帧、可能崩溃 |
| 🔥 高 | 一次性加载所有图片，无分页 | 大文件夹内存爆炸 |
| 🔥 高 | `Images.Add()` 逐个添加，触发大量 UI 刷新 | ItemsRepeater 频繁重排 |
| ⚠️ 中 | `FindParentNode` 递归查找 O(n²) | 面包屑更新卡顿 |
| ⚠️ 中 | `IsImageFile` 每次创建新数组 | 轻微性能损耗 |
| 💡 低 | 异常被吞掉，无日志输出 | 调试困难 |

---

## 优化步骤

### 步骤 1：优化图片加载（核心优化）

**文件**: `d:\PhotoView\PhotoView\ViewModels\MainViewModel.cs`

**修改内容**:

1. 添加分页加载常量和取消令牌支持
2. 重写 `LoadImagesAsync` 方法，实现：
   - 后台线程处理文件信息
   - 分页加载（每页 100 张）
   - 批量添加到 UI（每批 20 张）

```csharp
// 新增字段
private CancellationTokenSource? _loadImagesCts;
private const uint PageSize = 100;
private const int BatchSize = 20;

// 重写 LoadImagesAsync
public async Task LoadImagesAsync(FolderNode folderNode)
{
    // 取消之前的加载任务
    _loadImagesCts?.Cancel();
    _loadImagesCts = new CancellationTokenSource();
    var cancellationToken = _loadImagesCts.Token;

    Images.Clear();
    SelectedFolder = folderNode;
    UpdateBreadcrumbPath(folderNode);
    ImagesChanged?.Invoke(this, EventArgs.Empty);

    if (folderNode?.Folder == null)
        return;

    try
    {
        var result = folderNode.Folder.CreateFileQueryWithOptions(new QueryOptions());
        uint index = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await result.GetFilesAsync(index, PageSize);
            if (batch.Count == 0)
                break;

            // 后台线程处理
            var imageInfos = await Task.Run(async () =>
            {
                var list = new List<ImageFileInfo>();
                foreach (var file in batch)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    if (IsImageFile(file))
                    {
                        try
                        {
                            var info = await LoadImageInfo(file);
                            list.Add(info);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"LoadImageInfo error: {ex}");
                        }
                    }
                }
                return list;
            }, cancellationToken);

            // UI 线程批量添加
            if (!cancellationToken.IsCancellationRequested)
            {
                foreach (var info in imageInfos)
                {
                    Images.Add(info);
                }
            }

            index += PageSize;
        }
    }
    catch (OperationCanceledException)
    {
        // 正常取消，忽略
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"LoadImagesAsync error: {ex}");
    }
}
```

---

### 步骤 2：优化 FolderNode，添加 Parent 属性

**文件**: `d:\PhotoView\PhotoView\Models\FolderNode.cs`

**修改内容**:

1. 添加 `Parent` 属性
2. 修改构造函数，支持传入父节点

```csharp
// 新增属性
public FolderNode? Parent { get; set; }

// 修改构造函数
public FolderNode(StorageFolder? folder = null, NodeType nodeType = NodeType.Folder, FolderNode? parent = null)
{
    Name = folder?.DisplayName ?? "This PC";
    Folder = folder;
    NodeType = nodeType;
    Parent = parent;
    Children = new ObservableCollection<FolderNode>();
    AllChildren = new ObservableCollection<FolderNode>();
    HasDummyChild = true;
}
```

---

### 步骤 3：优化 MainViewModel 中创建子节点时设置 Parent

**文件**: `d:\PhotoView\PhotoView\ViewModels\MainViewModel.cs`

**修改内容**:

在 `LoadChildrenAsync` 方法中，创建子节点时设置 Parent：

```csharp
// 创建 driveNode 时
var driveNode = new FolderNode(storageFolder, NodeType.Drive, node);

// 创建 childNode 时
var childNode = new FolderNode(folder, NodeType.Folder, node);
```

---

### 步骤 4：优化 UpdateBreadcrumbPath 方法

**文件**: `d:\PhotoView\PhotoView\ViewModels\MainViewModel.cs`

**修改内容**:

利用 Parent 属性，避免递归查找：

```csharp
private void UpdateBreadcrumbPath(FolderNode? folderNode)
{
    BreadcrumbPath.Clear();
    if (folderNode == null)
        return;

    var path = new List<FolderNode>();
    var current = folderNode;
    
    // 使用 Parent 属性直接向上遍历
    while (current != null)
    {
        path.Insert(0, current);
        current = current.Parent;
    }
    
    foreach (var node in path)
    {
        BreadcrumbPath.Add(node);
    }
}
```

---

### 步骤 5：优化 IsImageFile 方法

**文件**: `d:\PhotoView\PhotoView\ViewModels\MainViewModel.cs`

**修改内容**:

使用静态 HashSet 替代每次创建数组：

```csharp
// 新增静态字段
private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"
};

// 修改方法
private static bool IsImageFile(StorageFile file)
{
    return ImageExtensions.Contains(file.FileType);
}
```

---

### 步骤 6：优化异常处理

**文件**: `d:\PhotoView\PhotoView\ViewModels\MainViewModel.cs`

**修改内容**:

将所有空的 `catch (Exception) { }` 替换为带日志输出的版本：

```csharp
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"方法名 error: {ex}");
}
```

涉及方法：
- `LoadDrivesAsync`
- `LoadChildrenAsync`

---

## 涉及文件清单

| 文件 | 修改类型 |
|------|---------|
| `ViewModels/MainViewModel.cs` | 主要修改 |
| `Models/FolderNode.cs` | 添加 Parent 属性 |

---

## 预期效果

1. **UI 流畅度提升**：图片加载不再阻塞 UI 线程
2. **内存占用降低**：分页加载避免一次性加载大量图片
3. **响应速度提升**：面包屑更新从 O(n²) 优化到 O(n)
4. **可调试性提升**：异常信息可追踪
