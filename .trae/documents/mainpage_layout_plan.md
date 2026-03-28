# MainPage 相册布局 - 实施计划

## [ ] 任务 1: 创建文件夹树节点数据模型
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 创建 FolderNode 类表示文件夹树节点
  - 支持子文件夹和图片
  - 实现 INotifyPropertyChanged
- **Success Criteria**:
  - 数据模型完整可用
- **Test Requirements**:
  - `programmatic` TR-1.1: 代码编译无错误

## [ ] 任务 2: 更新 MainViewModel 支持文件夹树
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 添加 CurrentFolder 属性
  - 添加 FolderTree 集合用于 TreeView
  - 添加 SelectedImages 集合用于 ListView
  - 实现文件夹加载和导航逻辑
- **Success Criteria**:
  - ViewModel 支持文件夹树导航
- **Test Requirements**:
  - `programmatic` TR-2.1: 代码编译无错误

## [ ] 任务 3: 更新 MainPage.xaml 实现双栏布局
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 
  - 使用 Grid 实现左右分栏
  - 左侧：TreeView 显示文件夹树
  - 右侧：ListView + ItemsWrapGrid 实现流式布局
  - 实现图片高度限制和比例自适应
  - 使用官方推荐的虚拟化和布局
- **Success Criteria**:
  - 布局符合要求，图片按比例自适应
- **Test Requirements**:
  - `programmatic` TR-3.1: 项目构建成功
  - `human-judgement` TR-3.2: 视觉上符合双栏文件管理器布局，图片比例正确

## [ ] 任务 4: 更新 MainPage.xaml.cs 实现交互逻辑
- **Priority**: P0
- **Depends On**: 任务 3
- **Description**: 
  - TreeView 选择事件处理
  - 文件夹导航
- **Success Criteria**:
  - 交互功能正常
- **Test Requirements**:
  - `programmatic` TR-4.1: 项目构建成功
  - `human-judgement` TR-4.2: 文件夹选择和图片显示正常

## [ ] 任务 5: 更新 CollectPage 保持一致性（可选）
- **Priority**: P2
- **Depends On**: 任务 1-4
- **Description**: 
  - 可选：CollectPage 也采用类似布局
- **Success Criteria**:
  - CollectPage 功能正常
- **Test Requirements**:
  - `programmatic` TR-5.1: 项目构建成功

## [ ] 任务 6: 验证并测试
- **Priority**: P1
- **Depends On**: 任务 1-5
- **Description**: 
  - 全面测试所有功能
- **Success Criteria**:
  - 所有功能正常工作
- **Test Requirements**:
  - `programmatic` TR-6.1: 项目构建无错误
  - `human-judgement` TR-6.2: 用户体验流畅
