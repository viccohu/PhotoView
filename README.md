# Sivo One

## 软件描述介绍

**PhotoView** 是一个专注于摄影选片的高性能图片浏览器，专为解决多文件格式拍摄（如 JPG + RAW 同时拍摄）的选片管理痛点而设计。

### 核心价值

- **格式关联**：同照片的 JPG 与 RAW 文件自动关联，评级同步
- **高效选片**：多种选片模式，满足不同场景需求
- **专业管理**：预删除标记、多条件筛选、灵活导出
- **丝滑体验**：物理特性缩放、流畅解码、Win11 风格 UI

***

## 核心功能介绍

### 1. 多种选片模式

#### 相册模式

- 像文件管理器一样的缩略图平铺展示
- 直观的网格布局，快速浏览大量图片
- 支持自定义缩略图尺寸

#### 预览模式

- 多文件夹同时载入，合并选片
- 双窗口对比功能：同时查看两张图片进行对比
- 连续模式选片：专门针对连拍或多张类似图片的快速选片

### 2. 快捷评级与同步

#### 自动格式关联

- 自动识别同照片的不同格式（JPG + RAW）
- 自动折叠显示，界面更整洁
- 评级操作自动同步到所有关联格式

#### 智能筛选

- 多条件组合筛选
- 按评级、格式、文件夹等筛选
- 快速定位目标照片

#### 预删除机制

- 删除操作只做标记，不立即删除文件
- 统一管理待删除项目
- 支持撤销和确认删除

### 3. 灵活导出功能

- 图片与 RAW 文件分开管理导出
- 支持自定义导出文件夹结构
- 批量导出选中图片
- 导出时保留原始信息

### 4. 丝滑浏览体验

- 高性能图片解码，打开大图无延迟
- 物理特性缩放：带有阻尼和惯性的自然缩放体验
- 流畅的平移操作
- 支持左右镜像、上下镜像

### 5. Win11 现代 UI

- 原生 WinUI 3 控件
- 流畅的动画效果
- 简洁现代的界面设计
- 符合 Win11 设计规范

***

## 其他特性

（小特性，非核心）

- 连拍照片自动分组
- 完整 EXIF 信息查看
- 亮色/暗色主题切换
- 多语言支持（中文/英文）
- 外部设备自动检测

***

## 技术栈

- **框架**：WinUI 3 (Windows App SDK 1.8)
- **运行时**：.NET 8.0
- **语言**：C# 12
- **架构**：x64
- **MVVM 工具**：CommunityToolkit.Mvvm 8.2.2
- **图片处理**：Magick.NET 14.12.0
- **依赖注入**：Microsoft.Extensions.DependencyInjection

### 支持的图片格式

**常见格式**：JPG, JPEG, PNG, GIF, BMP, TIFF, WebP, PSD, PSB

**RAW 格式**：

- Canon: CR2, CR3, CRW
- Nikon: NEF, NRW
- Sony: ARW, SR2
- Fujifilm: RAF
- Olympus: ORF
- Panasonic/Leica: RW2
- Pentax: PEF
- Adobe: DNG
- 其他: RAW, IIQ, 3FR, MEF, MOS, X3F, ERF, DCR, KDC, SRW

***

## 项目结构

```
PhotoView/
├── Activation/              # 应用激活处理
│   ├── ActivationHandler.cs
│   └── DefaultActivationHandler.cs
├── Assets/              # 应用资源和图标
├── Behaviors/          # XAML 行为
├── Contracts/          # 接口定义
│   ├── Services/       # 服务接口
│   └── ViewModels/     # ViewModel 接口
├── Controls/            # 自定义控件
│   ├── CollectSourcePane.xaml/.cs
│   ├── ImageInfoPanelControl.xaml/.cs
│   ├── ImageViewerControl.xaml/.cs
│   ├── NavigationPaneExplorer.xaml/.cs
│   ├── PreviewImageCanvasControl.xaml/.cs
│   ├── ShimmerControl.xaml/.cs
│   └── ThumbnailItemControl.xaml/.cs
├── Converters/          # 值转换器
├── Dialogs/            # 对话框
│   ├── DeleteConfirmDialog.xaml/.cs
│   └── ExportDialog.xaml/.cs
├── Helpers/            # 辅助类
├── Layouts/            # 自定义布局
│   └── JustifiedLayout.cs
├── Models/            # 数据模型
│   ├── BurstPhotoGroup.cs
│   ├── ImageFileInfo.cs
│   ├── FolderNode.cs
│   ├── PreviewSource.cs
│   └── ThumbnailSize.cs
├── Services/          # 服务实现
│   ├── RatingService.cs
│   ├── ThumbnailService.cs
│   ├── ExifService.cs
│   ├── PreviewWorkspaceService.cs
│   ├── FolderTreeService.cs
│   └── NavigationPaneService.cs
├── Strings/            # 本地化资源
│   ├── en-us/          # 英文
│   └── zh-cn/          # 中文
├── Styles/            # 样式定义
├── ViewModels/          # 视图模型
│   ├── MainViewModel.cs
│   ├── CollectViewModel.cs
│   ├── FilterViewModel.cs
│   └── ImageViewerViewModel.cs
├── Views/              # 视图页面
│   ├── MainPage.xaml/.cs
│   ├── CollectPage.xaml/.cs
│   └── SettingsPage.xaml/.cs
├── App.xaml/.cs        # 应用入口
└── PhotoView.csproj # 项目文件
```

***

## 快捷键说明

按 **F1** 可以在软件内打开快捷键说明对话框。

### 通用快捷键

| 快捷键 | 说明 |
|--------|------|
| `F1` | 打开快捷键说明 |
| `←` / `↑` / `→` / `↓` | 切换图片或移动当前选择 |
| `0-5` / `Num 0-5` | 设置或清除评级 |

### 相册页快捷键

| 快捷键 | 说明 |
|--------|------|
| `Space` | 打开或关闭查看器 |
| `Esc` | 清空选择 |
| `Ctrl + A` | 全选 |
| `Delete` | 标记或取消预删除 |

### 查看器快捷键

| 快捷键 | 说明 |
|--------|------|
| `Space` / `Esc` | 关闭查看器 |
| `←` / `→` | 上一张/下一张图片 |
| `0-5` / `Num 0-5` | 设置或清除评级 |

### 预览页快捷键

| 快捷键 | 说明 |
|--------|------|
| `Esc` | 重置缩放 |
| `Tab` | 双页模式下切换左右预览焦点 |
| `Space` | 多个缩略图选中时，收拢为当前预览图 |
| `Delete` | 标记或取消预删除 |
| `←` / `↑` / `→` / `↓` | 切换图片 |
| `0-5` / `Num 0-5` | 设置或清除评级 |

***

## 系统要求

- **操作系统**：Windows 10 版本 1809 (build 17763) 或更高版本
- **推荐系统**：Windows 11（最佳体验）
- **架构**：x64
- **运行时**：.NET 8.0 Runtime

***

## 构建项目

### 前置要求

1. Visual Studio 2022 (17.8 或更高版本)
2. .NET 8.0 SDK
3. Windows 应用程序开发工作负载

### 构建步骤

```bash
# 克隆项目
git clone <repository-url>
cd PhotoView

# 还原依赖
dotnet restore

# 构建项目
dotnet build

# 运行项目
dotnet run
```

***



