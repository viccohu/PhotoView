
# 信息栏优化 - Skeleton 加载动画和 None 值显示

## 1. 现状分析
- 已有 `IsLoadingExif` 属性指示加载状态
- 信息项目前隐藏显示，需要改为固定显示
- 空值需要显示为 "None"
- 加载时需要 Skeleton 骨架动画

## 2. Skeleton/Shimmer 实现选项
根据搜索结果，有以下选项：
1. **自定义 Shimmer**：创建自定义控件，不依赖额外包
2. **WinUIEssential.WinUI3**：已有 Shimmer 控件，但需要添加新包（已选择）
3. **CommunityToolkit Labs**：实验性质的 Shimmer，需要添加新包

**已选择**：WinUIEssential.WinUI3

## 3. 具体修改方案

### 3.1 添加 NuGet 包
- 添加 `WinUIEssential.WinUI3` 包

### 3.2 ViewModel 层修改
- 修改所有字符串属性的默认值：从空字符串改为 "None"
- 保持 IsLoadingExif 不变

### 3.3 XAML 层修改
- 所有信息框固定显示，不使用 Visibility 绑定
- 空值显示 "None"
- 使用 Shimmer 控件包裹需要加载动画的内容
- 绑定 IsLoading 属性到 Shimmer 控件的 IsLoading
- 日期显示需要检查并修复

## 4. 修改文件列表
1. `PhotoView.csproj` - 添加 WinUIEssential.WinUI3 包引用
2. `ViewModels/ImageViewerViewModel.cs` - 修改默认值
3. `Controls/ImageViewerControl.xaml` - 使用 Shimmer 控件并修改显示逻辑

## 5. 风险和注意事项
- 保持线程安全
- 不引入新依赖（除非用户选择）
- 保持主题色适配
- 确保动画流畅

