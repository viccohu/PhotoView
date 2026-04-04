# 实现面包屑与 TreeView 双向同步

## 问题分析

当前状态：
- **面包屑 → TreeView**：点击面包屑项时会调用 `ExpandTreeViewPathAsync()` 同步 TreeView ✅
- **TreeView → 面包屑**：点击 TreeView 时会更新 `SelectedFolder`，面包屑自动更新 ✅
- **控制按钮 → 面包屑**：返回/向上/刷新按钮更新了 `SelectedFolder`，面包屑自动更新 ✅
- **控制按钮 → TreeView**：返回/向上/刷新按钮 **没有** 同步 TreeView 选中状态 ❌

问题根源：
- `GoBackAsync()`、`GoUpAsync()`、`RefreshAsync()` 方法只更新了 `SelectedFolder`
- 没有通知 MainPage 同步 TreeView 的选中状态和展开状态

## 解决方案

在 ViewModel 中添加 `SelectedFolderChanged` 事件，当 `SelectedFolder` 变化时通知 MainPage，MainPage 收到通知后同步 TreeView。

## 实施步骤

### 步骤 1：在 MainViewModel 中添加 SelectedFolderChanged 事件
- 添加 `public event EventHandler<FolderNode>? SelectedFolderChanged;`
- 在 `SelectedFolder` 属性的 setter 中触发事件

### 步骤 2：在 MainPage.xaml.cs 中订阅事件
- 在构造函数中订阅 `ViewModel.SelectedFolderChanged`
- 事件处理中调用 `ExpandTreeViewPathAsync()` 同步 TreeView

### 步骤 3：构建验证
运行 `dotnet build` 确保无错误
