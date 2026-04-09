
# 信息栏布局优化方案

## 1. 现状分析
当前信息栏位于 `ImageViewerControl.xaml`，使用以下控件展示信息：
- 文件名：使用 `TextBox`（只读模式）
- 评级：使用 `RatingControl`
- 日期/时间：分别使用 `DatePicker` 和 `TimePicker`
- 文件信息和设备信息：使用列表形式展示

## 2. 优化目标
根据用户需求和草图参考，优化信息栏布局，主要改进点：
- 文件名改为普通 TextBlock
- 日期时间整合显示，不使用官方控件
- 文件信息和设备信息按方块样式展示
- 添加交互效果（hover、点击、复制）

## 3. 具体修改方案

### 3.1 ViewModel 层修改（ImageViewerViewModel.cs）
- 添加新的属性 `FormattedDateTime` 用于整合日期和时间显示
- 确保所有需要复制的信息都有对应的 RelayCommand

### 3.2 View 层修改（ImageViewerControl.xaml）
1. **文件名区域**
   - 将 TextBox 替换为 TextBlock
   - 添加图标、hover 效果和点击复制功能

2. **评级区域**
   - 保持现有 RatingControl 不变
   - 添加图标

3. **日期时间区域**
   - 移除 DatePicker 和 TimePicker
   - 创建自定义布局整合日期和时间显示
   - 添加图标、hover 效果和点击复制功能

4. **文件信息区域**
   - 按草图中的方块样式重新布局
   - 使用网格布局排列信息块
   - 每个信息块有独立的图标、标题和值
   - 添加 hover 效果和点击复制功能

5. **设备信息区域**
   - 按草图中的方块样式重新布局
   - 使用网格布局排列信息块
   - 每个信息块有独立的图标、标题和值
   - 添加 hover 效果和点击复制功能

### 3.3 交互效果实现
- 使用 VisualStateManager 或 PointerEntered/PointerExited 事件实现 hover 效果
- 使用 Tapped 事件或 Command 实现点击复制功能
- 保持使用主题色，不自定义颜色

## 4. 修改文件列表
1. `ViewModels/ImageViewerViewModel.cs` - 添加整合日期时间属性
2. `Controls/ImageViewerControl.xaml` - 重构整个信息栏布局
3. `Controls/ImageViewerControl.xaml.cs` - 添加必要的事件处理（如需要）

## 5. 风险和注意事项
- 确保所有绑定正确更新
- 保持线程安全（UI 对象在 UI 线程创建）
- 不使用非 WinUI3 的属性或方法
- 保持响应式布局适应不同尺寸

