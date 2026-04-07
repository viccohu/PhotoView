# WinUI 3 官方 UI 设计规范

## 目录
- [1. 字体使用规范](#1-字体使用规范)
- [2. 文字样式规范（TextBlock Styles）](#2-文字样式规范textblock-styles)
- [3. 颜色规范](#3-颜色规范)
- [4. 主题规范](#4-主题规范)
- [5. 字体大小规范](#5-字体大小规范)
- [6. 间距规范](#6-间距规范)
- [7. 圆角规范](#7-圆角规范)
- [8. 其他 UI 书写规范](#8-其他-ui-书写规范)

---

## 1. 字体使用规范

### 1.1 系统字体
- **默认字体**: Segoe UI Variable（Windows 11 新系统字体）
- **字体特性**: 支持可变字体技术，包括权重轴（wght）和光学大小轴（opsz）
- **适用场景**: 所有 UI 文本，包括标题、正文、按钮等

### 1.2 字体权重
| 权重名称 | 权重值 | 使用场景 |
|---------|--------|---------|
| 浅色 | 300 | 极少使用 |
| 半光 | 350 | 极少使用 |
| 常规 | 400 | 大多数正文文本 |
| 半粗体 | 600 | 标题、需要强调的文本 |
| 加粗 | 700 | 避免使用，太重 |

### 1.3 字体最小尺寸
- 半粗体: 最小 14px
- 常规: 最小 12px

### 1.4 文本对齐
- 默认: 左对齐
- 居中对齐: 仅用于特殊场景（如图标下方的文本）

### 1.5 大小写
- 使用句子大小写（Sentence case），包括标题

### 1.6 文本截断
- 大多数情况: 使用省略号（CharacterEllipsis）
- 特殊情况: 使用剪切（Clip）

---

## 2. 文字样式规范（TextBlock Styles）

### 2.1 官方 TextBlock 样式完整列表

#### 标题类（Header/Title）
| 样式名称 | 用途 | 字体大小/权重 | 使用场景示例 |
|---------|------|-------------|------------|
| `HeaderTextBlockStyle` | 页面/分区大标题 | 28px / SemiBold | 页面主标题、主要分区标题 |
| `TitleLargeTextBlockStyle` | 大型标题 | 40px / SemiBold | 应用标题、欢迎页标题 |
| `TitleTextBlockStyle` | 标准标题 | 20px / SemiBold | 卡片标题、对话框标题 |
| `SubtitleTextBlockStyle` | 副标题 | 18px / Regular | 标题下方的说明文字 |
| `SubheaderTextBlockStyle` | 子标题/分区标题 | 24px / SemiBold | 次要分区标题 |

#### 正文类（Body）
| 样式名称 | 用途 | 字体大小/权重 | 使用场景示例 |
|---------|------|-------------|------------|
| `BodyLargeTextBlockStyle` | 大型正文 | 18px / Regular | 重要说明文本 |
| `BodyTextBlockStyle` | 标准正文 | 14px / Regular | 主要内容文本 |
| `BodyStrongTextBlockStyle` | 强调正文 | 14px / SemiBold | 需要强调的正文 |
| `BodySmallTextBlockStyle` | 小型正文 | 12px / Regular | 辅助说明文本 |

#### 辅助文本类（Caption）
| 样式名称 | 用途 | 字体大小/权重 | 使用场景示例 |
|---------|------|-------------|------------|
| `CaptionTextBlockStyle` | 说明文本 | 12px / Regular | 标签、状态提示、辅助信息 |

### 2.2 样式使用的正确与错误示例

#### ❌ 错误用法
```xaml
<!-- TitleTextBlockStyle 不应该用于小组件标题！ -->
<TextBlock Text="图片名称" Style="{ThemeResource TitleTextBlockStyle}" />

<!-- 自定义字体大小，覆盖样式 -->
<TextBlock Text="标题" Style="{ThemeResource BodyTextBlockStyle}" FontSize="28" />
```

#### ✅ 正确用法
```xaml
<!-- 页面主标题 -->
<TextBlock Text="相册" Style="{ThemeResource HeaderTextBlockStyle}" />

<!-- 卡片标题 -->
<TextBlock Text="精选图片" Style="{ThemeResource TitleTextBlockStyle}" />

<!-- 小组件/标签标题 -->
<TextBlock Text="图片名称" Style="{ThemeResource BodyStrongTextBlockStyle}" />

<!-- 辅助信息 -->
<TextBlock Text="2024-01-01" Style="{ThemeResource CaptionTextBlockStyle}" />
```

### 2.3 样式层级关系
```
TitleLarge (40px)
  └─ Header (28px)
      └─ Subheader (24px)
          └─ Title (20px)
              └─ Subtitle (18px)
                  └─ BodyLarge (18px)
                      └─ Body / BodyStrong (14px)
                          └─ BodySmall / Caption (12px)
```

---

## 3. 颜色规范

### 3.1 基本原则
- **永远不要硬编码颜色值**（如 `#FFFFFF`、`Colors.White`）
- **始终使用 `{ThemeResource}`** 来引用画笔和颜色
- 这样可以自动支持浅色、深色和高对比度主题

### 3.2 文本颜色资源
| 资源名称 | 用途 |
|---------|------|
| `TextFillColorPrimaryBrush` | 主要文本（标题、正文） |
| `TextFillColorSecondaryBrush` | 次要文本（辅助说明） |
| `TextFillColorTertiaryBrush` | 第三级文本（禁用、提示） |
| `TextFillColorDisabledBrush` | 禁用状态文本 |

### 3.3 背景颜色资源
| 资源名称 | 用途 |
|---------|------|
| `BackgroundFillColorDefaultBrush` | 默认背景 |
| `BackgroundFillColorSecondaryBrush` | 次要背景 |
| `CardBackgroundFillColorDefaultBrush` | 卡片默认背景 |
| `CardBackgroundFillColorSecondaryBrush` | 卡片次要背景 |

### 3.4 边框颜色资源
| 资源名称 | 用途 |
|---------|------|
| `CardStrokeColorDefaultBrush` | 卡片默认边框 |
| `CardStrokeColorDefaultSecondaryBrush` | 卡片次要边框 |

### 3.5 强调色资源
| 资源名称 | 用途 |
|---------|------|
| `SystemAccentColor` | 系统强调色（颜色） |
| `AccentFillColorDefaultBrush` | 强调色填充（画笔） |

### 3.6 颜色使用示例

#### ❌ 错误用法
```xaml
<TextBlock Text="标题" Foreground="#000000" />
<Border Background="White" BorderBrush="#808080" />
```

#### ✅ 正确用法
```xaml
<TextBlock Text="标题" Foreground="{ThemeResource TextFillColorPrimaryBrush}" />
<Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" 
        BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" />
```

---

## 4. 主题规范

### 4.1 支持的主题
WinUI 3 支持 3 个主题：
- **浅色主题（Light）**
- **深色主题（Dark）**
- **高对比度主题（HighContrast）**

### 4.2 ThemeResource vs StaticResource
| 标记扩展 | 评估时机 | 更新时机 | 使用场景 |
|---------|---------|---------|---------|
| `{ThemeResource}` | 应用加载时 + 主题变更时 | 每次主题变更时更新 | 样式、设置器、控件模板、动画 |
| `{StaticResource}` | 仅 XAML 首次加载时 | 不更新 | 不随主题变化的资源 |

### 4.3 ThemeResource 使用规则
- ✅ 在样式、设置器、控件模板、属性设置器、动画中使用 `{ThemeResource}`
- ❌ 不要在 `ThemeDictionaries` 内的资源定义中使用 `{ThemeResource}`（使用 `{StaticResource}` 代替）
- ⚠️ 例外：可以在 `ThemeDictionaries` 中使用 `{ThemeResource}` 引用与应用主题无关的资源（如 `SystemAccentColor`）

### 4.4 自定义主题资源
如果需要自定义主题资源，需要：
1. 为 "Light" 和 "Dark" 分别指定主题字典
2. 另外提供 "HighContrast" 主题字典
3. 推荐明确使用 "Light"、"Dark"、"HighContrast"，而不是 "Default"

---

## 5. 字体大小规范

### 5.1 Windows 11 字体渐变（Type Ramp）
所有大小均采用有效像素（epx）

| 示例 | 权重 | 大小/行高 | 对应样式 |
|-----|------|----------|---------|
| 显示 | SemiBold | 68/92 epx | - |
| 显示 | SemiBold | 40/52 epx | TitleLarge |
| 显示 | SemiBold | 28/36 epx | Header |
| 显示 | SemiBold | 20/28 epx | Title |
| 文本 | Regular | 18/24 epx | BodyLarge / Subtitle |
| 文本 | SemiBold | 14/20 epx | BodyStrong |
| 文本 | Regular | 14/20 epx | Body |
| 小型 | Regular | 12/16 epx | BodySmall / Caption |

### 5.2 行宽（Readability）
- **理想**: 每行 50-60 个字符（含空格）
- **最大**: 不超过 75 个字符
- **例外**: 无连续阅读的内容可以使用较长行宽

---

## 6. 间距规范

### 6.1 标准间距单位
使用 4px 的倍数作为间距单位：
- 4px, 8px, 12px, 16px, 24px, 32px, 48px 等

### 6.2 常用间距值
| 间距名称 | 值 | 使用场景 |
|---------|----|---------|
| XXSmall | 4px | 紧凑元素间距 |
| XSmall | 8px | 小间距 |
| Small | 12px | 标准元素间距 |
| Medium | 24px | 区块间距 |
| Large | 36px | 大区块间距 |

### 6.3 文本间距
- 标题与正文间距: 12-24px
- 段落间距: 8-12px
- 行高: 约为字体大小的 1.4-1.5 倍

### 6.4 控件间距
- 相关控件组内间距: 8-12px
- 不相关控件组间距: 16-24px
- 页面边距: 24-32px

---

## 7. 圆角规范

### 7.1 圆角半径标准
Windows 11 使用三种级别的圆角效果：

| 圆角半径 | 使用场景 | 对应资源 |
|---------|---------|---------|
| 8px | 顶级容器（应用窗口、下拉菜单、对话框） | `OverlayCornerRadius` |
| 4px | 页面内元素（按钮、列表背板、条形控件） | `ControlCornerRadius` |
| 0px | 与其他直边缘相交的直边缘 | - |

### 7.2 详细使用场景

#### 8px 圆角
- ContentDialog（内容对话框）
- 弹出框（Flyout）
- 菜单弹出框（MenuFlyout）
- 教学提示（TeachingTip）
- 应用窗口（顶级窗口）

#### 4px 圆角
- Button（按钮）
- CheckBox（复选框）
- ComboBox（组合框）
- TextBox（文本框）
- ListView（列表视图）
- ProgressBar（进度条）
- ScrollBar（滚动条）
- Slider（滑块）
- 工具提示（Tooltip）- 例外，因尺寸较小

#### 0px 圆角（不应圆角化的情况）
- 容器内多个 UI 元素相互触摸时（如 SplitButton 的两个部分）
- 浮出式 UI 元素连接到调用它的界面时
- 窗口对齐或最大化时

### 7.3 全局资源控制
可以在 App.xaml 中重写以下资源来更改应用中所有控件的圆角：
```xaml
<CornerRadius x:Key="ControlCornerRadius">4</CornerRadius>
<CornerRadius x:Key="OverlayCornerRadius">8</CornerRadius>
```

### 7.4 圆角使用示例

#### ❌ 错误用法
```xaml
<!-- 按钮不应该用 8px 圆角 -->
<Button Content="点击" CornerRadius="8" />

<!-- 对话框不应该用 4px 圆角 -->
<ContentDialog CornerRadius="4" />
```

#### ✅ 正确用法
```xaml
<!-- 按钮使用默认或显式 4px 圆角 -->
<Button Content="点击" CornerRadius="{ThemeResource ControlCornerRadius}" />

<!-- 对话框使用默认或显式 8px 圆角 -->
<ContentDialog CornerRadius="{ThemeResource OverlayCornerRadius}" />
```

---

## 8. 其他 UI 书写规范

### 8.1 XAML 标记规范

#### x:Bind vs Binding
- **优先使用 `{x:Bind}`**: 编译时类型安全、性能更高
- **`{Binding}`**: 仅在需要动态反射绑定时使用
- **NativeAOT 注意**: NativeAOT 编译下 `{Binding}` 完全不工作，必须使用 `{x:Bind}`

#### DataTemplate 中的 x:DataType
- 在使用 `{x:Bind}` 的 `DataTemplate` 上必须设置 `x:DataType`
- 在 `Page`/`UserControl` 上设置 `x:DataType` 可启用编译时绑定验证

#### 绑定模式
- `Mode=OneWay`: 动态变化的值
- `Mode=OneTime`: 静态不变的值
- `Mode=TwoWay`: 仅用于可编辑的输入控件

### 8.2 性能优化规范

#### 文本渲染快速路径
TextBlock 有高效的快速路径渲染，满足以下条件时启用：
- 通过 `Text` 属性显式设置文本（而不是 `Inlines`）
- `CharacterSpacing` 为默认值 0
- `TextTrimming` 为 `None`、`CharacterEllipsis` 或 `WordEllipsis`（`Clip` 会禁用快速路径）

#### 延迟加载
- 使用 `x:Load` 或 `x:DeferLoadStrategy` 延迟加载不需要立即显示的 UI 元素
- 对大型列表使用虚拟化（`ItemsRepeater`、`ListView`、`GridView` 等）

### 8.3 可访问性规范
- 在所有交互控件上设置 `AutomationProperties.Name`
- 在分区标题上使用 `AutomationProperties.HeadingLevel`
- 使用 `AutomationProperties.AccessibilityView="Raw"` 隐藏装饰性元素
- 确保完整的键盘导航（Tab、Enter、Space、方向键）
- 满足 WCAG 颜色对比度要求

### 8.4 线程规范（重要！）
- UI 对象必须在 UI 线程创建（禁止在后台线程创建 `BitmapImage`、`ImageSource`、`DependencyObject`）
- 禁止在 async 后直接操作 UI
- 使用 `DispatcherQueue.TryEnqueue(() => { ... })` 从后台线程更新 UI
- 在调度前检查 `DispatcherQueue.HasThreadAccess`

### 8.5 WinUI 3 特定 API 规范（重要！）

#### 禁止使用的 UWP API（必须使用 Windows App SDK 等效项）
| ❌ 禁止使用 | ✅ 应该使用 |
|------------|------------|
| `Windows.UI.Popups.MessageDialog` | `ContentDialog`（必须设置 `XamlRoot`） |
| `CoreDispatcher.RunAsync` / `Dispatcher.RunAsync` | `DispatcherQueue.TryEnqueue` |
| `Window.Current` | 静态 `App.MainWindow` 属性 |
| `Windows.UI.Xaml.*` 命名空间 | `Microsoft.UI.Xaml.*` |
| `Windows.UI.Composition` | `Microsoft.UI.Composition` |
| `Windows.UI.Colors` | `Microsoft.UI.Colors` |
| `ApplicationView` / `CoreWindow` | `Microsoft.UI.Windowing.AppWindow` |
| `CoreApplicationViewTitleBar` | `AppWindow.TitleBar` |
| `GetForCurrentView()` 模式 | `AppWindow` API |

#### ContentDialog 使用规范
```csharp
// ✅ 正确：必须设置 XamlRoot
var dialog = new ContentDialog();
dialog.XamlRoot = this.Content.XamlRoot;
await dialog.ShowAsync();
```

#### Window 管理
```csharp
// 获取 AppWindow
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
```

---

## 附录

### A. 快速参考检查表

在编写 UI 代码前，请检查：
- [ ] 所有颜色使用 `{ThemeResource}` 而不是硬编码
- [ ] TextBlock 使用正确的官方样式（如需要）
- [ ] TitleTextBlockStyle 只用于真正的标题（不是小组件标题）
- [ ] 圆角使用 4px 或 8px，符合场景
- [ ] 字体大小符合官方字体渐变
- [ ] 使用 `{x:Bind}` 而不是 `{Binding}`
- [ ] DataTemplate 设置了 `x:DataType`
- [ ] 交互控件设置了 `AutomationProperties.Name`

### B. 参考资源
- [Windows 排版文档](https://learn.microsoft.com/zh-cn/windows/apps/design/signature-experiences/typography)
- [Windows 几何图形文档](https://learn.microsoft.com/zh-cn/windows/apps/design/style/rounded-corner)
- [XAML 主题资源](https://learn.microsoft.com/zh-hk/windows/apps/develop/platform/xaml/xaml-theme-resources)
- [WinUI 3 Gallery](https://aka.ms/winui3gallery) - 下载查看实际示例
- [WinUI 3 GitHub](https://github.com/microsoft/microsoft-ui-xaml)

---

**最后更新**: 2026-04-07  
**适用版本**: WinUI 3 / Windows App SDK 1.7+
