# PhotoView

一个基于 WinUI 3 的高性能图片浏览器，专为大规模图片集合设计。

## ✨ 特性

### 🚀 高性能

- **大规模图片支持**：轻松处理 10,000+ 张图片
- **虚拟化技术**：智能加载可见区域，内存占用低
- **流畅滚动**：无卡顿、无闪烁、无乱序的滚动体验
- **线程安全**：严格的线程模型确保应用稳定性

### 🖼️ 图片格式支持

支持广泛的图片格式，包括：

- **常见格式**：JPG, JPEG, PNG, GIF, BMP, TIFF, WebP, PSD, PSB
- **RAW 格式**：
  - Canon: CR2, CR3, CRW
  - Nikon: NEF, NRW
  - Sony: ARW, SR2
  - Fujifilm: RAF
  - Olympus: ORF
  - Panasonic/Leica: RW2
  - Pentax: PEF
  - Adobe: DNG
  - Samsung: SRW
  - 其他: RAW, IIQ, 3FR, MEF, MOS, X3F, ERF, DCR, KDC

### 🎯 核心功能

- **缩略图预览**：智能缩略图加载和缓存
- **图片评级**：为图片添加评级标记
- **文件夹导航**：快速浏览文件夹结构
- **连拍分组**：自动识别并分组连拍照片
- **图片筛选**：按格式、评级等条件筛选图片
- **EXIF 信息**：查看图片元数据信息
- **主题切换**：支持亮色/暗色主题
- **多语言支持**：中文、英文界面

## 🛠️ 技术栈

- **框架**：WinUI 3 (Windows App SDK 1.8)
- **运行时**：.NET 8.0
- **语言**：C# 12
- **架构**：MVVM 模式
- **依赖注入**：Microsoft.Extensions.DependencyInjection
- **图片处理**：Magick.NET

### 主要依赖库

| 库名                           | 版本            | 用途         |
| ---------------------------- | ------------- | ---------- |
| Microsoft.WindowsAppSDK      | 1.8.260317003 | WinUI 3 框架 |
| CommunityToolkit.Mvvm        | 8.2.2         | MVVM 工具包   |
| WinUIEx                      | 2.3.0         | WinUI 扩展功能 |
| Magick.NET-Q8-AnyCPU         | 14.12.0       | 图片处理       |
| Newtonsoft.Json              | 13.0.2        | JSON 序列化   |
| Microsoft.Extensions.Hosting | 7.0.1         | 通用主机       |

## 📋 系统要求

- **操作系统**：Windows 10 版本 1809 (build 17763) 或更高版本
- **架构**：x64
- **运行时**：.NET 8.0 Runtime

## 🏗️ 构建项目

### 前置要求

1. 安装 [Visual Studio 2022](https://visualstudio.microsoft.com/) (17.8 或更高版本)
2. 安装以下工作负载：
   - .NET 桌面开发
   - Windows 应用程序开发
3. 安装 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 构建步骤

1. 克隆仓库

```bash
git clone <repository-url>
cd PhotoView
```

1. 打开解决方案

```bash
# 使用 Visual Studio 打开 PhotoView.slnx
# 或使用命令行
dotnet restore
dotnet build
```

1. 运行项目

```bash
dotnet run
```

## 📁 项目结构

```
PhotoView/
├── Activation/          # 应用激活处理
├── Assets/              # 应用资源和图标
├── Behaviors/           # XAML 行为
├── Contracts/           # 接口定义
│   ├── Services/       # 服务接口
│   └── ViewModels/     # ViewModel 接口
├── Controls/            # 自定义控件
├── Converters/          # 值转换器
├── Dialogs/             # 对话框
├── Helpers/             # 辅助类
├── Layouts/             # 自定义布局
├── Models/              # 数据模型
├── Services/            # 服务实现
├── Strings/             # 本地化资源
│   ├── en-us/          # 英文资源
│   └── zh-cn/          # 中文资源
├── Styles/              # 样式定义
├── ViewModels/          # 视图模型
└── Views/               # 视图页面
```

## 🎨 架构设计

### MVVM 架构

项目采用 MVVM (Model-View-ViewModel) 架构模式，确保关注点分离：

- **Model**: 数据模型和业务逻辑
- **View**: XAML 页面和用户界面
- **ViewModel**: 视图模型，连接 Model 和 View

### 依赖注入

使用 `Microsoft.Extensions.DependencyInjection` 进行依赖注入管理：

```csharp
// 服务注册示例
services.AddSingleton<IThumbnailService, ThumbnailService>();
services.AddSingleton<RatingService>();
services.AddTransient<MainViewModel>();
```

### 线程模型

严格的线程模型确保 UI 线程安全：

- UI 对象必须在 UI 线程创建
- 禁止在 async 方法后直接操作 UI
- 使用版本校验防止元素复用错位
- 限制并发加载数量

## 🔧 核心服务

### 缩略图服务 (IThumbnailService)

- 智能缩略图加载和缓存
- 分级加载策略（快速预览 → 目标缩略图）
- 并发加载控制

### 评级服务 (RatingService)

- 图片评级管理
- 评级缓存和持久化
- 批量评级操作

### 文件夹树服务 (FolderTreeService)

- 文件夹结构导航
- 外部设备监听
- 收藏夹管理

### 图片工作区服务 (PreviewWorkspaceService)

- 图片预览加载
- 图片信息管理
- 加载状态跟踪

## 🎯 性能优化

### 虚拟化

- 使用 `ItemsRepeater` 实现虚拟化
- 仅加载可见区域的缩略图
- 智能预加载和缓存策略

### 内存管理

- 限制并发加载数量 (SemaphoreSlim)
- 及时释放不可见图片资源
- 缩略图分级缓存

### 滚动优化

- 节流加载避免频繁更新
- 异步加载防止 UI 阻塞
- 版本校验防止图片错位

## 🌐 本地化

项目支持多语言：

- **中文 (zh-CN)**: 默认语言
- **英文 (en-US)**

资源文件位于 `Strings/` 目录下。

## 📝 开发规范

详细的开发规范请参考 [.trae/rules/项目规则.md](.trae/rules/项目规则.md)，包括：

- 线程模型规则
- UI 开发规范
- 性能优化要求
- 异常处理规范

## 📄 许可证

本项目采用 MIT 许可证。详见 [LICENSE](LICENSE) 文件。

## 👤 作者

**Vicco**

- Publisher: CN=Vicco

## 🙏 致谢

感谢以下开源项目：

- [Windows App SDK](https://github.com/microsoft/WindowsAppSDK)
- [CommunityToolkit](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- [Magick.NET](https://github.com/dlemstra/Magick.NET)
- [WinUIEx](https://github.com/dotMorten/WinUIEx)

***

