# 移除 Core 文件夹实现计划

## 目标
安全移除 `d:\PhotoView\PhotoView\Core` 文件夹，同时确保现有业务功能不受影响。

## 依赖分析
通过分析，发现主项目 `PhotoView` 依赖于 Core 项目中的以下组件：
1. `IFileService` 接口
2. `FileService` 实现
3. `Json` 帮助类

这些依赖在以下文件中使用：
- `Services/LocalSettingsService.cs`：使用 `IFileService` 和 `Json`
- `App.xaml.cs`：注册 `IFileService` 服务
- `Helpers/SettingsStorageExtensions.cs`：使用 `Json`

## 实施计划

### [x] 任务 1：复制 Core 项目中的必要文件到主项目
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 将 `Core/Contracts/Services/IFileService.cs` 复制到 `Contracts/Services/`
  - 将 `Core/Services/FileService.cs` 复制到 `Services/`
  - 将 `Core/Helpers/Json.cs` 复制到 `Helpers/`
- **成功标准**：所有必要文件已复制到主项目相应目录
- **测试要求**：
  - `programmatic` TR-1.1：文件复制成功，无错误
  - `human-judgment` TR-1.2：文件结构合理，符合项目组织规范

### [x] 任务 2：更新复制文件的命名空间
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 更新 `IFileService.cs` 的命名空间从 `PhotoView.Core.Contracts.Services` 改为 `PhotoView.Contracts.Services`
  - 更新 `FileService.cs` 的命名空间从 `PhotoView.Core.Services` 改为 `PhotoView.Services`
  - 更新 `Json.cs` 的命名空间从 `PhotoView.Core.Helpers` 改为 `PhotoView.Helpers`
- **成功标准**：所有文件的命名空间已更新
- **测试要求**：
  - `programmatic` TR-2.1：命名空间更新正确，无编译错误
  - `human-judgment` TR-2.2：命名空间与项目结构一致

### [x] 任务 3：更新主项目中的引用
- **优先级**：P0
- **依赖**：任务 2
- **描述**：
  - 更新 `Services/LocalSettingsService.cs` 中的引用，移除对 `PhotoView.Core.*` 的引用
  - 更新 `App.xaml.cs` 中的引用，移除对 `PhotoView.Core.*` 的引用
  - 更新 `Helpers/SettingsStorageExtensions.cs` 中的引用，移除对 `PhotoView.Core.*` 的引用
- **成功标准**：所有文件中的引用已更新
- **测试要求**：
  - `programmatic` TR-3.1：引用更新正确，无编译错误
  - `human-judgment` TR-3.2：代码整洁，无冗余引用

### [x] 任务 4：移除 Core 项目的项目引用
- **优先级**：P1
- **依赖**：任务 3
- **描述**：
  - 从 `PhotoView.csproj` 中移除对 `PhotoPro.Core.csproj` 的引用
- **成功标准**：Core 项目引用已移除
- **测试要求**：
  - `programmatic` TR-4.1：项目文件中无 Core 项目引用
  - `human-judgment` TR-4.2：项目配置清晰，无冗余引用

### [x] 任务 5：编译和测试
- **优先级**：P0
- **依赖**：任务 4
- **描述**：
  - 编译项目，确保无编译错误
  - 运行应用，确保所有功能正常工作
- **成功标准**：项目编译成功，应用运行正常
- **测试要求**：
  - `programmatic` TR-5.1：编译无错误
  - `programmatic` TR-5.2：应用启动正常，无运行时错误
  - `human-judgment` TR-5.3：应用功能与移除 Core 文件夹前一致

### [x] 任务 6：删除 Core 文件夹
- **优先级**：P1
- **依赖**：任务 5
- **描述**：
  - 删除 `d:\PhotoView\PhotoView\Core` 文件夹
- **成功标准**：Core 文件夹已删除
- **测试要求**：
  - `programmatic` TR-6.1：Core 文件夹不存在
  - `human-judgment` TR-6.2：项目结构整洁，无冗余文件夹

## 风险评估
- **风险**：如果复制文件或更新引用时出错，可能导致编译失败或运行时错误
- **缓解措施**：
  - 仔细检查每个文件的内容和引用
  - 确保所有依赖都已正确迁移
  - 在删除 Core 文件夹前进行充分测试

## 预期结果
- Core 文件夹被成功移除
- 项目编译正常，无错误
- 应用功能与移除 Core 文件夹前一致
- 项目结构更简洁，减少不必要的项目依赖