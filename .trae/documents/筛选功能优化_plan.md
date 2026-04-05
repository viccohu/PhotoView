# 筛选功能优化 - 实现方案

## 概述
修复和优化现有筛选功能，添加新特性。

---

## 任务分解

### [ ] 任务 1：修复文件类型回显问题
- **优先级**: P0
- **依赖**: None
- **描述**: 修复文件类型按钮只有"全部"显示的问题
- **成功标准**: 文件类型按钮正确显示当前文件夹中的所有类型
- **测试要求**:
  - `programmatic` TR-1.1: 可用文件类型正确加载
  - `human-judgement` TR-1.2: 按钮正确回显选中状态

### [ ] 任务 2：评级筛选模式改为 ToggleButton
- **优先级**: P0
- **依赖**: None
- **描述**: 将评级模式从 ComboBox 改为三个 ToggleButton（全部/有评级/无评级）
- **成功标准**: 三个 ToggleButton 互斥选择
- **测试要求**:
  - `human-judgement` TR-2.1: 按钮布局美观
  - `programmatic` TR-2.2: 互斥逻辑正确

### [ ] 任务 3：有星时自动重置为 ≥1 星
- **优先级**: P0
- **依赖**: 任务 2
- **描述**: 点击"有评级"时，自动重置条件为 ≥，星级为 1，并更新 RatingControl 视觉
- **成功标准**: 视觉和状态同步更新
- **测试要求**:
  - `programmatic` TR-3.1: 状态正确更新
  - `human-judgement` TR-3.2: RatingControl 视觉联动

### [ ] 任务 4：添加预删除筛选按钮
- **优先级**: P0
- **依赖**: None
- **描述**: 在文件类型块下方添加"删除预选"筛选按钮，点击后只显示预删除的文件
- **成功标准**: 预删除筛选功能正常
- **测试要求**:
  - `programmatic` TR-4.1: 只显示预删除文件
  - `human-judgement` TR-4.2: 按钮位置合适

### [ ] 任务 5：FilterViewModel 添加预删除筛选状态
- **优先级**: P0
- **依赖**: None
- **描述**: 在 FilterViewModel 中添加 IsPendingDeleteFilter 属性
- **成功标准**: 筛选逻辑包含预删除条件
- **测试要求**:
  - `programmatic` TR-5.1: 筛选逻辑正确

### [ ] 任务 6：主按钮高亮和清除筛选功能
- **优先级**: P0
- **依赖**: 任务 5
- **描述**: 
  - 有筛选条件时筛选按钮高亮
  - 添加小叉按钮，点击后清除所有筛选（重置 FilterViewModel）
- **成功标准**: 高亮和清除功能正常
- **测试要求**:
  - `human-judgement` TR-6.1: 高亮视觉明显
  - `programmatic` TR-6.2: 清除后恢复全部显示

---

## 技术细节

### FilterViewModel 新增
```csharp
[ObservableProperty]
private bool _isPendingDeleteFilter;
```

### MatchFilter 逻辑更新
```csharp
private bool MatchFilter(ImageFileInfo image)
{
    return MatchFileType(image) && MatchRating(image) && MatchPendingDelete(image);
}

private bool MatchPendingDelete(ImageFileInfo image)
{
    if (!Filter.IsPendingDeleteFilter)
        return true;
    return image.IsPendingDelete;
}
```

### UI 更新
- 评级模式：三个 ToggleButton 水平排列
- 文件类型下方：添加"删除预选" ToggleButton
- 主按钮：根据筛选状态设置高亮样式
- 小叉按钮：在筛选按钮旁边或 Flyout 内
