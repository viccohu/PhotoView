# NavigationPane 重构计划

## 一、当前问题根因

### 1.1 为什么在窗口内不崩溃，放到导航栏就崩溃？

**核心区别：控件生命周期**

| | 窗口内控件 | 导航栏控件 |
|---|---|---|
| 父容器 | 稳定的Grid/Page | NavigationViewItem（受NavigationView管理） |
| 生命周期 | 创建一次，永不销毁 | 随导航栏折叠/展开反复销毁/重建 |
| 数据源 | 始终绑定 | Context反复设为null再设回 |

**崩溃因果链**：
```
导航栏折叠 → Context = null → DataContext = null → {Binding RootNodes} = null
→ TreeView销毁所有项 → 内部状态不一致 → 0xC0000005 访问违规
```

**状态丢失因果链**：
```
TreeView销毁项 → Collapsed事件触发 → node.IsExpanded = false
→ 导航栏展开 → Context = context → TreeView重建项 → IsExpanded绑定读到false → 全部折叠
```

### 1.2 架构问题

当前 `NavigationPaneExplorer` 同时承担了 4 种职责：
1. **UI渲染** — TreeView、按钮、进度条
2. **状态管理** — IsExpanded、SelectedNode 同步
3. **IO操作** — 触发 ExpandNodeAsync 加载子目录
4. **同步逻辑** — SyncSelectedNodeAsync 沿父链展开，展开=选中互相触发

**问题**：展开节点 = 设置选中节点（`FolderTreeView_Expanding` 中 `Context.SelectedNode = node`），这导致：
- 展开 → SelectedNode 变化 → PropertyChanged → SyncSelectedNodeAsync → 又操作 TreeView
- 循环触发 + 异步操作 + 版本校验 = 复杂且脆弱

### 1.3 两个实例问题

ShellPage 中有两个 `NavigationPaneExplorer`：
- `NavigationPaneHost`（展开态，嵌在 NavigationViewItem 里）
- `_compactNavigationPaneHost`（折叠态，嵌在 Flyout 里）

切换时 Context 从一个设为 null 再设给另一个，导致 TreeView 反复销毁/重建。

---

## 二、重构目标

1. **TreeView 只存在一份**，放在 ShellPage 层级，不随导航栏折叠/展开销毁
2. **状态与 UI 分离**，FolderNode.IsExpanded 是唯一状态源，TreeView 只读取不写入
3. **展开 ≠ 选中**，去耦这两个操作
4. **载入区拆分**，做成页面独立控件，操作按钮放到 ShellToolbar

---

## 三、重构方案

### 3.1 TreeView 单实例 + 全局状态

**核心思路**：将 NavigationPaneExplorer 拆成两部分：
- `FolderTreePane` — 纯 TreeView，只负责展示和用户交互
- `SourceListPane` — 载入区列表 + 操作按钮（CollectPage 专属）

ShellPage 中只放一个 `FolderTreePane`，始终存在，不随导航栏折叠/展开销毁。

#### 3.1.1 新建 FolderTreePane 控件

**职责**：纯 TreeView 展示 + 用户交互事件转发

```
FolderTreePane (UserControl)
├── TreeView (文件夹树)
└── 事件:
    ├── NodeExpanding → 通知 ViewModel 加载子节点
    ├── NodeCollapsed → 更新 FolderNode.IsExpanded
    ├── NodeClicked → 通知 ViewModel 选中节点
    ├── NodeDoubleClicked → 通知 ViewModel 激活节点
    └── NodeRightClicked → 弹出右键菜单
```

**关键设计**：
- `IsExpanded="{x:Bind IsExpanded, Mode=OneWay}"` — 只读绑定，TreeView 不写入模型
- `Expanding` 事件中：先设 `node.IsExpanded = true`，再通知 ViewModel 加载
- `Collapsed` 事件中：检查 DataContext != null 才设 `node.IsExpanded = false`
- **移除 SyncSelectedNodeAsync** — 不再从 ViewModel→TreeView 反向同步选中
- **移除展开=选中** — Expanding 事件不再设置 SelectedNode

#### 3.1.2 ShellPage 中单实例布局

```xml
<NavigationView>
    <NavigationView.MenuItems>
        <!-- 页面导航项 -->
    </NavigationView.MenuItems>
    <NavigationView.PaneFooter>
        <!-- 折叠态：显示图标按钮，点击弹出 Flyout -->
    </NavigationView.PaneFooter>
    <Grid>
        <!-- 左侧面板：FolderTreePane，始终存在 -->
        <controls:FolderTreePane x:Name="FolderTreePaneHost"
            Visibility="{x:Bind IsTreePaneVisible, Mode=OneWay}" />
        <!-- 右侧：Frame -->
        <Frame x:Name="NavigationFrame" />
    </Grid>
</NavigationView>
```

**但这个方案有问题**：NavigationView 的内容区域不支持左右分栏布局，Tree 应该在导航面板内。

**修正方案**：保持 TreeView 在 NavigationViewItem 内，但**不再设 Context 为 null**：

```csharp
// 之前（崩溃源）：
NavigationPaneHost.Context = isPaneOpen ? context : null;

// 之后（安全）：
NavigationPaneHost.Context = context;  // 始终保持
// 用 Visibility 控制显隐，不销毁 TreeView 的项
```

同时**移除第二个实例** `_compactNavigationPaneHost`，折叠态改为弹出同一个控件的 Flyout。

### 3.2 去耦：展开 ≠ 选中

当前问题：
```csharp
// FolderTreeView_Expanding 中：
node.IsExpanded = true;
await Context.ExpandNodeAsync(node);
Context.SelectedNode = node;  // ← 这行导致展开=选中，触发循环
```

修改：
```csharp
// FolderTreeView_Expanding 中：
node.IsExpanded = true;
await Context.ExpandNodeAsync(node);
// 移除：Context.SelectedNode = node;
```

选中节点只在用户**主动点击**时触发（ItemInvoked 事件）。

### 3.3 移除 SyncSelectedNodeAsync

当前 `SyncSelectedNodeAsync` 的问题：
- 从 ViewModel → TreeView 反向同步选中
- 需要沿父链逐级展开（异步操作）
- 操作 TreeView.SelectedItem 和 ContainerFromItem（崩溃源）

**替代方案**：用 `FolderNode.IsSelected` 属性 + 绑定驱动

```csharp
// FolderNode 新增：
[ObservableProperty]
private bool _isSelected;

// XAML 中：
<TreeViewItem IsSelected="{x:Bind IsSelected, Mode=OneWay}" />
```

这样选中状态完全由数据模型驱动，不需要反向操作 TreeView。

### 3.4 载入区拆分

将 `NavigationPaneExplorer` 中的 Row1（SourceItems 列表）和 Row2（操作按钮）拆分出去：

**方案**：载入区做成 CollectPage 的独立控件，放在 ShellToolbar 中

```
ShellToolbar (已有)
├── 导出按钮
├── 删除按钮
├── 筛选按钮
└── 载入区按钮 (新增)
    ├── SplitButton: 显示已载入数量
    └── Flyout: 载入区列表 + "载入"按钮 + "包含子文件夹"开关
```

**好处**：
- 载入区操作始终可见，不依赖导航栏展开/折叠
- NavigationPaneExplorer 只剩 TreeView，职责单一
- 符合用户操作习惯：功能按钮在工具栏

### 3.5 简化 NavigationPaneContext

拆分后，`INavigationPaneContext` 接口大幅简化：

```csharp
public interface INavigationPaneContext
{
    string Title { get; }
    string? Subtitle { get; }
    ObservableCollection<FolderNode> RootNodes { get; }
    FolderNode? SelectedNode { get; set; }
    bool ActivateOnSingleClick { get; }
    bool ActivateOnDoubleTap { get; }
    Task ExpandNodeAsync(FolderNode node);
    Task SelectNodeAsync(FolderNode node);
    Task ActivateNodeAsync(FolderNode node);
    Task ActivateNodeSecondaryAsync(FolderNode node);
    IReadOnlyList<NavigationPaneNodeAction> GetNodeActions(FolderNode node);
}
```

移除的成员（移到 CollectPage 的载入区控件）：
- `SourceItems` / `HasSourceItems`
- `PrimaryAction` / `HasPrimaryAction`
- `ToggleOptionText` / `IsToggleOptionVisible` / `ToggleOptionValue`
- `IsProgressVisible` / `IsProgressIndeterminate` / `ProgressValue`
- `StatusText` / `HasStatusText`
- `RemoveSourceAsync` / `ExecutePrimaryActionAsync` / `ToggleOptionAsync`

---

## 四、实施步骤

### 步骤1：FolderNode 新增 IsSelected 属性
- 在 `FolderNode.cs` 中添加 `[ObservableProperty] private bool _isSelected;`
- 为 TreeViewItem 添加 `IsSelected="{x:Bind IsSelected, Mode=OneWay}"` 绑定

### 步骤2：简化 NavigationPaneExplorer
- 移除 Row1（SourceItems 列表）和 Row2（操作按钮区）的 XAML
- 只保留 Row0（标题）和 Row3（TreeView）
- 移除相关事件处理：RemoveSourceButton_Click, PrimaryActionButton_Click, ToggleOptionButton_CheckedChanged
- 移除 SyncSelectedNodeAsync 方法
- 移除 FolderTreeView_Expanding 中的 `Context.SelectedNode = node`
- FolderTreeView_Collapsed 中保留 DataContext == null 检查

### 步骤3：简化 INavigationPaneContext 和 NavigationPaneContext
- 移除载入区相关的属性和方法
- 保留 TreeView 相关的核心接口

### 步骤4：ShellPage 单实例化
- 移除 `_compactNavigationPaneHost` 第二个实例
- `NavigationPaneHost.Context = context`（始终不设 null）
- 折叠态改为弹出包含 NavigationPaneHost 的 Flyout，或用其他方式
- 移除 `ExpandedNavigationPaneItem.Content` 的动态设置

### 步骤5：创建 CollectSourcePane 控件（载入区）
- 新建 `CollectSourcePane` UserControl
- 包含：载入区列表 + "载入"按钮 + "包含子文件夹"开关 + 进度条
- 数据绑定到 CollectViewModel
- 放在 ShellToolbar 中作为 SplitButton 的 Flyout 内容

### 步骤6：修改 CollectPage
- 移除 `CreateNavigationPaneContext` 中载入区相关的 Handler
- 创建 `CollectSourcePane` 实例并注册到 ShellToolbar
- 简化 `UpdateNavigationPaneContextState` 只同步标题和树相关状态

### 步骤7：验证和测试
- 构建验证
- 测试导航栏折叠/展开 TreeView 状态保持
- 测试页面切换 TreeView 状态保持
- 测试载入区功能正常
- 确认无崩溃

---

## 五、风险和注意事项

1. **IsSelected 绑定**：WinUI3 TreeView 的 `IsSelected` 属性可能不支持 x:Bind 绑定，需要验证。如果不支持，保留 SelectedItem 的代码设置方式，但添加安全检查。

2. **Flyout 中放 NavigationPaneHost**：将控件从 NavigationViewItem 移到 Flyout 可能导致布局问题。需要测试 Flyout 打开时控件是否正确渲染。

3. **ShellToolbar 空间**：载入区按钮加入 ShellToolbar 后，如果按钮太多可能溢出。需要考虑响应式布局。

4. **向后兼容**：如果其他页面（如 MainPage）未来也需要 TreeView，需要确保接口设计可扩展。
