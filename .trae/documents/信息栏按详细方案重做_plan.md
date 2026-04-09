
# 信息栏按详细方案重做计划

## 1. 现状分析
当前实现与详细方案存在以下差异：
- 未使用 VerticalStackPanel 作为主布局容器
- 文件信息和设备信息未使用 UniformGrid 实现固定网格
- 圆角为 8，方案要求 4
- 标签文字未使用 Opacity="0.7" 弱化
- 图标编码与方案不一致
- 日期模块需要调整为与方案一致的形式

## 2. 优化目标
严格按照详细方案文档重新实现：
- 使用 VerticalStackPanel 作为主布局容器
- 文件信息和设备信息使用 2行×3列 的 UniformGrid
- 小圆角 CornerRadius="4"
- 小间距 Spacing="8"
- 标签文字使用 Opacity="0.7"
- 使用官方主题色 {ThemeResource SubtleFillColorSecondaryBrush}
- 保留文件路径信息
- 沿用之前的所有数据绑定逻辑

## 3. 具体修改方案

### 3.1 主布局调整
- 将 StackPanel 替换为 VerticalStackPanel
- Spacing 设置为 16（模块间距）
- Padding 设置为 16,12,16,12

### 3.2 文件名模块
- 使用 HorizontalStackPanel
- 图标使用方案推荐的编码
- Spacing=12

### 3.3 评级模块
- 使用 HorizontalStackPanel
- 图标使用方案推荐的编码
- Spacing=12

### 3.4 日期模块
- 需要调整为与方案类似的布局（图标+日期显示）
- 保持当前的拆分显示方式

### 3.5 文件信息模块
- 标题使用 HorizontalStackPanel
- 内容使用 UniformGrid Rows="2" Columns="3" Spacing="8"
- 每个格子使用 Border，CornerRadius="4"
- 背景使用 {ThemeResource SubtleFillColorSecondaryBrush}
- Padding=12,8
- 标签文字 Opacity="0.7"
- 最后一个格子预留（第2行第3列）

### 3.6 设备信息模块
- 标题使用 HorizontalStackPanel
- 内容使用 UniformGrid Rows="2" Columns="3" Spacing="8"
- 每个格子使用 Border，CornerRadius="4"
- 背景使用 {ThemeResource SubtleFillColorSecondaryBrush}
- Padding=12,8
- 标签文字 Opacity="0.7"
- 相机信息不跨列，占用第一格

### 3.7 文件路径模块
- 保留现有实现不变

## 4. 修改文件列表
1. `Controls/ImageViewerControl.xaml` - 完全重写信息栏布局

## 5. 风险和注意事项
- 保持所有数据绑定不变
- 保持事件处理不变
- 使用官方主题色，确保深浅主题适配
- 确保布局响应式
- 保留文件路径模块

