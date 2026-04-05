# 筛选功能 - 实现方案

## 概述
在功能区添加筛选按钮，支持按文件类型和评级筛选图片。

---

## 任务分解

### [ ] 任务 1：创建筛选视图模型 (FilterViewModel)
- **优先级**: P0
- **依赖**: None
- **描述**:
  - 创建 `FilterViewModel` 类，使用 CommunityToolkit.Mvvm
  - 文件类型筛选：`ObservableCollection<string>` 存储当前类型，`HashSet<string>` 存储选中类型
  - 评级筛选：枚举或字符串表示模式（全部/有评级/无评级），条件（=、≥、≤），星级值
- **成功标准**:
  - 筛选状态正确管理
  - 支持双向绑定
- **测试要求**:
  - `programmatic` TR-1.1: 视图模型属性变更通知正常
  - `human-judgement` TR-1.2: 代码结构清晰易读

### [ ] 任务 2：修改 MainViewModel 支持筛选
- **优先级**: P0
- **依赖**: 任务 1
- **描述**:
  - 添加 `_allImages` 私有字段保存原始图片列表
  - 添加 `FilterViewModel` 实例
  - 添加 `ApplyFilter()` 方法，根据筛选条件更新 `Images`
  - 加载图片后调用 `RefreshAvailableFileTypes()` 刷新类型列表
- **成功标准**:
  - 原始列表正确保存
  - 筛选后正确更新显示
- **测试要求**:
  - `programmatic` TR-2.1: 筛选后 Images 集合正确更新
  - `programmatic` TR-2.2: 取消筛选恢复完整列表

### [ ] 任务 3：添加筛选按钮 UI
- **优先级**: P0
- **依赖**: None
- **描述**:
  - 在功能区添加 DropDownButton，使用筛选图标（&#xE71C;）
  - Flyout 使用 `FlyoutPresenter` 自定义样式，横向布局
- **成功标准**:
  - 按钮显示正确
  - Flyout 在按钮下方弹出
- **测试要求**:
  - `human-judgement` TR-3.1: UI 美观，与现有风格一致

### [ ] 任务 4：实现文件类型筛选 Flyout 内容
- **优先级**: P0
- **依赖**: 任务 1, 3
- **描述**:
  - 左侧面板：文件类型筛选
  - 动态生成 ToggleButton（全部 + 当前类型）
  - 实现交互逻辑：
    - 全部选中 → 取消所有单类型
    - 任意单类型选中 → 取消全部
    - 支持多选
    - 无选中 = 全部
- **成功标准**:
  - 类型按钮正确显示
  - 交互逻辑符合要求
- **测试要求**:
  - `human-judgement` TR-4.1: 按钮交互流畅
  - `programmatic` TR-4.2: 筛选逻辑正确

### [ ] 任务 5：实现评级筛选 Flyout 内容
- **优先级**: P0
- **依赖**: 任务 1, 3
- **描述**:
  - 右侧面板：评级筛选
  - 左边 ComboBox：全部 / 有评级 / 无评级
  - 右边：
    - ComboBox：=、≥、≤
    - RatingControl：1-5 星，支持清除
  - 实现联动：
    - 全部/无评级 → 右边禁用变灰
    - 有评级 → 右边启用，默认 ≥1 星
- **成功标准**:
  - 评级筛选 UI 正确
  - 联动逻辑符合要求
- **测试要求**:
  - `human-judgement` TR-5.1: 联动响应及时
  - `programmatic` TR-5.2: 筛选逻辑正确

### [ ] 任务 6：Flyout 布局与样式
- **优先级**: P0
- **依赖**: 任务 4, 5
- **描述**:
  - 横向布局，两个面板平级
  - 中间竖线分隔
  - 合适的间距和内边距
- **成功标准**:
  - 布局美观清晰
- **测试要求**:
  - `human-judgement` TR-6.1: 布局美观，符合设计要求

### [ ] 任务 7：集成与测试
- **优先级**: P0
- **依赖**: 任务 2, 4, 5, 6
- **描述**:
  - 筛选条件变更时调用 `ApplyFilter()`
  - 完整测试所有筛选组合
- **成功标准**:
  - 所有筛选功能正常工作
- **测试要求**:
  - `programmatic` TR-7.1: 文件类型 + 评级组合筛选正确
  - `human-judgement` TR-7.2: 整体体验流畅

---

## 技术细节

### FilterViewModel 属性
```csharp
// 文件类型
public ObservableCollection<string> AvailableFileTypes { get; }
public HashSet<string> SelectedFileTypes { get; }
public bool IsAllFileTypesSelected { get; set; }

// 评级筛选
public RatingFilterMode RatingMode { get; set; } // All, HasRating, NoRating
public RatingCondition RatingCondition { get; set; } // Equals, GreaterOrEqual, LessOrEqual
public int RatingStars { get; set; } // 0-5
```

### 筛选逻辑
```csharp
// 文件类型匹配
bool MatchFileType(ImageFileInfo image)
{
    if (IsAllFileTypesSelected || SelectedFileTypes.Count == 0)
        return true;
    
    var ext = Path.GetExtension(image.ImageFile.Path).ToLowerInvariant();
    return SelectedFileTypes.Contains(ext);
}

// 评级匹配
bool MatchRating(ImageFileInfo image)
{
    switch (RatingMode)
    {
        case RatingFilterMode.All:
            return true;
        case RatingFilterMode.NoRating:
            return image.Rating == 0;
        case RatingFilterMode.HasRating:
            var stars = ImageFileInfo.RatingToStars(image.Rating);
            switch (RatingCondition)
            {
                case RatingCondition.Equals:
                    return stars == RatingStars;
                case RatingCondition.GreaterOrEqual:
                    return stars >= RatingStars;
                case RatingCondition.LessOrEqual:
                    return stars <= RatingStars;
            }
            return false;
    }
}
```

### 布局结构
```xml
<DropDownButton>
    <DropDownButton.Flyout>
        <Flyout Placement="Bottom">
            <Flyout.FlyoutPresenterStyle>
                <Style TargetType="FlyoutPresenter">
                    <Setter Property="Padding" Value="12"/>
                </Style>
            </Flyout.FlyoutPresenterStyle>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                
                <!-- 文件类型筛选 -->
                <StackPanel Grid.Column="0">
                    <TextBlock Text="文件类型" FontWeight="SemiBold"/>
                    <ItemsControl x:Name="FileTypeButtonsPanel"/>
                </StackPanel>
                
                <!-- 分隔线 -->
                <Border Grid.Column="1" Width="1" Background="{ThemeResource CardStrokeColorDefaultBrush}"/>
                
                <!-- 评级筛选 -->
                <StackPanel Grid.Column="2">
                    <TextBlock Text="评级筛选" FontWeight="SemiBold"/>
                    <!-- 评级控件 -->
                </StackPanel>
            </Grid>
        </Flyout>
    </DropDownButton.Flyout>
</DropDownButton>
```
