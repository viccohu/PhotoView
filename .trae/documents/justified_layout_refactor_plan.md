# 缩略图预览重构计划 - JustifiedLayout 实现

## 目标
将当前的 GridView + ItemsWrapGrid 布局重构为 ItemsRepeater + 自定义 JustifiedLayout，实现类似 Win11 照片应用的"两端对齐等高布局"，并支持图片多选功能。

## 当前状态分析

### 现有实现
- 使用 `GridView` + `ItemsWrapGrid` 显示图片
- 图片高度固定为 200px，宽度通过 `AutoWidth` 计算
- `ImageFileInfo` 类有 `Width` 和 `Height` 属性，但没有 `AspectRatio` 属性

### 目标效果
- 每行高度一致（可配置）
- 每行图片宽度自适应（按比例伸缩）
- 行宽刚好填满容器（无空隙）
- 图片不被裁剪（保持比例）
- 支持图片多选功能

---

## 实施计划

### [x] 任务 1：添加 AspectRatio 属性到 ImageFileInfo
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 在 `ImageFileInfo` 类中添加 `AspectRatio` 属性
  - 计算公式：`Width / (Height + 1)`（避免除零）
- **成功标准**：`AspectRatio` 属性可正常访问
- **测试要求**：
  - `programmatic` TR-1.1：属性返回正确的宽高比值
  - `human-judgment` TR-1.2：代码风格与现有属性一致

### [x] 任务 2：创建 JustifiedLayout 基础类
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 创建 `Layouts/JustifiedLayout.cs` 文件
  - 定义 `LineHeight` 和 `Spacing` 依赖属性
  - 实现基础的 `MeasureOverride` 和 `ArrangeOverride` 方法框架
- **成功标准**：JustifiedLayout 类可被 XAML 引用
- **测试要求**：
  - `programmatic` TR-2.1：类编译无错误
  - `human-judgment` TR-2.2：类结构清晰，符合 WinUI 3 规范

### [x] 任务 3：实现行计算算法
- **优先级**：P0
- **依赖**：任务 2
- **描述**：
  - 实现 `BuildRows` 方法，计算每行包含哪些图片
  - 核心逻辑：
    1. 累加图片宽度直到接近容器宽度
    2. 计算缩放比例使整行填满容器
    3. 最后一行特殊处理（不强制拉伸）
  - 定义 `Row` 内部类存储行数据
- **成功标准**：行计算算法正确工作
- **测试要求**：
  - `programmatic` TR-3.1：不同图片比例组合下行计算正确
  - `human-judgment` TR-3.2：布局效果接近 Win11 照片应用

### [x] 任务 4：实现 MeasureOverride
- **优先级**：P0
- **依赖**：任务 3
- **描述**：
  - 调用 `BuildRows` 计算行数据
  - 测量每个元素的大小
  - 返回总大小（宽度 + 总高度）
- **成功标准**：正确计算布局所需空间
- **测试要求**：
  - `programmatic` TR-4.1：返回的 Size 正确反映内容大小
  - `human-judgment` TR-4.2：滚动条范围正确

### [x] 任务 5：实现 ArrangeOverride
- **优先级**：P0
- **依赖**：任务 4
- **描述**：
  - 遍历行数据，摆放每个元素
  - 计算每个元素的位置和大小
  - 处理间距
- **成功标准**：元素正确排列
- **测试要求**：
  - `programmatic` TR-5.1：元素位置计算正确
  - `human-judgment` TR-5.2：视觉效果符合预期

### [x] 任务 6：添加行数据缓存
- **优先级**：P1
- **依赖**：任务 5
- **描述**：
  - 添加 `_rowsCache` 和 `_lastWidth` 字段
  - 只在宽度变化时重新计算行数据
  - 优化性能
- **成功标准**：避免不必要的重复计算
- **测试要求**：
  - `programmatic` TR-6.1：窗口大小不变时不重新计算
  - `human-judgment` TR-6.2：布局响应流畅

### [x] 任务 7：升级为 VirtualizingLayout
- **优先级**：P1
- **依赖**：任务 6
- **描述**：
  - 将 `JustifiedLayout` 改为继承 `VirtualizingLayout`
  - 使用 `VirtualizingLayoutContext` 获取可见区域
  - 只实现可见元素的测量和排列
  - 实现 `InitializeForContextCore` 和 `UninitializeForContextCore`
- **成功标准**：支持虚拟化，大量图片时性能良好
- **测试要求**：
  - `programmatic` TR-7.1：滚动时只渲染可见元素
  - `programmatic` TR-7.2：1000+ 图片时内存占用合理
  - `human-judgment` TR-7.3：滚动流畅无卡顿

### [x] 任务 8：创建图片项模板
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 创建 `DataTemplate` 用于 ItemsRepeater
  - 包含图片显示、圆角边框、右键菜单
  - 添加选中状态视觉反馈
- **成功标准**：图片项模板可复用
- **测试要求**：
  - `programmatic` TR-8.1：模板正确绑定数据
  - `human-judgment` TR-8.2：视觉效果与原 GridView 一致

### [x] 任务 9：实现多选功能
- **优先级**：P1
- **依赖**：任务 8
- **描述**：
  - 创建 `ImageSelectionService` 服务管理选中状态
  - 实现单击选择、Ctrl+单击多选、Shift+单击范围选择
  - 添加选中状态的视觉样式（边框高亮、勾选标记）
  - 在 ViewModel 中添加 `SelectedImages` 集合
- **成功标准**：支持多种选择模式
- **测试要求**：
  - `programmatic` TR-9.1：单击选择单个图片
  - `programmatic` TR-9.2：Ctrl+单击添加/移除选择
  - `programmatic` TR-9.3：Shift+单击范围选择
  - `human-judgment` TR-9.4：选中状态视觉反馈明显

### [x] 任务 10：更新 MainPage.xaml
- **优先级**：P0
- **依赖**：任务 5, 任务 8
- **描述**：
  - 移除现有的 `GridView` 控件
  - 添加 `ItemsRepeater` 控件
  - 配置 `JustifiedLayout` 作为布局
  - 绑定图片数据源
  - 添加 `ScrollView` 包裹（如果需要）
- **成功标准**：页面正常显示图片
- **测试要求**：
  - `programmatic` TR-10.1：页面加载无错误
  - `human-judgment` TR-10.2：布局效果符合预期

### [ ] 任务 11：更新 MainPage.xaml.cs
- **优先级**：P0
- **依赖**：任务 10
- **描述**：
  - 移除 GridView 相关的事件处理代码
  - 添加 ItemsRepeater 相关的事件处理
  - 实现图片项的交互逻辑（点击、右键菜单等）
- **成功标准**：交互功能正常
- **测试要求**：
  - `programmatic` TR-11.1：右键菜单正常工作
  - `human-judgment` TR-11.2：点击响应正确

### [x] 任务 12：集成多选功能
- **优先级**：P1
- **依赖**：任务 9, 任务 11
- **描述**：
  - 在 MainPage 中集成 `ImageSelectionService`
  - 实现选择相关的键盘快捷键（Ctrl+A 全选等）
  - 更新顶部工具栏显示选中数量
- **成功标准**：多选功能完整可用
- **测试要求**：
  - `programmatic` TR-12.1：Ctrl+A 全选所有图片
  - `programmatic` TR-12.2：Esc 取消选择
  - `human-judgment` TR-12.3：工具栏正确显示选中数量

### [x] 任务 13：测试和优化
- **优先级**：P0
- **依赖**：任务 12
- **描述**：
  - 测试不同数量图片的性能
  - 测试不同窗口大小的布局响应
  - 测试不同图片比例的布局效果
  - 修复发现的问题
- **成功标准**：功能完整，性能良好
- **测试要求**：
  - `programmatic` TR-13.1：100+ 图片加载时间 < 1秒
  - `programmatic` TR-13.2：窗口调整大小时布局响应 < 100ms
  - `human-judgment` TR-13.3：布局效果接近 Win11 照片应用

---

## 文件结构

重构后的文件结构：

```
PhotoView/
├── Layouts/
│   └── JustifiedLayout.cs          # 自定义布局
├── Models/
│   └── ImageFileInfo.cs            # 添加 AspectRatio 属性
├── Services/
│   └── ImageSelectionService.cs    # 多选服务（新增）
├── ViewModels/
│   └── MainViewModel.cs            # 更新选中状态管理
├── Views/
│   ├── MainPage.xaml               # 使用 ItemsRepeater
│   └── MainPage.xaml.cs            # 更新交互逻辑
└── Styles/
    └── ImageItemStyles.xaml        # 图片项样式（新增，可选）
```

---

## 技术要点

### JustifiedLayout 核心算法

```
1. 遍历图片，累加宽度：sum(width) = sum(aspectRatio * lineHeight)
2. 当累加宽度接近容器宽度时，形成一行
3. 计算缩放比例：scale = containerWidth / currentRowWidth
4. 调整行高：finalHeight = lineHeight * scale
5. 最后一行不强制拉伸，保持原比例
```

### 多选实现要点

```
1. 使用 SelectionService 管理选中状态
2. 使用 Attached Property 或 Behavior 实现选择交互
3. 使用 Visual State 管理选中状态的视觉效果
```

---

## 风险评估

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 虚拟化布局实现复杂 | 高 | 分阶段实现，先完成基础布局 |
| 多选功能与布局冲突 | 中 | 使用独立的服务管理选中状态 |
| 性能问题 | 中 | 使用缓存和虚拟化优化 |
| 兼容性问题 | 低 | 充分测试不同场景 |

---

## 预期结果

1. 布局效果接近 Win11 照片应用
2. 支持 1000+ 图片流畅显示
3. 支持多种选择模式
4. 代码结构清晰，易于维护和扩展
