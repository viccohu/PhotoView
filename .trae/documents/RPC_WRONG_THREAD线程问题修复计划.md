# RPC_WRONG_THREAD 线程问题修复计划

## 🎯 问题分析

### 日志证据

```
Microsoft.UI.Xaml.dll!00007FFC2127DDC9: 8001010E - RPC_E_WRONG_THREAD
WinRT originate error - 0x8001010E : '应用程序调用一个已为另一线程整理的接口。'
```

### 根本原因

在 `ImageFileInfo.cs` 中：

**当前流程（错误）：**
1. 后台线程 → `GetThumbnailAsync()` → 调用 `ImageFile.GetThumbnailAsync()` 获取 `IRandomAccessStream`
2. 然后封送到 UI 线程 → `CreateBitmapOnUIThreadAsync(stream)` → 调用 `bitmap.SetSource(stream)`

**问题：**
- `IRandomAccessStream` 是 WinRT COM 对象，不是 agile 的
- 在后台线程获取 stream，在 UI 线程使用它 → `RPC_E_WRONG_THREAD`
- 概率性崩溃 + 快速切换必崩溃

---

## ✅ 修复方案

### 核心原则

> "在哪里拿 stream，就在哪里用"

### 修改内容

| 文件 | 修改项 |
|------|---------|
| **ImageFileInfo.cs** | 重构 `GetThumbnailAsync`，新增 `GetThumbnailOnUIThreadAsync`，删除 `CreateBitmapOnUIThreadAsync` |

---

## 🔧 详细修改步骤

### 第一步：重构 `GetThumbnailAsync`

**原代码：**
```csharp
private async Task<BitmapImage> GetThumbnailAsync(ThumbnailSize size, CancellationToken cancellationToken)
{
    var thumbnail = await ImageFile.GetThumbnailAsync(...);
    return await CreateBitmapOnUIThreadAsync(thumbnail, ...);
}
```

**新代码：**
```csharp
private async Task<BitmapImage> GetThumbnailAsync(ThumbnailSize size, CancellationToken cancellationToken)
{
    var requestedSize = (uint)size;
    var optimalSize = GetOptimalThumbnailSize(requestedSize);
    var dispatcher = App.MainWindow.DispatcherQueue;

    if (dispatcher.HasThreadAccess)
    {
        return await GetThumbnailOnUIThreadAsync(optimalSize, cancellationToken);
    }
    else
    {
        var tcs = new TaskCompletionSource<BitmapImage>();
        dispatcher.TryEnqueue(async () =>
        {
            try
            {
                var bmp = await GetThumbnailOnUIThreadAsync(optimalSize, cancellationToken);
                tcs.SetResult(bmp);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return await tcs.Task;
    }
}
```

### 第二步：新增 `GetThumbnailOnUIThreadAsync`

```csharp
private async Task<BitmapImage> GetThumbnailOnUIThreadAsync(
    uint optimalSize,
    CancellationToken cancellationToken)
{
    if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
        return new BitmapImage();

    using var thumbnail = await ImageFile.GetThumbnailAsync(
        ThumbnailMode.SingleItem,
        optimalSize,
        ThumbnailOptions.None);

    if (thumbnail != null && thumbnail.Size > 0)
    {
        var bitmap = new BitmapImage();
        bitmap.DecodePixelWidth = (int)optimalSize;
        bitmap.SetSource(thumbnail); // ✅ 同线程
        return bitmap;
    }

    return new BitmapImage();
}
```

### 第三步：删除 `CreateBitmapOnUIThreadAsync`

整个函数不再需要，删除它。

---

## 🎯 最终效果

- ✅ `RPC_E_WRONG_THREAD` 异常消失
- ✅ 不再在 UI 线程使用跨线程的 WinRT stream
- ✅ 快速切换文件夹不再崩溃
- ✅ 保持原有的版本号、CTS、UI 线程封送等并发控制

---

## 📝 风险评估

| 风险 | 等级 | 说明 |
|------|------|------|
| 修改范围 | 中 | 只修改 ImageFileInfo.cs，不涉及其他文件 |
| 兼容性 | 高 | 保持公共 API 不变 |
| 测试需要 | 高 | 需要测试快速切换文件夹场景 |
