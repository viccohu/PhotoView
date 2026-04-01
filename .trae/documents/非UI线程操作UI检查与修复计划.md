# 等高两头对齐布局 - 实现计划

## 背景

当前 `JustifiedLayout` 实现了等高布局，但间距固定，没有头尾对齐。新想法要求：

1. 保持等高
2. 定义最小间距
3. 智能换行（连最小间距都排不下时换行）
4. 头尾对齐（平均分配额外间距）

## 可行性分析

### ✅ 完全可行！

| 评估项   | 结果   | 说明                                   |
| ----- | ---- | ------------------------------------ |
| 算法复杂度 | O(n) | 与当前相同                                |
| 性能影响  | 几乎无  | 只有简单数学运算                             |
| 实现难度  | 低    | 只需修改 `BuildRows` 和 `ArrangeOverride` |

***

## 实现步骤

### \[ ] 任务 1：添加 `MinimumSpacing` 依赖属性

* **Priority**: P0

* **Depends On**: None

* **Description**:

  * 添加 `MinimumSpacing` 依赖属性（替代 `Spacing` 或作为补充）

  * 默认值：4.0

* **Success Criteria**:

  * 属性正确注册

  * 属性更改时正确触发布局刷新

* **Test Requirements**:

  * `programmatic` TR-1.1: 属性可读写

  * `programmatic` TR-1.2: 属性更改时调用 `InvalidateMeasure`

### \[ ] 任务 2：修改 `Row` 类，添加间距信息

* **Priority**: P0

* **Depends On**: 任务 1

* **Description**:

  * 给 `Row` 类添加 `ActualSpacing` 属性，存储实际使用的间距

* **Success Criteria**:

  * `Row` 类包含 `ActualSpacing` 属性

* **Test Requirements**:

  * `programmatic` TR-2.1: `ActualSpacing` 属性可读写

### \[ ] 任务 3：重写 `BuildRows` 方法，计算实际间距

* **Priority**: P0

* **Depends On**: 任务 2

* **Description**:

  * 使用 `MinimumSpacing` 进行换行判断

  * 计算每行总宽度（使用最小间距）

  * 计算额外可用宽度 = 容器宽度 - 总宽度

  * 计算实际间距 = MinimumSpacing + 额外宽度 / (图片数量 - 1)

  * 将 `ActualSpacing` 存储到 `Row` 对象中

  * 保持 `LineHeight` 不变（等高）

* **Success Criteria**:

  * 使用最小间距换行

  * 正确计算实际间距

  * 最后一行不强制对齐（保持最小间距）

* **Test Requirements**:

  * `programmatic` TR-3.1: 换行逻辑正确

  * `programmatic` TR-3.2: `ActualSpacing` 计算正确

  * `human-judgement` TR-3.3: 布局视觉效果良好

### \[ ] 任务 4：修改 `ArrangeOverride` 方法，使用实际间距

* **Priority**: P0

* **Depends On**: 任务 3

* **Description**:

  * 使用 `row.ActualSpacing` 替代 `Spacing`

* **Success Criteria**:

  * 使用实际间距进行元素排列

* **Test Requirements**:

  * `programmatic` TR-4.1: 使用 `ActualSpacing` 进行排列

### \[ ] 任务 5：更新 MainPage.xaml，使用新属性

* **Priority**: P1

* **Depends On**: 任务 1

* **Description**:

  * 在 `MainPage.xaml` 中设置 `MinimumSpacing` 属性

  * 移除或保留 `Spacing` 属性（取决于设计）

* **Success Criteria**:

  * XAML 正确使用新属性

* **Test Requirements**:

  * `human-judgement` TR-5.1: 布局在 MainPage 中正常工作

***

## 涉及文件

| 文件                           | 修改类型 |
| ---------------------------- | ---- |
| `Layouts/JustifiedLayout.cs` | 主要修改 |
| `Views/MainPage.xaml`        | 可选更新 |

***

## 性能预期

* 布局计算时间：与当前相同（O(n)）

* 内存占用：略有增加（每个 Row 多存储一个 double）

* 渲染性能：无变化

***

## 边界情况处理

1. **单行只有一张图片**：不计算间距，直接居中或左对齐
2. **最后一行**：不强制对齐，保持最小间距
3. **容器宽度很小**：图片垂直排列（每行一张）
4. **最小间距为 4px**：头尾紧密对齐

