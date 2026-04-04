# 删除流程重构 - 实现计划

## [ ] 任务 1: 修改删除按钮（移除下拉菜单）
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 将 `DropDownButton` 改为普通 `Button`
  - 直接点击按钮触发删除对话框
- **Success Criteria**: 删除按钮变为单按钮，点击即打开对话框
- **Test Requirements**:
  - `programmatic` TR-1.1: XAML 中不再有 `DropDownButton`
  - `human-judgment` TR-1.2: UI 上只有一个删除按钮
- **Notes**: 修改 MainPage.xaml

## [ ] 任务 2: 重构 DeleteConfirmDialog
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 移除 DeleteType 枚举依赖
  - 添加 ToggleButton 面板展示文件类型
  - 添加"全部"选项
  - 根据预删除文件动态生成扩展名 ToggleButton
- **Success Criteria**: 对话框展示 ToggleButton 选择界面
- **Test Requirements**:
  - `programmatic` TR-2.1: 对话框中有 ToggleButtonsPanel
  - `programmatic` TR-2.2: 对话框有"全部" ToggleButton
  - `human-judgment` TR-2.3: 界面显示 [jpg][arw][全部] 样式
- **Notes**: 修改 DeleteConfirmDialog.xaml 和 DeleteConfirmDialog.xaml.cs

## [ ] 任务 3: 修改 DeleteConfirmDialog 代码逻辑
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 
  - 接收文件扩展名列表和文件列表
  - 实现 ToggleButton 点击事件
  - 实现"全部"按钮逻辑（点亮所有 ToggleButton）
  - 暴露 `SelectedExtensions` 属性
- **Success Criteria**: ToggleButton 可以正常选中/取消选中
- **Test Requirements**:
  - `programmatic` TR-3.1: 点击 ToggleButton 正确更新选中状态
  - `programmatic` TR-3.2: 点击"全部"正确点亮/取消所有按钮
  - `human-judgment` TR-3.3: UI 交互流畅
- **Notes**: 修改 DeleteConfirmDialog.xaml.cs

## [ ] 任务 4: 修改 MainPage 删除逻辑
- **Priority**: P0
- **Depends On**: 任务 1, 3
- **Description**: 
  - 修改删除按钮点击事件
  - 提取预删除文件中的所有扩展名
  - 传递给对话框
  - 根据对话框返回的 `SelectedExtensions` 过滤要删除的文件
- **Success Criteria**: 删除流程正常工作
- **Test Requirements**:
  - `programmatic` TR-4.1: 正确提取预删除文件的扩展名
  - `programmatic` TR-4.2: 根据 SelectedExtensions 正确过滤文件
  - `human-judgment` TR-4.3: 整个删除流程可以正常完成
- **Notes**: 修改 MainPage.xaml.cs

## [ ] 任务 5: 清理旧代码
- **Priority**: P1
- **Depends On**: 任务 4
- **Description**: 
  - 删除 DeleteType 枚举（不再需要）
  - 删除旧的 DeleteMenuItem_Click 方法
  - 删除不再需要的代码
- **Success Criteria**: 清理完成，无遗留无用代码
- **Test Requirements**:
  - `programmatic` TR-5.1: 项目可以成功编译
  - `programmatic` TR-5.2: 没有编译警告
- **Notes**: 清理相关代码

## 整体成功标准
- ✅ 删除按钮是单按钮，无下拉选项
- ✅ 对话框显示 [扩展名][扩展名][全部] 样式的 ToggleButton
- ✅ 用户可以选择要删除的文件类型
- ✅ 点击"全部"可以选中所有类型
- ✅ 删除功能正常工作
