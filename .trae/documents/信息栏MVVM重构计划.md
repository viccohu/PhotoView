# 信息栏 MVVM 重构计划

## 问题分析

当前 `ImageViewerControl` 的信息栏实现存在以下问题：

### 1. 设备信息 (DeviceInfoPanel)
```csharp
// 当前实现：直接操作 UI 元素
DeviceInfoPanel.Children.Clear();
foreach (var info in deviceInfo)
{
    var textBlock = new TextBlock { Text = info, ... };
    DeviceInfoPanel.Children.Add(textBlock);
}
```

### 2. 文件路径 (FilePathPanel)
```csharp
// 当前实现：直接创建 UI 控件
var grid = new Grid { ... };
var pathTextBlock = new TextBlock { Text = file.ImageFile.Path, ... };
var copyButton = new Button { ... };
FilePathPanel.Children.Add(grid);
```

### 3. 其他信息
- 文件名：`ImageNameTextBox.Text = imageFileInfo.ImageName;`
- 分辨率：`ResolutionTextBlock.Text = $"{imageFileInfo.Width} x {imageFileInfo.Height}";`
- 文件大小：`FileSizeTextBlock.Text = FormatFileSize(basicProps.Size);`
- 日期时间：`CaptureDatePicker.Date = ...; CaptureTimePicker.Time = ...;`

## 重构目标

将信息栏改为 MVVM 模式：
- 创建 `ImageViewerViewModel` 管理所有数据
- 使用数据绑定替代直接操作 UI
- 使用 `ItemsControl` 显示列表数据

## 实现步骤

### 步骤 1：创建 ImageViewerViewModel

创建 `ViewModels/ImageViewerViewModel.cs`：

```csharp
public partial class ImageViewerViewModel : ObservableRecipient
{
    [ObservableProperty] private string _imageName = string.Empty;
    [ObservableProperty] private string _resolution = string.Empty;
    [ObservableProperty] private string _fileSize = string.Empty;
    [ObservableProperty] private DateTimeOffset? _captureDate;
    [ObservableProperty] private TimeSpan? _captureTime;
    [ObservableProperty] private ObservableCollection<string> _deviceInfo = new();
    [ObservableProperty] private ObservableCollection<FilePathItem> _filePaths = new();
    
    public async Task LoadFileInfoAsync(ImageFileInfo imageFileInfo) { ... }
}

public class FilePathItem
{
    public string Path { get; set; }
    public ICommand OpenInExplorerCommand { get; set; }
    public ICommand CopyPathCommand { get; set; }
}
```

### 步骤 2：修改 XAML 使用数据绑定

```xml
<!-- 文件名 -->
<TextBlock Text="{x:Bind ViewModel.ImageName, Mode=OneWay}" />

<!-- 分辨率 -->
<TextBlock Text="{x:Bind ViewModel.Resolution, Mode=OneWay}" />

<!-- 文件大小 -->
<TextBlock Text="{x:Bind ViewModel.FileSize, Mode=OneWay}" />

<!-- 日期时间 -->
<DatePicker Date="{x:Bind ViewModel.CaptureDate, Mode=OneWay}" />
<TimePicker Time="{x:Bind ViewModel.CaptureTime, Mode=OneWay}" />

<!-- 设备信息：使用 ItemsControl -->
<ItemsControl ItemsSource="{x:Bind ViewModel.DeviceInfo, Mode=OneWay}">
    <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="x:String">
            <TextBlock Text="{x:Bind}" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>

<!-- 文件路径：使用 ItemsControl -->
<ItemsControl ItemsSource="{x:Bind ViewModel.FilePaths, Mode=OneWay}">
    <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="models:FilePathItem">
            <Grid ColumnSpacing="8">
                <TextBlock Grid.Column="0" Text="{x:Bind Path}" />
                <Button Grid.Column="1" Command="{x:Bind CopyPathCommand}" />
            </Grid>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

### 步骤 3：修改 ImageViewerControl

- 添加 ViewModel 属性
- 移除直接操作 UI 的代码
- 在 `PrepareContent` 中调用 `ViewModel.LoadFileInfoAsync()`

### 步骤 4：注册服务

在 `App.xaml.cs` 中注册 `ImageViewerViewModel`

## 文件变更清单

| 文件 | 操作 |
|------|------|
| `ViewModels/ImageViewerViewModel.cs` | 新建 |
| `Models/FilePathItem.cs` | 新建 |
| `Controls/ImageViewerControl.xaml` | 修改：使用数据绑定 |
| `Controls/ImageViewerControl.xaml.cs` | 修改：移除UI操作代码 |
| `App.xaml.cs` | 修改：注册 ViewModel |

## 预期效果

- 信息栏数据完全由 ViewModel 管理
- XAML 只负责展示，不包含业务逻辑
- 代码更清晰、可测试性更好
