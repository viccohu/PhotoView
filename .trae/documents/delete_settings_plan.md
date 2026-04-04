# 删除设置功能实现计划

## 需求概述
1. 在设置页面增加删除设置，控制文件删除方式：直接删除或放入回收站
2. 实时切换开关，立即生效
3. 文件驱动器检查：对于U盘/可移动设备，文件删除只能直接删除

## 一、数据模型修改

### 1.1 ISettingsService.cs - 接口定义
- 新增 `DeleteToRecycleBin` 属性（bool，默认 true）
- 新增 `DeleteToRecycleBinChanged` 事件
- 新增 `SaveDeleteToRecycleBinAsync(bool)` 方法
- 新增 `LoadDeleteToRecycleBinAsync()` 方法

### 1.2 SettingsService.cs - 服务实现
- 新增 `_deleteToRecycleBin` 私有字段，默认值 true
- 实现 `DeleteToRecycleBin` 属性
- 实现 `DeleteToRecycleBinChanged` 事件
- 实现 `SaveDeleteToRecycleBinAsync` 方法
- 实现 `LoadDeleteToRecycleBinAsync` 方法
- 在 `InitializeAsync` 中加载设置

## 二、ViewModel 修改

### 2.1 SettingsViewModel.cs
- 新增 `DeleteToRecycleBin` 属性（代理到 SettingsService）
- 新增 `SetDeleteToRecycleBinCommand` 命令

## 三、UI 修改

### 3.1 SettingsPage.xaml
在 "Display" 区域后新增 "File Operations" 区域：
```xml
<!-- File Operations -->
<TextBlock Style="{StaticResource SettingsSectionHeaderTextBlockStyle}" Text="File Operations" />

<toolkit:SettingsCard Description="Move deleted files to recycle bin (not available for removable drives)" Header="Delete to recycle bin">
    <toolkit:SettingsCard.HeaderIcon>
        <FontIcon Glyph="&#xE74D;" />
    </toolkit:SettingsCard.HeaderIcon>
    <ToggleSwitch x:Name="DeleteToRecycleBinToggleSwitch" Toggled="DeleteToRecycleBinToggleSwitch_Toggled" />
</toolkit:SettingsCard>
```

### 3.2 SettingsPage.xaml.cs
- 新增 `UpdateDeleteToRecycleBinSelection()` 方法
- 新增 `DeleteToRecycleBinToggleSwitch_Toggled` 事件处理

## 四、删除逻辑修改

### 4.1 MainPage.xaml.cs - DeleteFileToRecycleBinAsync 方法
修改删除逻辑，根据设置和驱动器类型决定删除方式：

```csharp
private async System.Threading.Tasks.Task DeleteFileAsync(StorageFile file)
{
    var settingsService = App.GetService<ISettingsService>();
    var useRecycleBin = settingsService.DeleteToRecycleBin;
    
    // 检查是否为可移动驱动器
    var isRemovableDrive = IsRemovableDrive(file.Path);
    
    // 可移动驱动器只能直接删除
    if (isRemovableDrive)
    {
        await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
    }
    else if (useRecycleBin)
    {
        await file.DeleteAsync(StorageDeleteOption.Default);
    }
    else
    {
        await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
    }
}

private bool IsRemovableDrive(string filePath)
{
    try
    {
        var driveLetter = Path.GetPathRoot(filePath);
        if (string.IsNullOrEmpty(driveLetter))
            return false;
            
        var driveInfo = new DriveInfo(driveLetter);
        return driveInfo.DriveType == DriveType.Removable;
    }
    catch
    {
        return false;
    }
}
```

## 五、文件结构

### 修改文件
```
Contracts/Services/ISettingsService.cs    - 添加删除设置接口
Services/SettingsService.cs               - 实现删除设置服务
ViewModels/SettingsViewModel.cs           - 添加删除设置属性和命令
Views/SettingsPage.xaml                   - 添加删除设置 UI
Views/SettingsPage.xaml.cs                - 添加删除设置事件处理
Views/MainPage.xaml.cs                    - 修改删除逻辑
```

## 六、实现步骤

### 步骤 1：接口和服务修改
1. 修改 `ISettingsService.cs` 添加接口定义
2. 修改 `SettingsService.cs` 实现服务

### 步骤 2：ViewModel 修改
1. 修改 `SettingsViewModel.cs` 添加属性和命令

### 步骤 3：UI 修改
1. 修改 `SettingsPage.xaml` 添加设置卡片
2. 修改 `SettingsPage.xaml.cs` 添加事件处理

### 步骤 4：删除逻辑修改
1. 修改 `MainPage.xaml.cs` 中的删除方法
2. 添加驱动器类型检测方法

## 七、注意事项

1. **实时生效**：设置切换后立即生效，无需重启应用
2. **可移动驱动器限制**：U盘等可移动设备上的文件只能直接删除，无法放入回收站
3. **默认值**：默认启用"删除到回收站"
4. **错误处理**：驱动器检测失败时默认使用用户设置
