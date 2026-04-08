# 高清预览信息栏完善实施计划

## [ ] Task 1: 移除右上角关闭按钮
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 从 ImageViewerControl.xaml 中移除右上角的关闭按钮
- **Success Criteria**:
  - 关闭按钮已移除
- **Test Requirements**:
  - `human-judgement` TR-1.1: 右上角不再显示关闭按钮

## [ ] Task 2: 完善文件名显示（可选择复制）
- **Priority**: P0
- **Depends On**: Task 1
- **Description**: 
  - 使用 TextBox 只读展示文件名
  - 支持文本选择和复制
  - 点击可复制文件名到剪贴板
- **Success Criteria**:
  - 文件名可显示、可选择、可复制
- **Test Requirements**:
  - `human-judgement` TR-2.1: 文件名可选择复制
  - `human-judgement` TR-2.2: 点击文件名可复制到剪贴板

## [ ] Task 3: 添加评级控件（支持点选评级）
- **Priority**: P0
- **Depends On**: Task 2
- **Description**: 
  - 在信息栏添加 RatingControl 控件
  - 支持点选评级
  - 兼容快捷键评级操作（数字键1-5）
  - 评级后同步更新 ImageFileInfo.Rating
  - 退出高清预览后缩略图评级信息自动刷新
- **Success Criteria**:
  - 评级控件可正常使用
  - 评级信息同步更新
- **Test Requirements**:
  - `human-judgement` TR-3.1: 点击星星可设置评级
  - `human-judgement` TR-3.2: 快捷键1-5可设置评级
  - `human-judgement` TR-3.3: 退出预览后缩略图评级正确显示

## [ ] Task 4: 完善日期时间显示
- **Priority**: P0
- **Depends On**: Task 3
- **Description**: 
  - 使用 ExifData 中的 DateTaken 数据
  - 日期格式：yyyy-MM-dd
  - 时间格式：HH:mm:ss
  - 点击可复制
- **Success Criteria**:
  - 日期时间正确显示 EXIF 数据
- **Test Requirements**:
  - `human-judgement` TR-4.1: 日期时间显示正确
  - `human-judgement` TR-4.2: 点击可复制

## [ ] Task 5: 完善文件信息区域
- **Priority**: P0
- **Depends On**: Task 4
- **Description**: 
  - 添加文件尺寸（Width x Height）
  - 添加文件大小（已实现）
  - 添加图片 DPI（DpiX x DpiY）
  - 添加图片色深（BitDepth）
  - 添加评级来源（RatingSource）
  - 使用 Border 框住整个区域
  - 每个信息项之间有分割线
  - 点击每个信息项可复制对应内容
- **Success Criteria**:
  - 所有文件信息正确显示
  - 布局整齐美观
- **Test Requirements**:
  - `human-judgement` TR-5.1: 文件尺寸显示正确
  - `human-judgement` TR-5.2: DPI 显示正确
  - `human-judgement` TR-5.3: 色深显示正确
  - `human-judgement` TR-5.4: 评级来源显示正确
  - `human-judgement` TR-5.5: 点击可复制

## [ ] Task 6: 完善设备信息区域
- **Priority**: P0
- **Depends On**: Task 5
- **Description**: 
  - 添加相机型号（CameraModel）
  - 添加镜头型号（LensModel）
  - 添加镜头焦距/等效焦距（FocalLength / FocalLengthInFilm）
  - 添加曝光时间（ExposureTime）
  - 添加光圈（FNumber）
  - 添加 ISO（ISOSpeed）
  - 添加曝光程序（ExposureProgram）
  - 添加曝光补偿（ExposureBias）
  - 添加闪光灯状态（Flash）
  - 使用 Border 框住整个区域
  - 每个信息项之间有分割线
  - 点击每个信息项可复制对应内容
- **Success Criteria**:
  - 所有设备信息正确显示
  - 布局整齐美观
- **Test Requirements**:
  - `human-judgement` TR-6.1: 相机型号显示正确
  - `human-judgement` TR-6.2: 镜头信息显示正确
  - `human-judgement` TR-6.3: 曝光参数显示正确
  - `human-judgement` TR-6.4: 点击可复制

## [ ] Task 7: 完善文件路径区域
- **Priority**: P0
- **Depends On**: Task 6
- **Description**: 
  - 文件格式标签（不同格式颜色区分）
    - JPG: 蓝色
    - PNG: 绿色
    - RAW (CR2/NEF/ARW等): 橙色
    - 其他: 灰色
  - 文件路径（可点击跳转到文件管理器）
  - 复制按钮（小图标，点击复制路径）
- **Success Criteria**:
  - 文件格式颜色正确
  - 点击路径可跳转
  - 复制按钮功能正常
- **Test Requirements**:
  - `human-judgement` TR-7.1: JPG 文件显示蓝色
  - `human-judgement` TR-7.2: PNG 文件显示绿色
  - `human-judgement` TR-7.3: RAW 文件显示橙色
  - `human-judgement` TR-7.4: 点击路径可打开文件管理器
  - `human-judgement` TR-7.5: 复制按钮可复制路径

## [ ] Task 8: 添加信息项点击复制功能
- **Priority**: P1
- **Depends On**: Task 5-7
- **Description**: 
  - 为每个信息项添加点击效果（类似按钮）
  - 点击后复制对应信息到剪贴板
  - 显示复制成功提示（可选）
- **Success Criteria**:
  - 所有信息项可点击复制
- **Test Requirements**:
  - `human-judgement` TR-8.1: 点击文件尺寸可复制
  - `human-judgement` TR-8.2: 点击相机型号可复制
  - `human-judgement` TR-8.3: 点击路径可复制

## [ ] Task 9: 更新 ImageViewerViewModel
- **Priority**: P0
- **Depends On**: None
- **Description**: 
  - 添加新的属性绑定：
    - DpiX, DpiY
    - BitDepth
    - RatingSource
    - LensModel
    - FocalLength, FocalLengthInFilm
    - ExposureTime, FNumber, ISOSpeed
    - ExposureProgram, ExposureBias
    - Flash
  - 添加评级命令
  - 添加复制信息命令
- **Success Criteria**:
  - ViewModel 包含所有需要的属性
- **Test Requirements**:
  - `programmatic` TR-9.1: 编译成功

## [ ] Task 10: 测试与验证
- **Priority**: P1
- **Depends On**: Task 1-9
- **Description**: 
  - 测试所有信息显示正确
  - 测试评级功能
  - 测试复制功能
  - 测试跳转功能
- **Success Criteria**:
  - 所有功能正常工作
- **Test Requirements**:
  - `human-judgement` TR-10.1: 所有信息显示正确
  - `human-judgement` TR-10.2: 评级功能正常
  - `human-judgement` TR-10.3: 复制功能正常
