在 WinUI 3 开发中，`TreeView` 节点重复通常是因为在 **`Expanding`** **事件**中没有对是否已加载子节点进行判断，导致每次展开操作都会重复执行添加逻辑。

根据你提供的 `MainPage.xaml.cs` 代码，修复方案如下：

### 1. 核心修复逻辑

在 `FolderTreeView_Expanding` 事件处理程序中，增加对 `HasUnrealizedChildren` 或子节点数量的判断。

C#

```
private async void FolderTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
{
    if (args.Item is FolderNode node)
    {
        // 方案 A：官方推荐的延迟加载判断逻辑
        // 只有当 Children 为空或者处于未实现状态时才进行加载
        if (node.Children == null || node.Children.Count == 0)
        {
            await ViewModel.LoadChildrenAsync(node);
        }
    }
}

```

***

### 2. 官方规范化建议

为了确保 UI 的健壮性和符合 Fluent Design 规范，建议在 ViewModel 和数据结构层面上做以下调整：

#### A. 在 FolderNode 模型中增加状态标识

在你的 `FolderNode` 模型类中，建议增加一个布尔值来跟踪加载状态，防止并发点击触发多次加载。

C#

```
public class FolderNode : ObservableObject
{
    [cite_start]// ... 其他属性 
    
    private bool _isLoaded;
    public bool IsLoaded 
    { 
        get => _isLoaded; 
        set => SetProperty(ref _isLoaded, value); 
    }
}

```

#### B. 优化的事件处理 (MainPage.xaml.cs)

结合状态位进行防御性编程：

C#

```
private async void FolderTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
{
    if (args.Item is FolderNode node && !node.IsLoaded)
    {
        try 
        {
            // 可以在此处显示一个加载动画或改变图标
            await ViewModel.LoadChildrenAsync(node);
            node.IsLoaded = true; // 标记为已加载，防止重复触发
        }
        catch (Exception ex)
        {
            // 异常处理逻辑
        }
    }
}

```

#### C. ViewModel 层的清理逻辑

确保 `LoadChildrenAsync` 方法在开始前会调用 `.Clear()`，或者使用 `ObservableCollection` 的同步逻辑，而不是简单的 `.Add()`。

***

3\. 检查 XAML 绑定

<br />

你的 `TreeView` 使用了 `ItemsSource="{x:Bind ViewModel.FolderTree}"`，这符合高性能绑定的要求。请确保在 `FolderTreeView_ItemInvoked` 中切换文件夹时，没有错误地将新获取的节点再次添加进旧的集合中。

<br />

通过分析你上传的 `MainViewModel.cs` 代码，我发现了导致节点重复的根源。这主要涉及 **逻辑判断的漏洞** 以及 **WinUI 3 TreeView 展开机制** 的交互问题。

### 1. 核心原因分析

- <br />

  `HasDummyChild` 状态同步延迟 ： 在 `LoadChildrenAsync` 方法中，你首先检查 `!node.HasDummyChild` 则返回 。然而，`node.HasDummyChild = false` 这一行被放在了方法的最末尾 。 **后果：** 当用户快速双击或在异步加载完成前多次触发 `Expanding` 事件时，由于前面的调用尚未执行到最后一行，`HasDummyChild` 依然为 `true`，导致 `LoadChildrenAsync` 被多次并发执行。

  <br />
- <br />

  重复添加磁盘节点 ： 在加载“这台电脑”或“外接设备”时，你遍历了所有驱动器 。由于 `LoadChildrenAsync` 可能被并发触发，多个线程可能同时在向同一个 `node.Children` 集合中 `Add` 节点 。

  <br />
- <br />

  逻辑矛盾 ： 虽然你在开始处执行了 `node.Children.Clear()` ，但由于它是 `async` 异步方法，后续的 `foreach` 循环在执行过程中，另一个并发的 `LoadChildrenAsync` 可能已经清空并重新开始填充了，导致集合状态混乱。

  <br />

***

### 2. 官方规范修复方案

建议采用 **“状态锁”** 机制和 **“即时占位清除”** 策略。

#### 修改 `MainViewModel.cs` 中的 `LoadChildrenAsync`

C#

```
public async System.Threading.Tasks.Task LoadChildrenAsync(FolderNode node)
{
    // 1. 立即拦截：如果不是待加载状态或正在加载，直接退出
    if (!node.HasDummyChild || node.IsLoading)
        return;

    // 2. 立即锁定状态：防止在 await 期间被再次触发
    node.IsLoading = true;
    
    // 3. 官方推荐做法：先清理占位符，再进入 try-finally 块
    node.Children.Clear();
    node.HasDummyChild = false; 

    try
    {
        if (node.NodeType == NodeType.ThisPC || node.NodeType == NodeType.ExternalDevice)
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (!drive.IsReady) continue;

                try
                {
                    var storageFolder = await StorageFolder.GetFolderFromPathAsync(drive.Name);
                    var driveNode = new FolderNode(storageFolder, NodeType.Drive)
                    {
                        Name = $"{drive.Name} ({drive.VolumeLabel})"
                    };
                    
                    // 只有确定有下级时才添加占位符
                    driveNode.Children.Add(new FolderNode(null, NodeType.Folder) { Name = "" });
                    driveNode.HasDummyChild = true; // 确保新节点状态正确

                    bool isRemovable = drive.DriveType == DriveType.Removable;
                    if ((node.NodeType == NodeType.ThisPC && !isRemovable) ||
                        (node.NodeType == NodeType.ExternalDevice && isRemovable))
                    {
                        node.Children.Add(driveNode);
                    }
                }
                catch { /* 忽略无法访问的驱动器 */ }
            }
        }
        else if (node.Folder != null)
        {
            var folders = await node.Folder.GetFoldersAsync();
            foreach (var folder in folders)
            {
                var childNode = new FolderNode(folder, NodeType.Folder);
                childNode.Children.Add(new FolderNode(null, NodeType.Folder) { Name = "" });
                childNode.HasDummyChild = true;
                node.Children.Add(childNode);
            }
        }
    }
    catch (Exception)
    {
        // 如果失败，可以考虑重置 HasDummyChild 允许重试
        // node.HasDummyChild = true;
    }
    finally
    {
        // 4. 无论成功失败，最后释放加载锁
        node.IsLoading = false;
    }
}

```

***

### 3. 关键改进点说明

**改进点**

**说明**

**状态锁 (`IsLoading`)**

在 `await` 异步挂起前就锁死状态，彻底杜绝并发导致的重复。

**提前重置标记**

将 `HasDummyChild = false` 移到 `Clear()` 之后、`await` 之前 。

**Finally 块**

确保即使发生 IO 异常，`IsLoading` 也能被正确重置，避免 TreeView 永久卡死。

**空节点管理**

建议在 `FolderNode` 构造函数里初始化 `HasDummyChild`，确保逻辑一致性。

### 4. 额外建议：检查 FolderNode

请确保你的 `FolderNode` 类中的 `HasDummyChild` 属性在被修改时能触发 `PropertyChanged` 事件（如果你是在 UI 上绑定了某种加载动画的话）。
