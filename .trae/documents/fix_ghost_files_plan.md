# 修复加载不存在文件的问题

## 问题根因

在 `MainViewModel.cs` 第 248 行：
```csharp
IndexerOption = IndexerOption.UseIndexerWhenAvailable,
```

**Windows 索引器问题：**
- 索引器会缓存文件信息，可能返回已删除的文件
- 索引器可能索引临时文件、缩略图缓存等
- 文件名 `wehCduY.jpg` 看起来像是浏览器缓存或临时文件

## 解决方案

### 方案：禁用索引器 + 文件验证

1. **禁用索引器**：改用 `IndexerOption.DoNotUseIndexer` 直接查询文件系统
2. **文件验证**：在 `LoadImageInfoSafeAsync` 中验证文件可访问性，失败时返回 `null` 跳过

## 实施步骤

### 步骤 1：修改 LoadImagesAsync 查询选项
- 将 `IndexerOption.UseIndexerWhenAvailable` 改为 `IndexerOption.DoNotUseIndexer`

### 步骤 2：修改 LoadImagesWithoutHistoryAsync 查询选项
- 同样修改索引器选项

### 步骤 3：修改 LoadImageInfoSafeAsync 返回类型
- 返回类型改为 `ImageFileInfo?`
- 文件无法访问时返回 `null`

### 步骤 4：修改调用方过滤 null 结果
- 在 `LoadImagesAsync` 中过滤 `null`
- 在 `LoadImagesWithoutHistoryAsync` 中过滤 `null`

### 步骤 5：构建验证
运行 `dotnet build` 确保无错误
