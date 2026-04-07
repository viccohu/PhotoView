# 修复 StorageFile 并发访问异常

## 问题分析

### 异常信息
```
WinRT originate error - 0x8000000E : '无法调用请求的方法(GetImagePropertiesAsync)。对此方法的上一次调用仍在进行，必须等其返回后才能再次调用。'
```

### 问题根源
在 `PrepareContent` 方法（第 62-64 行）中：
```csharp
_ = LoadFileSizeAsync();      // 调用 GetBasicPropertiesAsync()
_ = LoadFilePathsAsync();     // 可能访问文件
_ = LoadImagePropertiesAsync(); // 调用 GetImagePropertiesAsync()
```

**三个方法同时并行访问同一个 `StorageFile` 对象**，导致并发冲突！

---

## 修复方案

## [ ] 任务 1: 合并所有属性加载到一个方法
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 创建一个统一的 `LoadFileInfoAsync()` 方法
  - 在该方法中顺序调用所有属性获取
  - 移除三个独立的调用
- **Success Criteria**: 所有属性加载在一个方法中顺序执行

## [ ] 任务 2: 更新 PrepareContent
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 在 PrepareContent 中只调用一个 `_ = LoadFileInfoAsync();`
  - 移除三个独立的 fire-and-forget 调用
- **Success Criteria**: PrepareContent 只调用一个统一的加载方法

## [ ] 任务 3: 构建和测试
- **Priority**: P1
- **Depends On**: 任务 2
- **Description**: 构建项目并测试
- **Success Criteria**: 构建成功，不再有并发异常
