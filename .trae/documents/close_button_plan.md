# 高清预览关闭功能修改计划

## 目标
1. 取消点击空白关闭功能
2. 在预览图中间放一个360*50px的关闭按钮
3. 半透明，默认高透，hover、按下时低透明
4. 图标：&#xE70D;（关闭图标）
5. esc键关闭功能保留

## 修改内容

### 1. ImageViewerControl.xaml
- 移除 `BackgroundOverlay` 的 `Tapped` 事件处理
- 在图片预览区域中间添加一个关闭按钮

  ```xml
  <Button
      x:Name="CloseButton"
      Width="360"
      Height="50"
      HorizontalAlignment="Center"
      VerticalAlignment="Bottom"
      Background="Transparent"
      CornerRadius="25"
      BorderBrush="{ThemeResource SubtleFillColorSecondaryBrush}"
      BorderThickness="1"
      Padding="0"
      PointerEntered="CloseButton_PointerPressed">
          Background="Transparent"
          CornerRadius="25"
          BorderBrush="Transparent"
          BorderThickness="1"
          Padding="0,          >
            <FontIcon FontFamily="{StaticResource SymbolThemeFontFamily}" Glyph="&#xE70D;" FontSize="16"/>
        </Button>
  </Grid>
  ```

### 2. ImageViewerControl.xaml.cs
- 移除 `BackgroundOverlay_Tapped` 事件处理
-  `BackgroundOverlay_Tapped` 事件处理中添加关闭按钮的点击事件处理

### 3. ImageViewerControl.xaml.cs
- 移除 `BackgroundOverlay_Tapped` 事件处理
- 添加关闭按钮的点击事件处理

## 预期结果

- ✅ 取消点击空白关闭功能
- ✅ 在预览图中间放一个360*50px的关闭按钮
- ✅ 按钮半透明，默认高透，hover、按下时低透明
- ✅ 图标：&#xE70D;（关闭图标）
- ✅ esc键关闭功能保留

## 风险评估

- **低风险**：修改仅涉及事件处理和UI布局，不影响核心功能
- **中等风险**：需要测试关闭按钮的交互是否正常
- **低风险**：修改是局部的，不会影响其他功能