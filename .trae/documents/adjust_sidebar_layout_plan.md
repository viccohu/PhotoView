# 侧边导航布局调整 - 实现计划

## [ ] 任务 1: 取消后退按键
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 移除 NavigationView 的 IsBackButtonVisible 属性（设置为 Collapsed）
  - 移除相关的后退按钮绑定和功能
- **Success Criteria**:
  - NavigationView 上不再显示后退按钮
- **Test Requirements**:
  - `programmatic` TR-1.1: NavigationView.IsBackButtonVisible 设置为 Collapsed
  - `human-judgement` TR-1.2: UI 上不再显示后退按钮
- **Notes**: 还需要移除 ShellPage.xaml.cs 中的键盘快捷键相关代码

## [ ] 任务 2: 调整标题栏和导航栏布局
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 标题栏宽度拉通（覆盖整个窗口宽度）
  - 导航栏高度下调至标题栏下方
  - 调整 AppTitleBar 的布局和样式
- **Success Criteria**:
  - 标题栏横向贯穿整个窗口
  - 导航栏从标题栏下方开始显示
- **Test Requirements**:
  - `human-judgement` TR-2.1: 标题栏横跨整个窗口宽度
  - `human-judgement` TR-2.2: 导航栏从标题栏下方开始
- **Notes**: 需要修改 Grid 布局结构和 ZIndex

## [ ] 任务 3: 实现设置按钮切换功能
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 点击设置按钮时，在导航菜单和设置页面之间切换
  - 当再次点击设置按钮时，返回到之前的导航页面
  - 实现页面缓存机制
- **Success Criteria**:
  - 第一次点击设置按钮导航到设置页面
  - 再次点击设置按钮返回到之前的页面
  - MainPage 和 CollectPage 保持缓存状态
- **Test Requirements**:
  - `programmatic` TR-3.1: 记录当前页面状态
  - `human-judgement` TR-3.2: 点击设置按钮可以在导航和设置页面前后切换
  - `human-judgement` TR-3.3: 切换回之前页面时，页面状态保持不变
- **Notes**: 需要在 ShellViewModel 和 ShellPage 中实现状态跟踪和切换逻辑

## [ ] 任务 4: 实现页面缓存
- **Priority**: P1
- **Depends On**: 任务 3
- **Description**: 
  - 设置 Frame 的 CacheSize
  - 确保 MainPage 和 CollectPage 被缓存
- **Success Criteria**:
  - 页面切换时不重新初始化
  - 保持页面的滚动位置和状态
- **Test Requirements**:
  - `human-judgement` TR-4.1: 从设置页面返回时，MainPage 的滚动位置保持不变
  - `human-judgement` TR-4.2: 页面切换时 ViewModel 状态保持
- **Notes**: 需要在 Frame 上设置 CacheSize 属性
