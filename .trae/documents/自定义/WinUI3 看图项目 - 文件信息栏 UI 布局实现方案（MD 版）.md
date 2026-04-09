# WinUI3 看图项目 - 文件信息栏 UI 布局实现方案（MD 版）

说明：本方案严格适配 AI 开发工具（Cursor/Trae 等），可直接用于代码生成，完全遵循 WinUI3 官方控件规范与 Fluent Design 设计理念，精准还原设计需求：图标对应内容、文件/设备信息模块为固定大小格子、小间距、小圆角，明确图标编码与布局层级。

# 一、核心设计规范

## 1. 整体布局结构（从上到下）

- 根容器：ScrollViewer（垂直滚动，适配内容溢出场景）
- 主布局：VerticalStackPanel（垂直流式布局，统一模块间距）
- 模块划分：标题栏（文件名）→ 基础信息（评级、日期）→ 文件信息（固定网格）→ 设备信息（固定网格）
- 全局样式：必须遵守（important）使用微软官方的样式规范，不允许颜色硬编码，所有可交互/容器控件统一小圆角、小间距

## 2. 图标规范（Segoe MDL2 Assets 官方字体图标）

| 对应模块                                                         | 图标编码   | 图标用途     | 样式配置                     |
| ------------------------------------------------------------ | ------ | -------- | ------------------------ |
| 文件名                                                          |       | 标识文件/图片  | FontSize="16"，垂直居中，系统主题色 |
| 文件信息                                                         |       | 标识文件信息模块 | FontSize="16"，垂直居中，系统主题色 |
| 设备信息                                                         |       | 标识设备信息模块 | FontSize="16"，垂直居中，系统主题色 |
| 日期                                                           |       | 标识拍摄日期模块 | FontSize="16"，垂直居中，系统主题色 |
| 评级                                                           |       | 标识星级评级模块 | FontSize="16"，垂直居中，系统主题色 |
| 补充：所有图标统一使用 FontIcon 控件，字体为 Segoe MDL2 Assets，避免图片资源，提升渲染性能。 | <br /> | <br />   | <br />                   |

## 3. 关键样式要求（核心设计重点）

- 固定网格：文件信息、设备信息模块均采用 2 行 × 3 列 UniformGrid，每个格子大小固定、自动均分
- 间距：格子间间距 8px（小间距），模块间间距 16px，图标与内容间距 8\~12px
- 圆角：所有格子、交互控件统一 CornerRadius="4"（小圆角）
- 背景与文字：深色主题默认（根背景 #1E1E1E，格子背景 #2B2B2B），文字白色，标签文字弱化（Opacity=0.7）

# 二、各模块详细布局与实现（可直接复制给 AI 工具）

## 1. 根容器与全局配置

作用：统一布局容器，适配滚动与主题，所有子模块嵌套于此

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto" Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">
    <VerticalStackPanel Spacing="16" Padding="16,12,16,12"&gt;
        <!-- 所有子模块（文件名、评级、日期、文件信息、设备信息）嵌套在此 -->
    </VerticalStackPanel>
</ScrollViewer>
```

## 2. 文件名模块（标题栏）

布局：水平布局（图标 + 文件名），作为信息栏顶部标题，支持编辑态切换

```xml
<!-- 文件名模块（标题栏） -->
<HorizontalStackPanel Spacing="12" Margin="0,0,0,8">
    <FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE91B;" FontSize="16" VerticalAlignment="Center" />
    <TextBlock Text="DSC_0057" FontSize="18" FontWeight="SemiBold" VerticalAlignment="Center" 
               TextTrimming="CharacterEllipsis" MaxWidth="300" />
</HorizontalStackPanel>
```

说明：TextBlock 可替换为 TextBox 实现编辑功能，MaxWidth 限制宽度，超出显示省略号，贴合看图软件使用场景。

## 3. 评级模块

布局：水平布局（图标 + 星级控件），支持用户评分，适配 WinUI3 官方控件

```xml
<!-- 评级模块 -->
<HorizontalStackPanel Spacing="12" Margin="0,0,0,8">
    <FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE728;" FontSize="16" VerticalAlignment="Center" />
    <RatingControl MaxRating="5" Value="1" IsClearable="False" ItemSize="24" VerticalAlignment="Center" />
</HorizontalStackPanel>
```

## 4. 日期模块

布局：水平布局（图标 + 日期+时间选择器），用于展示/编辑图片拍摄日期

```xml
<!-- 日期模块 -->
<HorizontalStackPanel Spacing="12" Margin="0,0,0,16">
    <FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE787;" FontSize="16" VerticalAlignment="Center" />
    <CalendarDatePicker Date="2024-12-01" VerticalAlignment="Center" CornerRadius="4" />
    <TimePicker Time="12:12" VerticalAlignment="Center" CornerRadius="4" />
</HorizontalStackPanel>
```

## 5. 文件信息模块（固定网格）

布局：标题行（图标 + 标题）+ 2行3列固定网格，格子固定大小、小间距、小圆角，展示图片文件核心参数

```xml
<!-- 文件信息模块标题 -->
<HorizontalStackPanel Spacing="8" Margin="0,0,0,8">
    <FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE97C;" FontSize="16" VerticalAlignment="Center" />
    <TextBlock Text="文件信息" FontSize="16" FontWeight="SemiBold" VerticalAlignment="Center" />
</HorizontalStackPanel>

<!-- 文件信息固定网格（2行3列，固定大小、小间距、小圆角） -->
<UniformGrid Rows="2" Columns="3" Spacing="8" Margin="0,0,0,16">
    <!-- 第1行第1列：尺寸 -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="尺寸" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="6000X3000" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
    <!-- 第1行第2列：大小 -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="大小" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="3MB" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
    <!-- 第1行第3列：DPI -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="DPI" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="72" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
    <!-- 第2行第1列：色深 -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="色深" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="12bit" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
    <!-- 第2行第2列：评级来源 -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="评级来源" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="WinRT" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
    <!-- 第2行第3列：预留 -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
</UniformGrid>
```

## 6. 设备信息模块（固定网格）

布局：标题行（图标 + 标题）+ 2行3列固定网格，与文件信息模块样式统一，展示图片拍摄设备参数

```xml
<!-- 设备信息模块标题 -->
<HorizontalStackPanel Spacing="8" Margin="0,0,0,8">
    <FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE722;" FontSize="16" VerticalAlignment="Center" />
    <TextBlock Text="设备信息" FontSize="16" FontWeight="SemiBold" VerticalAlignment="Center" />
</HorizontalStackPanel>

<!-- 设备信息固定网格（2行3列，固定大小、小间距、小圆角） -->
<UniformGrid Rows="2" Columns="3" Spacing="8">
    <!-- 第1行第1列：相机 -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="相机" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="SONY A1" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
    <!-- 第1行第2列：镜头 -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="镜头" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="35mm GM" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
    <!-- 第1行第3列：焦距 -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="焦距" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="35MM" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
    <!-- 第2行第1列：快门 -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="快门" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="1/10" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
    <!-- 第2行第2列：光圈 -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="光圈" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="F1.2" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
    <!-- 第2行第3列：ISO -->
    <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
        <VerticalStackPanel Spacing="4">
            <TextBlock Text="ISO" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
            <TextBlock Text="100" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
        </VerticalStackPanel>
    </Border>
</UniformGrid>
```

# 三、AI 开发工具实现提示（关键指引）

1. 命名空间：所有控件需使用 Microsoft.UI.Xaml.Controls 命名空间，禁止第三方控件，确保 WinUI3 兼容性。
2. 主题适配：将方案中硬编码背景色替换为 ThemeResource，实现深浅主题自动切换（示例：{ThemeResource SubtleFillColorSecondaryBrush}）。
3. 数据绑定：所有展示类文本（如文件名、尺寸、相机参数）预留绑定路径（示例：{x:Bind FileName}），方便对接图片元数据逻辑。
4. 交互逻辑：为 RatingControl、CalendarDatePicker、TimePicker 添加 ValueChanged 事件，实现用户操作后的数据回写。
5. 性能优化：优先使用 UniformGrid 实现固定网格，避免手动设置 Grid 列宽行高，减少布局计算压力；图标使用 FontIcon，提升渲染效率。

# 四、完整c代码（整合所有模块）

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto" Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">
    <VerticalStackPanel Spacing="16" Padding="16,12,16,12"&gt;
        <!-- 1. 文件名模块（标题栏） -->
        <HorizontalStackPanel Spacing="12" Margin="0,0,0,8">
            <FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE91B;" FontSize="16" VerticalAlignment="Center" />
            <TextBlock Text="DSC_0057" FontSize="18" FontWeight="SemiBold" VerticalAlignment="Center" 
                       TextTrimming="CharacterEllipsis" MaxWidth="300" /&gt;
        &lt;/HorizontalStackPanel&gt;

        <!-- 2. 评级模块 -->
        <HorizontalStackPanel Spacing="12" Margin="0,0,0,8">
            <FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE728;" FontSize="16" VerticalAlignment="Center" />
            <RatingControl MaxRating="5" Value="1" IsClearable="False" ItemSize="24" VerticalAlignment="Center" />
        </HorizontalStackPanel>

<!-- 3. 日期模块 -->
        <HorizontalStackPanel Spacing="12" Margin="0,0,0,16">
            <FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE787;" FontSize="16" VerticalAlignment="Center" />
            <CalendarDatePicker Date="2024-12-01" VerticalAlignment="Center" CornerRadius="4" />
            <TimePicker Time="12:12" VerticalAlignment="Center" CornerRadius="4" />
        </HorizontalStackPanel&gt;

        <!-- 4. 文件信息模块 -->
        <HorizontalStackPanel Spacing="8" Margin="0,0,0,8">
            <FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE97C;" FontSize="16" VerticalAlignment="Center" />
            <TextBlock Text="文件信息" FontSize="16" FontWeight="SemiBold" VerticalAlignment="Center" />
        </HorizontalStackPanel>
        <UniformGrid Rows="2" Columns="3" Spacing="8" Margin="0,0,0,16">
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="尺寸" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="6000X3000" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="大小" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="3MB" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="DPI" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="72" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="色深" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="12bit" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="评级来源" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="WinRT" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
        </UniformGrid>

        <!-- 5. 设备信息模块 -->
        <HorizontalStackPanel Spacing="8" Margin="0,0,0,8">
            <FontIcon FontFamily="Segoe MDL2 Assets" Glyph="&#xE722;" FontSize="16" VerticalAlignment="Center" />
            <TextBlock Text="设备信息" FontSize="16" FontWeight="SemiBold" VerticalAlignment="Center" />
        </HorizontalStackPanel>
        <UniformGrid Rows="2" Columns="3" Spacing="8">
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="相机" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="SONY A1" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="镜头" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="35mm GM" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="焦距" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="35MM" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="快门" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="1/10" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="光圈" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="F1.2" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
            <Border CornerRadius="4" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,8">
                <VerticalStackPanel Spacing="4">
                    <TextBlock Text="ISO" FontSize="14" Opacity="0.7" HorizontalAlignment="Center" />
                    <TextBlock Text="100" FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </VerticalStackPanel>
            </Border>
        </UniformGrid>
    </VerticalStackPanel>
</ScrollViewer>
```

> （注：文档部分内容可能由 AI 生成）

