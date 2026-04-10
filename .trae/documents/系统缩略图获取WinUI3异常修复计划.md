# 系统缩略图获取 WinUI 3 异常修复计划

## 问题分析

### 核心问题
`ImageFile.GetThumbnailAsync()` 是 WinRT API，在 UI 线程上直接调用导致 WinUI 3 内部异常（0xC000027B）。

### 异常详情
- 异常发生在：`ImageFileInfo.GetThumbnailOnUIThreadAsync` 方法
- 调用：`ImageFile.GetThumbnailAsync()` 在 UI 线程上调用
- 错误：`0xC000027B: 发生应用程序内部异常

### 根本原因
WinRT StorageFile 相关 API 在 UI 线程上调用可能导致 WinUI 3 内部崩溃。

## 修复方案

### 移除系统缩略图获取
直接使用完整解码逻辑，完全避免 UI 线程问题。

## 修改文件

- `Models/ImageFileInfo.cs` - 简化 `GetThumbnailOnUIThreadAsync` 方法，直接调用完整解码

## 实施步骤

1. 简化 `GetThumbnailOnUIThreadAsync` 方法
2. 移除系统缩略图获取逻辑
3. 直接调用 `thumbnailService.GetThumbnailByLongSideAsync`
