# 修复 PROPSYS.dll 灾难性故障

## 问题分析

### 错误信息
```
onecoreuap\shell\propsys\propdesc.cpp(2785)\PROPSYS.dll!00007FFC476CD09F: 
(caller: 00007FFC476CD315) LogHr(1) tid(ad78) 8000FFFF 灾难性故障
```

### 可能原因
1. PROPSYS.dll 在短时间内被多次调用导致内部状态问题
2. 某些图片的 EXIF 数据损坏
3. 同一 StorageFile 对象的属性访问冲突

---

## 修复方案

## [ ] 任务 1: 隔离每个属性访问，添加详细错误处理
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 将 `GetBasicPropertiesAsync()` 和 `GetImagePropertiesAsync()` 分开到独立的 try-catch 块
  - 添加更详细的错误日志，标识具体是哪个属性读取失败
  - 即使某个属性读取失败，也要继续尝试读取其他属性
- **Success Criteria**: 每个属性访问都有独立的错误处理

## [ ] 任务 2: 添加延迟加载机制
- **Priority**: P1
- **Depends On**: 任务 1
- **Description**: 
  - 不要在 `PrepareContent` 时立即加载所有属性
  - 只在需要显示时才加载（懒加载）
  - 或者延迟 100-200ms 后再加载属性
- **Success Criteria**: 属性加载不会阻塞图片显示

## [ ] 任务 3: 考虑添加重试机制
- **Priority**: P2
- **Depends On**: 任务 2
- **Description**: 
  - 如果属性读取失败，等待一小段时间后重试一次
  - 只重试一次，避免无限循环
- **Success Criteria**: 有简单的重试机制

## [ ] 任务 4: 构建和测试
- **Priority**: P1
- **Depends On**: 任务 3
- **Description**: 构建项目并测试
- **Success Criteria**: 构建成功，不再有灾难性故障提示
