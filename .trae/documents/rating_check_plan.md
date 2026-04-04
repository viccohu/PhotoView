# 评级功能链路检查与修复计划

## 当前链路分析

### 1. 读取链路
- MainViewModel (Line 522): `_ = imageInfo.LoadRatingAsync(_ratingService)` 
- ImageFileInfo.LoadRatingAsync -> RatingService.GetRatingAsync
- RatingService: WinRT API (支持格式) -> RatingCacheService (缓存)
- ImageFileInfo.Rating 属性更新 -> 触发 RatingValue 通知

### 2. 显示链路
- MainPage.xaml: RatingControl.Value 绑定到 RatingValue
- RatingValue: rating -> 星星数转换

### 3. 写入链路
- RatingControl.ValueChanged -> RatingControl_ValueChanged
- 查找 ImageFileInfo -> 星星数转 rating -> UpdateRatingAsync
- UpdateRatingAsync -> RatingService.SetRatingAsync
- RatingService: WinRT API (支持格式) -> RatingCacheService (缓存)
- 写入完成后设置 imageInfo.Rating

---

## 发现的问题

### 问题 1: RatingToStars 映射不完整
**位置**: ImageFileInfo.cs (Line 390-397)
**问题**: rating 在 75-98 之间应该返回 4 星，但直接跳过返回 5 星了
```csharp
private static double RatingToStars(int rating)
{
    if (rating == 0) return 0;
    if (rating <= 24) return 1;
    if (rating <= 49) return 2;
    if (rating <= 74) return 3;
    return 5;  // 漏掉了 4 星！
}
```
**修复**: 添加 75-98 -> 4 星

---

### 问题 2: LoadRatingAsync 调用没有 await
**位置**: MainViewModel.cs (Line 522)
**问题**: 使用 `_ = imageInfo.LoadRatingAsync(_ratingService)` 没有 await，可能导致:
- 加载时机不确定
- 异常没有被捕获
- UI 更新时机不确定

---

### 问题 3: UpdateRatingAsync 中重复设置 rating
**位置**: MainPage.xaml.cs (Line 566-579)
**问题**: 
```csharp
await ratingService.SetRatingAsync(imageInfo.ImageFile, rating);
DispatcherQueue.TryEnqueue(() =>
{
    imageInfo.Rating = (int)rating;  // 这里又设置了一次！
});
```
RatingService.SetRatingAsync 应该已经更新了 rating，这里重复设置可能导致问题。

---

### 问题 4: SetRatingAsync 成功写入后不更新缓存
**位置**: RatingService.cs (Line 61-87)
**问题**: WinRT 支持的格式成功写入后直接 return，没有更新缓存
```csharp
if (IsWinRTRatingSupported(file.FileType))
{
    // ... 写入成功 ...
    return;  // 没有同步更新缓存！
}
await _cacheService.SetRatingAsync(file.Path, (int)rating);
```
**修复**: WinRT 写入成功后也要同步更新缓存

---

### 问题 5: RatingControl_ValueChanged 中 double 转 int 不精确
**位置**: MainPage.xaml.cs (Line 526)
**问题**: 直接使用 `(int)stars`，stars 是 double 类型，可能有精度问题
```csharp
var rating = ((int)stars) switch { ... }
```
**修复**: 使用 Math.Round 或者更精确的转换

---

### 问题 6: Rating 属性设置逻辑问题
**位置**: ImageFileInfo.cs (Line 363-375)
**问题**: 没有使用 SetProperty，手动调用 OnPropertyChanged 可能不够安全

---

## 修复任务列表

- [x] 分析链路，识别问题
- [ ] 修复 RatingToStars 映射（添加 4 星）
- [ ] 修复 SetRatingAsync 缓存同步
- [ ] 修复 Rating 属性设置逻辑（使用 SetProperty）
- [ ] 优化 UpdateRatingAsync（避免重复设置）
- [ ] 优化 RatingControl_ValueChanged（double 转 int 精度）
- [ ] 测试验证完整链路
