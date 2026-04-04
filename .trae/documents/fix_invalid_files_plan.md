# 修复加载无效文件问题

## 问题分析

在 `MainViewModel.cs` 的 `LoadImagesAsync` 方法中：

1. **索引器问题**：使用 `IndexerOption.UseIndexerWhenAvailable` 可能返回已删除/移动的文件（索引器数据过期）

2. **错误处理问题**：`LoadImageInfoSafeAsync` 在文件无法访问时仍创建默认 `ImageFileInfo` 对象，导致"幽灵"图片出现

```csharp
// 当前问题代码：失败时仍返回对象
catch (Exception ex)
{
    return new ImageFileInfo(200, 200, string.Empty, 0, file, file.DisplayName, file.DisplayType);
}
```

## 解决方案

### 方案 1：验证文件存在性（推荐）

在 `LoadImageInfoSafeAsync` 中：
- 尝试打开文件流验证文件是否真实存在
- 如果文件不存在或无法访问，返回 `null` 表示跳过
- 在调用方过滤掉 `null` 结果

### 方案 2：使用更可靠的查询选项

将 `IndexerOption.UseIndexerWhenAvailable` 改为 `IndexerOption.DoNotUseIndexer`
- 缺点：查询速度变慢
- 优点：结果更准确

## 实施步骤

### 步骤 1：修改 LoadImageInfoSafeAsync 返回类型
- 返回类型改为 `ImageFileInfo?`
- 文件无法访问时返回 `null`

### 步骤 2：添加文件验证逻辑
- 尝试打开文件流
- 验证文件扩展名是否在允许列表中
- 捕获所有异常并返回 `null`

### 步骤 3：修改调用方处理 null 结果
- 在 `LoadImagesAsync` 中过滤 `null` 结果
- 在 `LoadImagesWithoutHistoryAsync` 中过滤 `null` 结果

### 步骤 4：构建验证
运行 `dotnet build` 确保无错误
