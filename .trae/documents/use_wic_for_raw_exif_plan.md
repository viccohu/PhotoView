# 使用 WIC (BitmapDecoder) 读取 RAW 文件 EXIF

## 已有基础

✅ 好消息！项目中已在使用 WIC (Windows Imaging Component)：
- `ThumbnailService.cs` 中使用 `BitmapDecoder.CreateAsync()` 
- 这正是读取 RAW 文件 EXIF 的正确 API

---

## 修复方案

## [ ] 任务 1: 创建使用 WIC 读取 EXIF 的方法
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 创建一个新方法 `LoadImagePropertiesWithWicAsync()`
  - 使用 `BitmapDecoder` 读取图片的元数据
  - 从 `BitmapDecoder.BitmapProperties` 获取 EXIF 数据
  - 特别处理 RAW 文件
- **Success Criteria**: 有独立的 WIC 读取方法

## [ ] 任务 2: 检测文件类型，选择正确的 API
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 创建 RAW 文件扩展名列表
  - 对于标准图片（JPG/PNG/HEIC 等）：继续使用 `GetImagePropertiesAsync()`
  - 对于 RAW 文件：使用 `BitmapDecoder` 读取 EXIF
- **Success Criteria**: 能根据文件类型选择正确的 API

## [ ] 任务 3: 更新 LoadFileInfoAsync
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 
  - 整合两种读取方式
  - 独立的错误处理：即使一个失败，另一个还能尝试
  - 详细的日志输出
- **Success Criteria**: LoadFileInfoAsync 使用新的读取方式

## [ ] 任务 4: 构建和测试
- **Priority**: P1
- **Depends On**: 任务 3
- **Description**: 构建项目并测试
- **Success Criteria**: 构建成功，RAW 文件也能正常读取 EXIF
