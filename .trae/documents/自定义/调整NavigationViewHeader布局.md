# 调整 NavigationView HeaderTemplate 布局计划

## 目标
将 `NavigationView.HeaderTemplate`（当前占用整行）改为左右两栏布局：
- **左侧**：保留现有的标题 TextBlock（缩小宽度，不再撑满整行）
- **右侧**：新增一个容器区域，用于放置功能按钮

## 当前结构分析

当前 `ShellPage.xaml` 第 61-69 行的 `HeaderTemplate`：
```xml
<NavigationView.HeaderTemplate>
    <DataTemplate>
        <Grid>
            <TextBlock Text="{Binding}" Style="{ThemeResource TitleTextBlockStyle}" />
        </Grid>
    </DataTemplate>
</NavigationView.HeaderTemplate>
```
- 内部只有一个 `Grid` 包裹一个 `TextBlock`，Grid 默认会撑满整行宽度

## 修改步骤

### 步骤 1：修改 HeaderTemplate 内的 Grid 布局
将 `HeaderTemplate` 中的 `Grid` 改为两列布局：
- **第一列**：`Width="Auto"`，放置现有的 TextBlock
- **第二列**：`Width="*"`，放置新的功能按钮容器（`StackPanel`，水平排列，右对齐）

修改后的结构：
```xml
<NavigationView.HeaderTemplate>
    <DataTemplate>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock
                Grid.Column="0"
                Text="{Binding}"
                Style="{ThemeResource TitleTextBlockStyle}" />
            <StackPanel
                Grid.Column="1"
                x:Name="HeaderActionButtons"
                Orientation="Horizontal"
                HorizontalAlignment="Right"
                VerticalAlignment="Center"
                Spacing="8">
            </StackPanel>
        </Grid>
    </DataTemplate>
</NavigationView.HeaderTemplate>
```

### 涉及文件
- `d:\PhotoView\PhotoView\Views\ShellPage.xaml`（第 61-69 行）

### 注意事项
- `DataTemplate` 内的 `x:Name` 无法直接在 code-behind 中通过名称引用（因为 DataTemplate 有自己的名称作用域），如果后续需要在代码中访问 `HeaderActionButtons`，需要通过 `VisualTreeHelper` 遍历或使用其他方式
- 如果需要从 ViewModel 绑定按钮命令，可以直接在 StackPanel 内添加按钮并通过绑定实现
