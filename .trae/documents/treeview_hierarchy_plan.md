# TreeView 层级结构修改计划

## [x] 任务 1: 更新 FolderNode 支持节点类型标识
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 在 FolderNode 中添加 NodeType 属性来区分不同类型的节点
  - 节点类型：ThisPC, ExternalDevice, Drive, Folder
- **Success Criteria**:
  - FolderNode 可以正确标识节点类型
  - 所有现有功能不受影响
- **Test Requirements**:
  - `programmatic` TR-1.1: 可以设置和获取 NodeType 属性
  - `programmatic` TR-1.2: 代码编译成功
- **Notes**: 使用枚举类型来定义 NodeType

## [x] 任务 2: 修改 MainViewModel 实现驱动器列表获取
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 创建 GetDrivesAsync() 方法获取系统驱动器
  - 区分本地驱动器和外接设备
  - 实现 GetLogicalDrives 或 StorageFolder API
- **Success Criteria**:
  - 能正确获取所有驱动器
  - 能区分本地和外接设备
- **Test Requirements**:
  - `programmatic` TR-2.1: GetDrivesAsync 返回驱动器列表
  - `programmatic` TR-2.2: 代码编译成功
  - `human-judgement` TR-2.3: 驱动器显示正确
- **Notes**: 使用 DriveInfo 或 StorageLibrary API

## [x] 任务 3: 重写 LoadDrivesAsync 构建新层级
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 
  - 重写 LoadDrivesAsync 方法
  - 创建一级节点："这台电脑" 和 "外接设备"
  - 在"这台电脑"下添加本地驱动器
  - 在"外接设备"下添加外接设备（如果有）
- **Success Criteria**:
  - TreeView 显示正确的层级结构
  - 一级：这台电脑、外接设备
  - 二级：各分区
- **Test Requirements**:
  - `programmatic` TR-3.1: FolderTree 包含正确的节点结构
  - `programmatic` TR-3.2: 代码编译成功
  - `human-judgement` TR-3.3: TreeView 显示符合预期
- **Notes**: 保持虚拟加载功能

## [x] 任务 4: 更新 LoadChildrenAsync 处理不同节点类型
- **Priority**: P0
- **Depends On**: 任务 3
- **Description**: 
  - 更新 LoadChildrenAsync 方法
  - 根据 NodeType 处理不同类型节点的子节点加载
  - Drive 节点加载根目录文件夹
  - Folder 节点加载子文件夹
- **Success Criteria**:
  - 所有节点类型都能正确加载子节点
  - 虚拟加载正常工作
- **Test Requirements**:
  - `programmatic` TR-4.1: 驱动器节点能加载文件夹
  - `programmatic` TR-4.2: 文件夹节点能加载子文件夹
  - `programmatic` TR-4.3: 代码编译成功
- **Notes**: 保持原有错误处理

## [x] 任务 5: 测试和验证
- **Priority**: P1
- **Depends On**: 任务 4
- **Description**: 
  - 完整测试 TreeView 功能
  - 测试驱动器展开
  - 测试文件夹导航
  - 测试图片加载
- **Success Criteria**:
  - 所有功能正常工作
  - 无错误
- **Test Requirements**:
  - `human-judgement` TR-5.1: TreeView 导航流畅
  - `human-judgement` TR-5.2: 图片加载正常
  - `programmatic` TR-5.3: 项目构建成功
- **Notes**: 进行全面测试
