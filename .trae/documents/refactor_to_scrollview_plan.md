# 图片查看器改造计划 - 迁移到 ScrollView

## 需求确认
- ✅ 使用 `ScrollView` 替代 `ScrollViewer`
- ✅ Image 的 `Stretch` 改为 `"None"`
- ✅ 按 Win11 照片应用方式做边界限定（自由拖动但限制在图片边界内）

---

## [ ] 任务 1: 将 ScrollViewer 替换为 ScrollView
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 修改 XAML，将 `ScrollViewer` 替换为 `ScrollView`
  - 设置 `HorizontalScrollBarVisibility="Hidden"`
  - 设置 `VerticalScrollBarVisibility="Hidden"`
  - 设置 `ZoomMode="Enabled"`
  - 设置 `MinZoomFactor="0.1"` 和 `MaxZoomFactor="5.0"`
  - 设置 `Background` 为合适的颜色
- **Success Criteria**:
  - XAML 中成功使用 ScrollView 替代 ScrollViewer
- **Test Requirements**:
  - `programmatic` TR-1.1: 验证控件已从 ScrollViewer 改为 ScrollView
- **Notes**: ScrollView 是 WinUI 3 新控件，API 与 ScrollViewer 不同

## [ ] 任务 2: 调整 ImageContainer 和 Image 的配置
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 
  - 保持 ImageContainer（Grid）作为中间层容器
  - 设置 `HorizontalAlignment="Center"` 和 `VerticalAlignment="Center"`
  - 将 Image 的 `Stretch` 从 `"Uniform"` 改为 `"None"`
  - 将 Pointer 事件绑定到 ImageContainer 而不是 ScrollView
- **Success Criteria**:
  - ImageContainer 和 Image 配置正确
- **Test Requirements**:
  - `programmatic` TR-2.1: 验证 Image.Stretch 已设置为 None
  - `programmatic` TR-2.2: 验证事件绑定到 ImageContainer

## [ ] 任务 3: 重写滚轮缩放逻辑（以鼠标为中心）
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 
  - 重写 `PointerWheelChanged` 事件处理程序
  - 实现以鼠标为中心的缩放公式：
    ```csharp
    targetX = (scrollX + mouseX) * ratio - mouseX;
    targetY = (scrollY + mouseY) * ratio - mouseY;
    ```
  - 使用 `ScrollTo` 和 `ZoomTo` 方法
  - 设置 `ScrollingAnimationMode.Disabled` 获得即时跟手感
  - 标记 `e.Handled = true`
- **Success Criteria**:
  - 滚轮缩放以鼠标为中心
- **Test Requirements**:
  - `human-judgement` TR-3.1: 验证缩放时鼠标指向的像素保持固定

## [ ] 任务 4: 重写鼠标左键拖动逻辑
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**: 
  - 重写 `PointerPressed`、`PointerMoved`、`PointerReleased`、`PointerCanceled` 事件
  - 在 `PointerPressed` 中：
    - 检查左键按下
    - 捕获指针
    - 设置 `ProtectedCursor` 为抓取手势
  - 在 `PointerMoved` 中：
    - 计算鼠标位移
    - 调用 `ScrollTo` 更新位置
    - ScrollView 会自动限制在边界内
  - 在 `PointerReleased/Canceled` 中：
    - 释放指针捕获
    - 恢复光标
- **Success Criteria**:
  - 鼠标左键可以拖动图片，且限制在边界内
- **Test Requirements**:
  - `human-judgement` TR-4.1: 验证鼠标左键可以拖动
  - `human-judgement` TR-4.2: 验证拖动不会超出图片边界

## [ ] 任务 5: 清理旧代码和适配新 API
- **Priority**: P0
- **Depends On**: 任务 3, 4
- **Description**: 
  - 移除旧的 `EndDrag` 方法（简化逻辑）
  - 移除不再需要的拖动状态变量
  - 适配新的 API：
    - `ZoomFactor` → 保持不变
    - `HorizontalOffset` → 保持不变
    - `VerticalOffset` → 保持不变
    - `ChangeView` → 替换为 `ScrollTo` 和 `ZoomTo`
  - 更新 `PrepareContent` 方法中的重置逻辑
- **Success Criteria**:
  - 代码清理完成，适配新 API
- **Test Requirements**:
  - `programmatic` TR-5.1: 验证旧代码已移除
  - `programmatic` TR-5.2: 验证使用了新 API

## [ ] 任务 6: 验证双指缩放和触控拖动（可选）
- **Priority**: P2
- **Depends On**: 任务 1
- **Description**: 
  - 验证 ScrollView 内置的双指缩放是否正常工作
  - 验证 ScrollView 内置的触控拖动是否正常工作
  - 确保与自定义鼠标逻辑共存
- **Success Criteria**:
  - 触控和双指缩放手势正常
- **Test Requirements**:
  - `human-judgement` TR-6.1: 验证触控可以拖动
  - `human-judgement` TR-6.2: 验证双指可以缩放

## [ ] 任务 7: 全面测试和验证
- **Priority**: P1
- **Depends On**: 任务 5
- **Description**: 
  - 测试滚轮缩放（以鼠标为中心）
  - 测试鼠标左键拖动
  - 测试双击缩放
  - 测试切换图片
  - 测试边界限制
- **Success Criteria**:
  - 所有功能正常工作
- **Test Requirements**:
  - `human-judgement` TR-7.1: 验证所有功能正常
