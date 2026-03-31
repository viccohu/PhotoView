# 缩略图加载深度优化方案

## 问题分析（来自文档）

### 1. DispatcherQueue 任务堆积与资源竞争
**问题**：快速滚动时，成百上千个 `EnsureThumbnailAsync` 任务同时触发，每个任务都往 UI 线程队列塞 `SetSource`。
**后果**：UI 线程被占满，界面假死，内存溢出或资源访问冲突。
**建议**：引入 `CancellationTokenSource`，取消旧任务。

### 2. MainViewModel 同步阻塞循环
**问题**：`LoadImagesAsync` 中每页内串行等待每张图片属性加载。
**后果**：加载 100 张图片很慢，用户看到图片一个接一个蹦出来。
**建议**：用 `Task.WhenAll` 并行加载一整页。

### 3. JustifiedLayout 潜在风险
**问题**：`MeasureOverride` 中手动调用 `GetOrCreateElementAt` 并 `Measure`。
**后果**：可能导致布局引擎不一致，引发 `InvalidOperationException` 或内存泄漏。
**建议**：仅在 `BuildRows` 中计算，让 ItemsRepeater 自动管理元素。

### 4. UpdateItemSelectionStates 低效
**问题**：选择变化时遍历整个可见区域手动切换 `VisualState`。
**后果**：点击延迟感明显。
**建议**：在 `ImageFileInfo` 加 `IsSelected` 属性，用绑定驱动。

### 5. ViewModel_ThumbnailSizeChanged 死亡死循环
**问题**：改变大小时遍历所有图片加载。
**后果**：2000 张图瞬间开启 2000 个异步任务争抢 IO。
**建议**：只处理可见区域，或分批处理。

---

## 优化方案

### [ ] 任务 1：ImageFileInfo 引入 CancellationTokenSource
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 添加 `CancellationTokenSource _cts;` 字段
  - 在 `EnsureThumbnailAsync` 开始时：`_cts?.Cancel(); _cts = new();`
  - 传递 `_cts.Token` 到异步操作
- **成功标准**：快速滚动时取消旧任务
- **测试要求**：
  - `programmatic` TR-1.1：快速滚动不崩溃

### [ ] 任务 2：添加 ImageRepeater_ElementCleared 事件
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 在 `MainPage.xaml.cs` 中添加 `ImageRepeater_ElementCleared` 事件处理
  - 在事件中调用 `imageInfo.CancelLoading()`
- **成功标准**：滚出屏幕的元素取消加载
- **测试要求**：
  - `programmatic` TR-2.1：滚出屏幕时任务被取消

### [ ] 任务 3：DispatcherQueue 优先级设置
- **优先级**：P1
- **依赖**：无
- **描述**：
  - 在 `CreateBitmapOnUIThreadAsync` 中使用 `DispatcherQueuePriority.Low`
- **成功标准**：缩略图不抢占 UI 响应优先级
- **测试要求**：
  - `human-judgment` TR-3.1：滚动响应流畅

### [ ] 任务 4：MainViewModel 并行加载
- **优先级**：P1
- **依赖**：无
- **描述**：
  - 修改 `LoadImagesAsync` 中的串行循环
  - 使用 `Task.WhenAll(batch.Select(file => LoadImageInfo(file)))`
- **成功标准**：每页图片并行加载
- **测试要求**：
  - `programmatic` TR-4.1：加载速度提升

### [ ] 任务 5：ImageFileInfo 添加 IsSelected 属性
- **优先级**：P1
- **依赖**：无
- **描述**：
  - 添加 `IsSelected` 属性，实现 `INotifyPropertyChanged`
- **成功标准**：选择状态通过属性驱动
- **测试要求**：
  - `programmatic` TR-5.1：选择状态通过绑定更新

### [ ] 任务 6：XAML 修改使用 IsSelected 绑定
- **优先级**：P1
- **依赖**：任务 5
- **描述**：
  - 修改 `MainPage.xaml` 中的 VisualState 绑定
  - 使用 `{x:Bind IsSelected, Mode=OneWay}` 驱动状态
  - 删除 `UpdateItemSelectionStates` 方法
- **成功标准**：选择状态更新通过绑定
- **测试要求**：
  - `programmatic` TR-6.1：选择状态更新流畅

### [ ] 任务 7：ViewModel_ThumbnailSizeChanged 只处理可见区域
- **优先级**：P1
- **依赖**：无
- **描述**：
  - 修改 `ViewModel_ThumbnailSizeChanged`，只处理可见元素
  - 或者分批处理，避免一次性加载所有
- **成功标准**：改变大小时不开启大量任务
- **测试要求**：
  - `programmatic` TR-7.1：改变大小不卡顿

---

## 技术要点

### 取消令牌模式
```csharp
public async Task EnsureThumbnailAsync(ThumbnailSize size)
{
    _cts?.Cancel();
    _cts = new CancellationTokenSource();
    var token = _cts.Token;

    try
    {
        Thumbnail = await GetThumbnailAsync(size, token);
    }
    catch (OperationCanceledException)
    {
    }
}

public void CancelLoading()
{
    _cts?.Cancel();
    _cts = null;
}
```

### 并行加载模式
```csharp
var tasks = batch.Select(file => LoadImageInfo(file));
var results = await Task.WhenAll(tasks);
list.AddRange(results);
```

### 选择状态绑定
```xml
<VisualStateManager.VisualStateGroups>
    <VisualStateGroup>
        <VisualState x:Name="Selected">
            <VisualState.StateTriggers>
                <StateTrigger IsActive="{x:Bind IsSelected, Mode=OneWay}"/>
            </VisualState.StateTriggers>
        </VisualState>
    </VisualStateGroup>
</VisualStateManager.VisualStateGroups>
```

---

## 预期结果
1. 快速滚动不再崩溃
2. 图片加载更流畅
3. 选择状态响应更快
4. 内存使用更合理
