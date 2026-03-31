# 缩略图大小调整功能计划

## 需求分析

### 功能要求
1. **UI 位置**：右上角工具栏，下拉菜单形式
2. **按钮图标**：使用 Segoe MDL2 字体图标 `eca5`（ViewAll 或类似图标）
3. **尺寸选项**：
   | 选项 | 高度 | 缩略图尺寸 |
   |------|------|------------|
   | 小 | 128px | 128 |
   | 中 | 256px | 256 |
   | 大 | 512px | 512 |
4. **智能匹配**：系统缩略图尺寸自动匹配最接近的预设尺寸

### 当前状态
- `ThumbnailSize` 枚举：Small=96, Medium=200, Large=400
- `JustifiedLayout.LineHeight`：固定为 200
- `ThumbnailConverter`：通过 ConverterParameter 传入尺寸

---

## 实施计划

### [x] 任务 1：更新 ThumbnailSize 枚举
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 更新枚举值：Small=128, Medium=256, Large=512
- **成功标准**：枚举值与需求一致
- **测试要求**：
  - `programmatic` TR-1.1：枚举编译无错误

### [x] 任务 2：在 MainViewModel 中添加缩略图大小属性
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 添加 `ThumbnailSize` 属性（ThumbnailSize 类型）
  - 添加 `ThumbnailHeight` 属性（double 类型，用于绑定布局高度）
  - 添加 `ThumbnailSizeChanged` 事件
  - 默认值为 Medium (256px)
- **成功标准**：属性可被 UI 绑定
- **测试要求**：
  - `programmatic` TR-2.1：属性变更时触发事件

### [x] 任务 3：更新 MainPage.xaml 添加缩略图大小切换按钮
- **优先级**：P0
- **依赖**：任务 2
- **描述**：
  - 在右上角 StackPanel 中添加 DropDownButton
  - 使用 Segoe MDL2 字体图标 `&#xeca5;`
  - 下拉菜单包含：小、中、大 三个选项
  - 绑定到 ViewModel 的 ThumbnailSize 属性
- **成功标准**：按钮可正常显示和交互
- **测试要求**：
  - `human-judgment` TR-3.1：图标显示正确
  - `programmatic` TR-3.2：点击选项可切换大小

### [x] 任务 4：绑定 JustifiedLayout 的 LineHeight
- **优先级**：P0
- **依赖**：任务 2
- **描述**：
  - 将 JustifiedLayout 的 LineHeight 绑定到 ViewModel.ThumbnailHeight
- **成功标准**：布局高度随设置变化
- **测试要求**：
  - `programmatic` TR-4.1：切换大小时布局重新计算

### [x] 任务 5：更新 ThumbnailConverter 支持动态尺寸
- **优先级**：P0
- **依赖**：任务 2
- **描述**：
  - 修改 ThumbnailConverter 绑定方式
  - 使用 ViewModel 的 ThumbnailSize 属性值
- **成功标准**：缩略图尺寸随设置变化
- **测试要求**：
  - `programmatic` TR-5.1：切换大小后缩略图重新加载

### [x] 任务 6：智能匹配缩略图尺寸
- **优先级**：P1
- **依赖**：任务 5
- **描述**：
  - 更新 ThumbnailConverter 或 ImageFileInfo
  - 根据请求尺寸智能选择最接近的系统缩略图
  - Windows 系统缩略图标准尺寸：96, 160, 256, 512, 1024
- **成功标准**：请求 128px 时获取 160px，请求 256px 时获取 256px
- **测试要求**：
  - `programmatic` TR-6.1：不同尺寸请求返回合适的缩略图

---

## 技术要点

### Segoe MDL2 字体图标使用
```xml
<FontIcon Glyph="&#xeca5;" FontFamily="Segoe MDL2 Assets"/>
```

### DropDownButton 实现
```xml
<DropDownButton>
    <DropDownButton.Content>
        <FontIcon Glyph="&#xeca5;" FontFamily="Segoe MDL2 Assets"/>
    </DropDownButton.Content>
    <DropDownButton.Flyout>
        <MenuFlyout>
            <MenuFlyoutItem Text="小" Tag="Small" Click="ThumbnailSize_Click"/>
            <MenuFlyoutItem Text="中" Tag="Medium" Click="ThumbnailSize_Click"/>
            <MenuFlyoutItem Text="大" Tag="Large" Click="ThumbnailSize_Click"/>
        </MenuFlyout>
    </DropDownButton.Flyout>
</DropDownButton>
```

### 智能匹配缩略图尺寸
```csharp
private uint GetOptimalThumbnailSize(uint requestedSize)
{
    uint[] systemSizes = { 96, 160, 256, 512, 1024 };
    return systemSizes.FirstOrDefault(s => s >= requestedSize, systemSizes.Last());
}
```

---

## 预期结果
1. 右上角有缩略图大小切换按钮
2. 点击下拉菜单可选择大中小三种尺寸
3. 切换后布局和缩略图都会相应调整
4. 系统缩略图智能匹配最接近的尺寸
