# 集合缩略图删除 Bug 修复计划

## 问题分析

### 当前行为
当一个 ImageGroup（集合缩略图）中有多个文件时，如果只删除其中一个文件，整个缩略图会从列表中消失。

### 问题根源
1. 集合缩略图显示的是 `ImageGroup.PrimaryImage`（主图片）
2. 删除文件时，代码直接从 `ViewModel.Images` 中移除被删除的 `ImageFileInfo` 对象
3. 如果被删除的是 `PrimaryImage`，整个缩略图就消失了
4. 即使删除的是副图片，也没有更新缩略图信息

---

## 修复策略

### 方案
- 判断删除的文件是否属于集合
- 如果是集合且只删除了部分文件：
  - 从 `ImageGroup.Images` 中移除被删除的文件
  - 重新选择新的 `PrimaryImage`
  - 更新剩余文件的 `GroupInfo`
  - **不**从 `ViewModel.Images` 中移除整个集合
  - 只移除被删除的单个 `ImageFileInfo`（如果它是之前显示的项）
- 如果是集合且删除了所有文件：
  - 从 `ViewModel.Images` 中移除整个集合

---

## 实现步骤

### 步骤 1：修改 RemoveDeletedImagesFromList 方法
**文件**：MainPage.xaml.cs
**修改**：
- 遍历被删除的图片
- 对于每个图片：
  - 检查是否有 Group
  - 有 Group：
    - 从 Group.Images 中移除该图片
    - 如果 Group.Images.Count > 0：
      - 重新选择 PrimaryImage
      - 更新所有剩余图片的 GroupInfo
      - 如果被删除的是 ViewModel.Images 中的项（原 PrimaryImage）：
        - 在原位置替换为新的 PrimaryImage
    - 否则：
      - 从 ViewModel.Images 中移除该图片（或原 PrimaryImage）
  - 无 Group：
    - 直接从 ViewModel.Images 中移除

---

## 详细设计

### RemoveDeletedImagesFromList 新逻辑

```csharp
private void RemoveDeletedImagesFromList(List<ImageFileInfo> deletedImages)
{
    if (deletedImages.Count == 0)
        return;

    var firstVisibleIndex = GetFirstVisibleItemIndex();
    var selectedItem = ImageGridView.SelectedItem as ImageFileInfo;

    // 按组处理被删除的图片
    var groupsToProcess = new Dictionary<ImageGroup, List<ImageFileInfo>>();
    var singleImagesToRemove = new List<ImageFileInfo>();

    foreach (var deletedImage in deletedImages)
    {
        if (deletedImage.Group != null)
        {
            if (!groupsToProcess.ContainsKey(deletedImage.Group))
            {
                groupsToProcess[deletedImage.Group] = new List<ImageFileInfo>();
            }
            groupsToProcess[deletedImage.Group].Add(deletedImage);
        }
        else
        {
            singleImagesToRemove.Add(deletedImage);
        }
    }

    // 处理单张图片
    foreach (var image in singleImagesToRemove)
    {
        ViewModel.Images.Remove(image);
    }

    // 处理组
    foreach (var group in groupsToProcess.Keys)
    {
        var deletedInGroup = groupsToProcess[group];
        
        // 从组中移除被删除的图片
        foreach (var deletedImage in deletedInGroup)
        {
            group.Images.Remove(deletedImage);
        }

        if (group.Images.Count > 0)
        {
            // 组还有剩余图片，需要更新
            var oldPrimary = group.PrimaryImage;
            
            // 重新选择主图片
            var newGroup = new ImageGroup(group.GroupName, group.Images);
            
            // 在 ViewModel.Images 中找到旧主图片的位置并替换
            var index = ViewModel.Images.IndexOf(oldPrimary);
            if (index >= 0)
            {
                ViewModel.Images[index] = newGroup.PrimaryImage;
            }
        }
        else
        {
            // 组已空，移除旧主图片
            ViewModel.Images.Remove(group.PrimaryImage);
        }
    }

    ViewModel.ClearAllPendingDelete();

    // 恢复滚动位置和选中状态
    DispatcherQueue.TryEnqueue(() =>
    {
        // ... 原有逻辑
    });
}
```

---

## 任务列表

- [ ] 分析当前删除逻辑
- [ ] 实现分组处理逻辑
- [ ] 实现组剩余图片更新逻辑
- [ ] 测试验证：
  - 单张图片删除
  - 集合部分删除（保留主图片）
  - 集合部分删除（删除主图片）
  - 集合全部删除
