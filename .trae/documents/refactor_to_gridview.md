# 重构缩略图展示 - 使用 GridView 替代 ItemsView

## 一、重构目标

1. **使用 GridView** 替代 ItemsView + LinedFlowLayout
2. **保留大中小尺寸切换** - Small(120), Medium(256), Large(512)
3. **保留分批加载** - 使用 ContainerContentChanging 事件
4. **切换目录图片快速展示** - 清空数据源后立即绑定
5. **无 Bug** - GridView 是成熟稳定的控件
6. **清理旧代码** - 移除 ItemsView 相关代码

## 二、XAML 修改

### 修改前 (ItemsView + LinedFlowLayout)
```xml
<ItemsView x:Name="ImageItemsView" ...>
    <ItemsView.Layout>
        <LinedFlowLayout x:Name="ImageLinedFlowLayout" ... />
    </ItemsView.Layout>
    <ItemsView.ItemTemplate>
        <DataTemplate>
            <ItemContainer ...>
                ...
            </ItemContainer>
        </DataTemplate>
    </ItemsView.ItemTemplate>
</ItemsView>
```

### 修改后 (GridView)
```xml
<GridView
    x:Name="ImageGridView"
    Grid.Column="1"
    Grid.Row="2"
    ItemsSource="{x:Bind ViewModel.Images}"
    SelectionMode="Extended"
    SelectionChanged="ImageGridView_SelectionChanged"
    ContainerContentChanging="ImageGridView_ContainerContentChanging"
    Margin="12,4,12,0"
    Padding="4">
    <GridView.ItemTemplate>
        <DataTemplate x:DataType="models:ImageFileInfo">
            <Grid
                Width="{x:Bind DisplayWidth, Mode=OneWay}"
                Height="{x:Bind DisplayHeight, Mode=OneWay}"
                Tag="{x:Bind}"
                RightTapped="ImageItem_RightTapped">
                <FlyoutBase.AttachedFlyout>
                    <MenuFlyout>
                        <MenuFlyoutItem Text="Share" Icon="Share" Click="Share_Click"/>
                        <MenuFlyoutItem Text="Save" Icon="Save" Click="Save_Click"/>
                        <MenuFlyoutItem Text="Delete" Icon="Delete" Click="Delete_Click"/>
                        <MenuFlyoutSeparator/>
                        <MenuFlyoutItem Text="Resize"/>
                        <MenuFlyoutItem Text="Move"/>
                    </MenuFlyout>
                </FlyoutBase.AttachedFlyout>
                <Border
                    CornerRadius="8"
                    Background="{ThemeResource CardBackgroundFillColorSecondaryBrush}"
                    BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                    BorderThickness="1"
                    Padding="6">
                    <Grid Background="{ThemeResource CardBackgroundFillColorSecondaryBrush}">
                        <Image
                            Source="{x:Bind Thumbnail, Mode=OneWay}"
                            Stretch="Uniform"
                            HorizontalAlignment="Center"
                            VerticalAlignment="Center"/>
                    </Grid>
                </Border>
                <Border
                    x:Name="SelectionBorder"
                    CornerRadius="8"
                    BorderBrush="{ThemeResource AccentFillColorDefaultBrush}"
                    BorderThickness="3"
                    Opacity="0"/>
                <ProgressRing
                    Width="24"
                    Height="24"
                    HorizontalAlignment="Center"
                    VerticalAlignment="Center"
                    IsActive="{x:Bind Thumbnail, Converter={StaticResource NullToBoolConverter}, Mode=OneWay}"/>
            </Grid>
        </DataTemplate>
    </GridView.ItemTemplate>
    <GridView.ItemContainerStyle>
        <Style TargetType="GridViewItem">
            <Setter Property="Margin" Value="4"/>
            <Setter Property="Padding" Value="0"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            <Setter Property="VerticalContentAlignment" Value="Stretch"/>
        </Style>
    </GridView.ItemContainerStyle>
    <GridView.ItemsPanel>
        <ItemsPanelTemplate>
            <ItemsWrapGrid
                Orientation="Horizontal"
                AreStickyGroupHeadersEnabled="False"/>
        </ItemsPanelTemplate>
    </GridView.ItemsPanel>
</GridView>
```

## 三、代码隐藏修改

### 需要移除的代码

1. **字段**
   - `_imageScrollView` - GridView 内置 ScrollViewer
   - `ImageLinedFlowLayout` 相关

2. **方法**
   - `AttachItemsViewScrollView()` - 不再需要
   - `ImageLinedFlowLayout_ItemsInfoRequested()` - GridView 不需要
   - `ImageScrollView_ViewChanged()` - 使用 ContainerContentChanging 替代
   - `GetVisibleImageItems()` - 使用 ContainerContentChanging 替代
   - `FindDescendants<T>()` - 不再需要

3. **事件处理**
   - `ImageItem_Loaded` - 使用 ContainerContentChanging 替代
   - `ImageItem_Unloaded` - 使用 ContainerContentChanging 替代

### 需要修改的代码

1. **ViewModel_ImagesChanged**
```csharp
private void ViewModel_ImagesChanged(object? sender, EventArgs e)
{
    if (_isUnloaded || AppLifetime.IsShuttingDown)
        return;

    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
    {
        try
        {
            ClearGridViewSelection();
            ImageGridView.ItemsSource = null;

            if (ViewModel.Images.Count > 0)
            {
                ImageGridView.ItemsSource = ViewModel.Images;
            }

            ImageGridView.ScrollIntoView(0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImagesChanged] 异常: {ex}");
        }
    });
}
```

2. **ViewModel_ThumbnailSizeChanged**
```csharp
private void ViewModel_ThumbnailSizeChanged(object? sender, EventArgs e)
{
    if (_isUnloaded || AppLifetime.IsShuttingDown)
        return;

    ImageGridView.UpdateLayout();
}
```

### 需要添加的代码

1. **ContainerContentChanging 事件处理**
```csharp
private void ImageGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
{
    if (args.Item is not ImageFileInfo imageInfo)
        return;

    if (args.InRecycleQueue)
    {
        imageInfo.CancelThumbnailLoad();
        return;
    }

    if (args.Phase == 0)
    {
        args.RegisterUpdateCallback(1, ImageGridView_ContainerContentChanging);
    }
    else if (args.Phase == 1)
    {
        _ = imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
    }
}
```

2. **选择相关方法**
```csharp
private void ImageGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    SyncSelectedStateFromGridView();
}

private void SyncSelectedStateFromGridView()
{
    if (_isUpdatingSelectionState)
        return;

    _isUpdatingSelectionState = true;
    try
    {
        foreach (var image in ViewModel.Images)
        {
            image.IsSelected = false;
        }

        foreach (var selectedItem in ImageGridView.SelectedItems)
        {
            if (selectedItem is ImageFileInfo imageInfo)
            {
                imageInfo.IsSelected = true;
            }
        }
    }
    finally
    {
        _isUpdatingSelectionState = false;
    }
}

private void ClearGridViewSelection()
{
    if (ImageGridView.SelectedItems.Count == 0)
        return;

    ImageGridView.DeselectAll();
}
```

3. **右键菜单**
```csharp
private void ImageItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
{
    if (sender is not FrameworkElement element)
        return;

    if (element.Tag is ImageFileInfo imageInfo && !ImageGridView.SelectedItems.Contains(imageInfo))
    {
        ImageGridView.SelectedItem = imageInfo;
    }

    FlyoutBase.ShowAttachedFlyout(element);
    e.Handled = true;
}
```

4. **键盘快捷键**
```csharp
private void MainPage_KeyDown(object sender, KeyRoutedEventArgs e)
{
    if (e.Key == VirtualKey.A)
    {
        var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (isCtrlPressed)
        {
            ImageGridView.SelectAll();
            e.Handled = true;
        }
    }
    else if (e.Key == VirtualKey.Escape)
    {
        ClearGridViewSelection();
        e.Handled = true;
    }
}
```

## 四、执行任务

| 序号 | 任务 | 说明 |
|------|------|------|
| 1 | 修改 MainPage.xaml | 替换 ItemsView 为 GridView |
| 2 | 修改 MainPage.xaml.cs | 更新代码隐藏 |
| 3 | 清理无用代码 | 移除 ItemsView 相关代码 |
| 4 | 构建测试 | 验证功能正常 |

## 五、GridView vs ItemsView 对比

| 方面 | ItemsView + LinedFlowLayout | GridView |
|------|----------------------------|----------|
| 布局稳定性 | 有 Bug，切换目录布局混乱 | 稳定可靠 |
| 虚拟化 | 支持 | 支持 |
| 选择模式 | 支持 | 支持 |
| 分批加载 | 需要手动实现 | ContainerContentChanging 原生支持 |
| 学习成本 | 新控件，文档少 | 成熟控件，文档丰富 |
| 维护成本 | 高（需要处理布局 Bug） | 低 |

## 六、注意事项

1. **GridView 内置 ScrollViewer**，不需要外部 ScrollView
2. **ContainerContentChanging** 提供分阶段加载，Phase 0 用于布局，Phase 1 用于加载缩略图
3. **ItemsWrapGrid** 作为 ItemsPanel 提供换行布局
4. **选择状态** 通过 GridView.SelectedItems 管理
