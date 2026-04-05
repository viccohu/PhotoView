# 筛选功能 SplitButton 调整 - 实现方案

## 概述
将筛选按钮改为 SplitButton，文件类型筛选简化为【图片】【RAW】两个选项。

---

## 任务分解

### [ ] 任务 1：筛选按钮改为 SplitButton
- **优先级**: P0
- **依赖**: None
- **描述**:
  - 用 SplitButton 替换当前的 DropDownButton + ClearButton 组合
  - 左边：筛选图标按钮（点击清除筛选）
  - 右边：下拉箭头（点击打开 Flyout）
  - 有筛选时左边高亮（蓝色背景）
  - 点击左边按钮时清除所有筛选
- **成功标准**: SplitButton 功能完整
- **测试要求**:
  - `human-judgement` TR-1.1: 视觉效果美观
  - `programmatic` TR-1.2: 点击逻辑正确

### [ ] 任务 2：文件类型筛选简化为【图片】【RAW】
- **优先级**: P0
- **依赖**: None
- **描述**:
  - 修改 FilterViewModel，移除动态文件类型检测
  - 添加 `IsImageFilter` 和 `IsRawFilter` 属性
  - 定义 RAW 格式列表（.arw, .cr2, .nef, .orf, .pef, .raf, .rw2, .srw, .dng 等）
  - 修改筛选逻辑：
    - 【图片】= 非 RAW 格式
    - 【RAW】= RAW 格式
    - 都不选 = 全部
- **成功标准**: 文件类型筛选简化为两个选项
- **测试要求**:
  - `programmatic` TR-2.1: RAW 格式正确识别
  - `programmatic` TR-2.2: 筛选逻辑正确

### [ ] 任务 3：更新 FilterFlyout UI
- **优先级**: P0
- **依赖**: 任务 2
- **描述**:
  - 文件类型块：改为两个 ToggleButton【图片】和【RAW】
  - 保留"删除预选"按钮
- **成功标准**: UI 更新完成
- **测试要求**:
  - `human-judgement` TR-3.1: 布局美观

---

## 技术细节

### RAW 格式列表
```csharp
private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".arw", ".cr2", ".cr3", ".nef", ".nrw", ".orf", ".pef",
    ".raf", ".rw2", ".srw", ".dng", ".x3f", ".mos", ".mrw"
};
```

### FilterViewModel 修改
```csharp
[ObservableProperty]
private bool _isImageFilter;

[ObservableProperty]
private bool _isRawFilter;
```

### 筛选逻辑
```csharp
private bool MatchFileType(ImageFileInfo image)
{
    if (!Filter.IsImageFilter && !Filter.IsRawFilter)
        return true;
    
    if (image.ImageFile == null)
        return false;
    
    var ext = Path.GetExtension(image.ImageFile.Path).ToLowerInvariant();
    bool isRaw = RawExtensions.Contains(ext);
    
    if (Filter.IsImageFilter && Filter.IsRawFilter)
        return true;
    if (Filter.IsImageFilter)
        return !isRaw;
    if (Filter.IsRawFilter)
        return isRaw;
    
    return true;
}
```

### SplitButton XAML
```xml
<SplitButton x:Name="FilterSplitButton" Click="FilterSplitButton_Click">
    <SplitButton.Content>
        <FontIcon Glyph="&#xE71C;" FontFamily="{StaticResource SymbolThemeFontFamily}"/>
    </SplitButton.Content>
    <SplitButton.Flyout>
        <Flyout Placement="Bottom">
            <Flyout.FlyoutPresenterStyle>
                <Style TargetType="FlyoutPresenter">
                    <Setter Property="Padding" Value="16"/>
                </Style>
            </Flyout.FlyoutPresenterStyle>
            <local:FilterFlyout x:Name="FilterFlyoutControl"/>
        </Flyout>
    </SplitButton.Flyout>
</SplitButton>
```
