# 切换目录图片残留问题修复计划

## 一、日志分析

### 关键发现

1. **`Images.Count=0`** - 每次切换目录时，`ImagesChanged` 事件触发时 `Images` 都是空的
2. **RPC_E_WRONG_THREAD 错误** - 线程访问错误

### 根本原因

查看 `MainViewModel.cs` 第 165-168 行：

```csharp
Images.Clear();                              // 1. 清空图片
SelectedFolder = folderNode;                  // 2. 设置选中文件夹
UpdateBreadcrumbPath(folderNode);             // 3. 更新面包屑
ImagesChanged?.Invoke(this, EventArgs.Empty); // 4. 触发事件（此时 Images.Count=0）
```

**时序问题**：
- `ImagesChanged` 在 `Images.Clear()` 后立即触发
- 此时 `Images` 是空集合
- UI 绑定的是空集合，但旧的 UI 容器还在
- 图片数据是异步加载的（第 178-213 行），在事件触发后才加载

### 为什么会有残留

1. `ViewModel_ImagesChanged` 中绑定 `ViewModel.Images`（空集合）
2. ItemsView 显示空数据，但旧的 ItemContainer 没有被正确清理
3. 新图片异步加载后，新 UI 元素叠加在旧元素上

## 二、修复方案

### 方案 A：改变事件触发时机（推荐）

在数据加载完成后再触发 `ImagesChanged`：

```csharp
public async System.Threading.Tasks.Task LoadImagesAsync(FolderNode folderNode)
{
    _loadImagesCts?.Cancel();
    _loadImagesCts = new CancellationTokenSource();
    var cancellationToken = _loadImagesCts.Token;

    SelectedFolder = folderNode;
    UpdateBreadcrumbPath(folderNode);
    
    // 先触发事件通知 UI 清空
    ImagesChanged?.Invoke(this, EventArgs.Empty);
    
    // 清空数据
    Images.Clear();

    if (folderNode?.Folder == null)
        return;

    try
    {
        // ... 加载图片代码 ...
        
        // 加载完成后再触发一次
        ImagesChanged?.Invoke(this, EventArgs.Empty);
    }
    // ...
}
```

### 方案 B：传递更多信息

修改 `ImagesChanged` 事件，传递状态信息：

```csharp
public class ImagesChangedEventArgs : EventArgs
{
    public bool IsClearing { get; set; }
    public bool IsLoaded { get; set; }
}
```

### 方案 C：在 ViewModel_ImagesChanged 中等待数据

```csharp
private void ViewModel_ImagesChanged(object? sender, EventArgs e)
{
    // 如果 Images 为空，等待数据加载
    if (ViewModel.Images.Count == 0)
    {
        // 不做任何操作，等待下一次事件
        return;
    }
    
    // 正常处理...
}
```

## 三、推荐方案

采用 **方案 A + C 结合**：

1. 修改 `MainViewModel.LoadImagesAsync`：
   - 清空前先触发 `ImagesChanged`（通知 UI 清空）
   - 加载完成后再触发 `ImagesChanged`（通知 UI 更新）

2. 修改 `ViewModel_ImagesChanged`：
   - 如果 `Images.Count == 0`，只清空不绑定
   - 如果 `Images.Count > 0`，正常绑定数据

## 四、执行任务

| 序号 | 任务 | 说明 |
|------|------|------|
| 1 | 修改 MainViewModel.LoadImagesAsync | 调整事件触发时机 |
| 2 | 修改 ViewModel_ImagesChanged | 处理空数据情况 |
| 3 | 添加日志 | 验证修复效果 |
| 4 | 构建测试 | 确认问题解决 |
