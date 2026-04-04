# 删除功能实现计划

## 需求概述
为 PhotoView 添加图片删除功能，包括预删除标记和真实删除两个阶段。

## 一、数据模型修改

### 1.1 ImageFileInfo.cs - 添加预删除状态
- 新增 `IsPendingDelete` 属性（bool，Observable）
- 用于标记图片是否处于预删除状态

### 1.2 MainViewModel.cs - 添加预删除管理
- 新增 `PendingDeleteImages` 集合（或使用计数属性）
- 新增 `PendingDeleteCount` 属性（int，Observable）
- 新增 `TogglePendingDeleteCommand` 命令
- 新增 `ClearAllPendingDelete()` 方法

## 二、UI 层修改

### 2.1 MainPage.xaml - 标题栏区域
- 在 "xx photos" 后添加预删除计数显示
  - 绑定到 `ViewModel.PendingDeleteCount`
  - 显示格式：红色圆角边框内 "预删除 xx 张"
  - Visibility 绑定到计数是否大于 0

### 2.2 MainPage.xaml - 删除按钮
- 在右上角功能区（ThumbnailSize DropDownButton 旁边）添加删除下拉按钮
- 图标：&#xE74D; (Delete 图标)
- 菜单选项：
  - 仅删除 JPG
  - 仅删除 RAW
  - 全部删除

### 2.3 MainPage.xaml - 缩略图预删除标记
- 在 GridView ItemTemplate 中添加预删除标记 Border
  - 尺寸：85x25
  - 红色背景，圆角（底部两角）
  - 显示 "预删除" 文字
  - Visibility 绑定到 `IsPendingDelete`

### 2.4 MainPage.xaml - 选中项预删除标记
- 在选中边框（SelectionBorder）附近添加预删除状态视觉反馈
- 使用红色边框或覆盖层

## 三、交互逻辑

### 3.1 MainPage.xaml.cs - 键盘事件
- 在 `MainPage_KeyDown` 中添加 Delete 键处理
- 逻辑：切换选中图片的预删除状态
- 如果没有选中项，不执行任何操作

### 3.2 删除菜单命令
- 点击菜单项后弹出确认对话框
- 根据选项过滤要删除的文件：
  - 仅删除 JPG：只删除 `.jpg/.jpeg` 文件
  - 仅删除 RAW：只删除 RAW 格式文件
  - 全部删除：删除所有预删除标记的文件

## 四、确认对话框

### 4.1 创建 DeleteConfirmDialog.xaml
- ContentDialog 样式
- 显示待删除文件数量
- 显示删除类型（JPG/RAW/全部）
- 确认和取消按钮
- 进度条（删除进行中显示）
- 删除完成后自动关闭

### 4.2 删除进度处理
- 使用 ProgressBar 显示进度
- 后台线程执行删除操作
- 删除完成后更新 UI

## 五、删除后界面更新策略

### 5.1 增量更新方案
- 不重新加载整个列表
- 从 `Images` 集合中移除已删除项
- 保持当前滚动位置和焦点

### 5.2 具体实现
1. 记录当前滚动位置（第一个可见项索引）
2. 记录当前选中项
3. 执行删除操作
4. 从 Images 集合中 Remove 已删除项
5. 恢复滚动位置
6. 如果原选中项已删除，选中相邻项

### 5.3 MainViewModel 新增方法
- `RemoveDeletedImagesAsync(List<ImageFileInfo> deletedImages)` 
- `ExecuteDeleteAsync(DeleteType deleteType)` 

## 六、文件结构

### 新增文件
```
Dialogs/
  DeleteConfirmDialog.xaml
  DeleteConfirmDialog.xaml.cs
```

### 修改文件
```
Models/ImageFileInfo.cs          - 添加 IsPendingDelete 属性
ViewModels/MainViewModel.cs      - 添加预删除管理逻辑
Views/MainPage.xaml              - UI 更新
Views/MainPage.xaml.cs           - 键盘和菜单事件处理
```

## 七、实现步骤

### 步骤 1：数据模型修改
1. 修改 `ImageFileInfo.cs` 添加 `IsPendingDelete` 属性
2. 修改 `MainViewModel.cs` 添加预删除计数和管理方法

### 步骤 2：UI 基础修改
1. 修改 `MainPage.xaml` 添加预删除标记显示
2. 添加标题栏预删除计数显示

### 步骤 3：键盘交互
1. 修改 `MainPage.xaml.cs` 添加 Delete 键处理

### 步骤 4：删除按钮和菜单
1. 添加删除下拉按钮
2. 实现菜单选项

### 步骤 5：确认对话框
1. 创建 `DeleteConfirmDialog`
2. 实现进度显示

### 步骤 6：删除执行和界面更新
1. 实现文件删除逻辑
2. 实现增量界面更新

## 八、注意事项

1. **线程安全**：所有 UI 操作必须在 UI 线程执行
2. **文件访问**：删除前确保文件可访问，处理权限问题
3. **错误处理**：删除失败时显示错误信息，不中断整个流程
4. **撤销功能**：暂不实现，可作为后续增强
5. **回收站**：考虑是否移入回收站而非永久删除（建议移入回收站）
