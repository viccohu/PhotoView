# TreeView 优化实现计划

## 需求分析

根据 `待优化.md` 中的要求，需要对 TreeView 进行以下优化：

| 序号 | 需求 | 说明 |
|------|------|------|
| 1 | 节流 | 多次点击相同节点，避免重复加载 |
| 2 | 缓存 | 避免重复加载已加载的节点 |
| 3 | 切换展开 | 在当前已选中的节点上再次点击实现切换展开/关闭 |
| 4 | 三角点击选中 | 点击三角展开节点=选中节点 |
| 5 | UI 布局调整 | 展开三角调整到右边，左边用图标标识文件夹或驱动器 |

---

## 实现步骤

### [ ] 任务 1：添加缓存机制（避免重复加载）
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 修改 `FolderNode` 模型，添加 `IsLoaded` 属性标识节点是否已加载
  - 修改 `MainViewModel.LoadChildrenAsync` 方法，检查 `IsLoaded` 标志
  - 已加载的节点直接返回，不再重新加载
- **Success Criteria**:
  - 节点只加载一次
  - `IsLoaded` 属性正确标识加载状态
- **Test Requirements**:
  - `programmatic` TR-1.1: `IsLoaded` 属性存在且可读写
  - `programmatic` TR-1.2: `LoadChildrenAsync` 检查 `IsLoaded` 标志

### [ ] 任务 2：添加节流机制（避免重复点击）
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**:
  - 在 `MainPage.xaml.cs` 中添加 `_lastClickedNode` 字段
  - 在 `FolderTreeView_ItemInvoked` 中检查是否为相同节点
  - 相同节点且正在加载时跳过
- **Success Criteria**:
  - 快速点击同一节点只触发一次加载
- **Test Requirements**:
  - `programmatic` TR-2.1: 相同节点连续点击只加载一次

### [ ] 任务 3：点击已选中节点切换展开/关闭
- **Priority**: P1
- **Depends On**: 任务 2
- **Description**:
  - 在 `FolderTreeView_ItemInvoked` 中检查是否为当前选中节点
  - 如果是选中节点，切换 `IsExpanded` 状态
  - 使用 `TreeViewItem` 的 `IsExpanded` 属性
- **Success Criteria**:
  - 点击已选中节点能切换展开/关闭状态
- **Test Requirements**:
  - `human-judgement` TR-3.1: 点击已选中节点能正常切换展开状态

### [ ] 任务 4：点击三角展开节点=选中节点
- **Priority**: P1
- **Depends On**: 任务 3
- **Description**:
  - 修改 `FolderTreeView_Expanding` 事件处理
  - 展开时同时选中该节点
  - 调用 `LoadImagesAsync` 加载图片
- **Success Criteria**:
  - 点击三角展开时自动选中节点
  - 同时加载该节点的图片
- **Test Requirements**:
  - `human-judgement` TR-4.1: 展开节点时自动选中并加载图片

### [ ] 任务 5：UI 布局调整（三角右移，图标左置）
- **Priority**: P2
- **Depends On**: 任务 4
- **Description**:
  - 修改 `TreeView.ItemTemplate`，自定义 `TreeViewItem` 模板
  - 添加图标显示（根据 `NodeType` 显示不同图标）
  - 调整布局：图标在左，文字在中间，展开三角在右
- **Success Criteria**:
  - 文件夹显示文件夹图标
  - 驱动器显示驱动器图标
  - 展开三角在右侧
- **Test Requirements**:
  - `human-judgement` TR-5.1: 图标正确显示
  - `human-judgement` TR-5.2: 展开三角在右侧

---

## 涉及文件

| 文件 | 修改类型 |
|------|---------|
| `Models/FolderNode.cs` | 添加 `IsLoaded` 属性 |
| `ViewModels/MainViewModel.cs` | 修改 `LoadChildrenAsync` 方法 |
| `Views/MainPage.xaml.cs` | 添加节流和切换展开逻辑 |
| `Views/MainPage.xaml` | 修改 TreeView 模板 |

---

## 技术细节

### 1. FolderNode 模型修改
```csharp
[ObservableProperty]
private bool _isLoaded;
```

### 2. LoadChildrenAsync 修改
```csharp
public async Task LoadChildrenAsync(FolderNode node)
{
    if (node.IsLoaded || !node.HasDummyChild || node.IsLoading)
        return;
    
    // ... 加载逻辑 ...
    
    node.IsLoaded = true;
}
```

### 3. ItemInvoked 修改
```csharp
private async void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
{
    if (args.InvokedItem is FolderNode node)
    {
        // 检查是否为当前选中节点
        if (FolderTreeView.SelectedItem == node)
        {
            // 切换展开状态
            if (args.InvokedItemContainer is TreeViewItem item)
            {
                item.IsExpanded = !item.IsExpanded;
            }
        }
        
        await ViewModel.LoadImagesAsync(node);
    }
}
```

### 4. Expanding 修改
```csharp
private async void FolderTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
{
    if (args.Item is FolderNode node)
    {
        // 展开时选中节点
        FolderTreeView.SelectedItem = node;
        await ViewModel.LoadChildrenAsync(node);
        await ViewModel.LoadImagesAsync(node);
    }
}
```

### 5. XAML 模板修改
```xml
<TreeView.ItemTemplate>
    <DataTemplate x:DataType="models:FolderNode">
        <TreeViewItem
            ItemsSource="{x:Bind Children}"
            FlowDirection="RightToLeft">
            <Grid FlowDirection="LeftToRight">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <FontIcon Grid.Column="0" Glyph="{x:Bind NodeType, Converter={StaticResource NodeTypeToIconConverter}}"/>
                <TextBlock Grid.Column="1" Text="{x:Bind Name}"/>
            </Grid>
        </TreeViewItem>
    </DataTemplate>
</TreeView.ItemTemplate>
```
