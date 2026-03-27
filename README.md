# PhotoPro 界面提取

本目录包含 PhotoPro 应用的所有界面和界面逻辑文件，便于移植到其他项目中。

## 目录结构

```
interface/
├── Views/              # XAML 界面文件
│   ├── ShellPage.xaml/.cs
│   ├── MainPage.xaml/.cs
│   ├── CollectPage.xaml/.cs
│   └── SettingsPage.xaml/.cs
├── ViewModels/         # 视图模型
│   ├── ShellViewModel.cs
│   ├── MainViewModel.cs
│   ├── CollectViewModel.cs
│   └── SettingsViewModel.cs
├── Styles/             # 样式资源
├── Common/             # 公共组件（瀑布流布局等）
├── Behaviors/          # XAML 行为
├── Helpers/            # 辅助类
├── Contracts/          # 接口定义
├── Models/             # 数据模型
├── Services/           # 服务实现
├── Assets/             # 资源文件
├── Strings/            # 多语言资源
├── Core/               # 核心服务（来自 PhotoPro.Core）
├── App.xaml/.cs        # 应用入口
├── MainWindow.xaml/.cs # 主窗口
└── Usings.cs           # 全局 using
```

## 主要界面说明

1. **ShellPage** - 应用主框架，包含导航菜单
2. **MainPage** - 主页面，照片浏览
3. **CollectPage** - 收藏页面
4. **SettingsPage** - 设置页面

## 依赖项

- CommunityToolkit.Mvvm (>= 7.1.2)
- Microsoft.Extensions.Hosting (>= 6.0.1)
- Microsoft.WindowsAppSDK (>= 1.2.230118.102)
- Microsoft.Xaml.Behaviors.WinUI.Managed (>= 2.0.9)
- WinUIEx (>= 2.1)

## 移植说明

1. 将此目录的文件复制到目标项目中
2. 调整命名空间以匹配目标项目
3. 更新依赖项引用
4. 修改 App.xaml.cs 中的服务注册配置
5. 根据需要调整数据绑定和业务逻辑
