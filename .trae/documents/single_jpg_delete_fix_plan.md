# 删除单 JPG 修复计划

## 问题分析

### 问题 1：预删除标记没有刷新
新替换的 PrimaryImage 没有明确设置 IsPendingDelete = false。

### 问题 2：左上角合集图标没有更新
虽然创建了新 ImageGroup 并调用了 SetGroupInfo，但可能没有正确触发相关属性的变更通知。

---

## 修复策略

### 方案
在替换 ViewModel.Images[index] 后，显式触发 newPrimary 的相关属性变更通知，确保 UI 更新。

---

## 实现步骤

### 修改 RemoveDeletedImagesFromList 方法
**文件**：MainPage.xaml.cs
**修改**：
在设置 ViewModel.Images[index] = newPrimary 后：
1. 显式设置 newPrimary.IsPendingDelete = false
2. 触发必要的属性变更通知

---

## 任务列表

- [ ] 分析问题
- [ ] 实现修复代码
- [ ] 测试验证
