using System;
using Windows.Foundation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FastPick.Controls;

/// <summary>
/// 物理驱动图片查看器控件
/// 支持：惯性拖动、鼠标中心缩放、阻尼边界、100%吸附
/// </summary>
[TemplatePart(Name = "Container", Type = typeof(Grid))]
[TemplatePart(Name = "Image", Type = typeof(Image))]
[TemplatePart(Name = "ScaleTransform", Type = typeof(ScaleTransform))]
[TemplatePart(Name = "RotateTransform", Type = typeof(RotateTransform))]
[TemplatePart(Name = "TranslateTransform", Type = typeof(TranslateTransform))]
public sealed class PhysicsImageViewer : Control
{
    #region 模板部件
    
    private Grid? _container;
    private Image? _image;
    private ScaleTransform? _scaleTransform;
    private RotateTransform? _rotateTransform;
    private TranslateTransform? _translateTransform;
    
    #endregion
    
    #region 物理状态
    
    private double _zoomScale = 1.0;
    private double _targetZoomScale = 1.0;
    private double _translateX = 0;
    private double _translateY = 0;
    private double _velocityX = 0;
    private double _velocityY = 0;
    private double _rotation = 0;
    private bool _flipHorizontal = false;
    private bool _flipVertical = false;
    
    private bool _isDragging = false;
    private Point _lastDragPoint;
    
    private bool _hasZoomAnchor = false;
    private double _zoomAnchorImgX = 0;
    private double _zoomAnchorImgY = 0;
    private double _zoomAnchorScreenX = 0;
    private double _zoomAnchorScreenY = 0;
    
    private bool _justSnappedTo100Percent = false;
    private int _snapStayCounter = 0;
    
    #endregion
    
    #region 物理参数
    
    /// <summary>
    /// 惯性衰减系数（影响拖动后的滑动距离）
    /// 值越大：滑动距离越远，手感越滑
    /// 值越小：滑动距离越短，手感越粘
    /// 推荐范围：0.85 - 0.95
    /// </summary>
    private const double InertiaDamping = 0.92;
    
    /// <summary>
    /// 缩放缓动系数（影响缩放的响应速度）
    /// 值越大：缩放响应越快，过渡越生硬
    /// 值越小：缩放响应越慢，过渡越平滑
    /// 推荐范围：0.1 - 0.3
    /// </summary>
    private const double ZoomEasingFactor = 0.15;
    
    /// <summary>
    /// 边界阻尼系数（影响拖动超出边界时的阻力感）
    /// 值越大：越难拖出边界，阻力感越强
    /// 值越小：越容易拖出边界，阻力感越弱
    /// 推荐范围：0.2 - 0.4
    /// </summary>
    private const double BoundaryDamping = 0.3;
    
    /// <summary>
    /// 回弹速度系数（影响边界回弹的速度）
    /// 值越大：回弹速度越快，手感越硬
    /// 值越小：回弹速度越慢，手感越软
    /// 推荐范围：0.1 - 0.2
    /// </summary>
    private const double SpringBackFactor = 0.12;
    
    /// <summary>
    /// 速度阈值（用于判断惯性是否停止）
    /// 值越大：停止得越早
    /// 值越小：滑动得越久
    /// 推荐范围：0.1 - 1.0
    /// </summary>
    private const double VelocityThreshold = 0.5;
    
    /// <summary>
    /// 100% 吸附阈值（影响吸附的灵敏度）
    /// 值越大：吸附范围越大，越容易吸附到 100%
    /// 值越小：吸附范围越小，越难吸附到 100%
    /// 推荐范围：0.03 - 0.1
    /// </summary>
    private const double SnapThreshold = 0.05;
    
    /// <summary>
    /// 100% 吸附停留次数（影响离开 100% 的难度）
    /// 值越大：需要滚动更多次才能离开 100%
    /// 值越小：很容易离开 100%
    /// 推荐范围：2 - 6
    /// </summary>
    private const int SnapStayCount = 4;
    
    #endregion
    
    #region 依赖属性
    
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(BitmapImage), typeof(PhysicsImageViewer), 
            new PropertyMetadata(null, OnSourceChanged));
    
    public static readonly DependencyProperty MinZoomProperty =
        DependencyProperty.Register(nameof(MinZoom), typeof(double), typeof(PhysicsImageViewer), 
            new PropertyMetadata(0.1));
    
    public static readonly DependencyProperty MaxZoomProperty =
        DependencyProperty.Register(nameof(MaxZoom), typeof(double), typeof(PhysicsImageViewer), 
            new PropertyMetadata(8.0));
    
    public static readonly DependencyProperty OriginalWidthProperty =
        DependencyProperty.Register(nameof(OriginalWidth), typeof(double), typeof(PhysicsImageViewer), 
            new PropertyMetadata(0.0));
    
    public static readonly DependencyProperty OriginalHeightProperty =
        DependencyProperty.Register(nameof(OriginalHeight), typeof(double), typeof(PhysicsImageViewer), 
            new PropertyMetadata(0.0));
    
    public static readonly DependencyProperty ZoomPercentageProperty =
        DependencyProperty.Register(nameof(ZoomPercentage), typeof(int), typeof(PhysicsImageViewer), 
            new PropertyMetadata(100));
    
    #endregion
    
    #region 公共属性
    
    public BitmapImage? Source
    {
        get => (BitmapImage?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
    
    public double MinZoom
    {
        get => (double)GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }
    
    public double MaxZoom
    {
        get => (double)GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }
    
    public double OriginalWidth
    {
        get => (double)GetValue(OriginalWidthProperty);
        set => SetValue(OriginalWidthProperty, value);
    }
    
    public double OriginalHeight
    {
        get => (double)GetValue(OriginalHeightProperty);
        set => SetValue(OriginalHeightProperty, value);
    }
    
    public int ZoomPercentage
    {
        get => (int)GetValue(ZoomPercentageProperty);
        private set => SetValue(ZoomPercentageProperty, value);
    }
    
    public double CurrentZoomScale => _zoomScale;
    public double CurrentRotation => _rotation;
    public bool IsFlippedHorizontal => _flipHorizontal;
    public bool IsFlippedVertical => _flipVertical;
    
    #endregion
    
    #region 公共事件
    
    public event EventHandler<int>? ZoomChanged;
    public event EventHandler? Reset;
    
    #endregion
    
    public PhysicsImageViewer()
    {
        DefaultStyleKey = typeof(PhysicsImageViewer);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering += OnPhysicsRendering;
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnPhysicsRendering;
    }
    
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        
        _container = GetTemplateChild("Container") as Grid;
        _image = GetTemplateChild("Image") as Image;
        _scaleTransform = GetTemplateChild("ScaleTransform") as ScaleTransform;
        _rotateTransform = GetTemplateChild("RotateTransform") as RotateTransform;
        _translateTransform = GetTemplateChild("TranslateTransform") as TranslateTransform;
        
        if (_container != null)
        {
            _container.PointerWheelChanged += OnPointerWheelChanged;
            _container.PointerPressed += OnPointerPressed;
            _container.PointerMoved += OnPointerMoved;
            _container.PointerReleased += OnPointerReleased;
            _container.PointerExited += OnPointerExited;
            _container.DoubleTapped += OnDoubleTapped;
        }
    }
    
    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PhysicsImageViewer viewer)
        {
            viewer.ResetZoom();
        }
    }
    
    #region 公共方法
    
    public void ResetZoom()
    {
        _targetZoomScale = 1.0;
        _velocityX = 0;
        _velocityY = 0;
        _hasZoomAnchor = false;
        _rotation = 0;
        _flipHorizontal = false;
        _flipVertical = false;
        _justSnappedTo100Percent = false;
        _snapStayCounter = 0;
        
        if (_image != null && _container != null)
        {
            var (imageWidth, imageHeight) = GetRotatedImageSize();
            if (imageWidth <= _container.ActualWidth && imageHeight <= _container.ActualHeight)
            {
                _translateX = 0;
                _translateY = 0;
            }
        }
        
        ApplyTransform();
        UpdateZoomPercentage();
        Reset?.Invoke(this, EventArgs.Empty);
    }
    
    public void SetZoom(double scale)
    {
        var (minZoom, maxZoom) = GetZoomLimits();
        _targetZoomScale = Math.Max(minZoom, Math.Min(maxZoom, scale));
        _hasZoomAnchor = false;
        UpdateZoomPercentage();
    }
    
    public void RotateLeft()
    {
        _rotation -= 90;
        NormalizeRotation();
        ApplyTransform();
    }
    
    public void RotateRight()
    {
        _rotation += 90;
        NormalizeRotation();
        ApplyTransform();
    }
    
    public void FlipHorizontal()
    {
        _flipHorizontal = !_flipHorizontal;
        ApplyTransform();
    }
    
    public void FlipVertical()
    {
        _flipVertical = !_flipVertical;
        ApplyTransform();
    }
    
    #endregion
    
    #region 物理引擎
    
    private void OnPhysicsRendering(object sender, object e)
    {
        if (_image?.Source == null || _container == null) return;
        
        bool needsUpdate = false;
        bool isZooming = Math.Abs(_zoomScale - _targetZoomScale) > 0.0001;
        
        if (isZooming)
        {
            var oldScale = _zoomScale;
            _zoomScale += (_targetZoomScale - _zoomScale) * ZoomEasingFactor;
            
            if (_hasZoomAnchor)
            {
                _translateX = _zoomAnchorScreenX - _zoomAnchorImgX * _zoomScale;
                _translateY = _zoomAnchorScreenY - _zoomAnchorImgY * _zoomScale;
            }
            else
            {
                var scaleRatio = _zoomScale / oldScale;
                _translateX *= scaleRatio;
                _translateY *= scaleRatio;
            }
            
            ClampTranslation();
            needsUpdate = true;
        }
        else if (_hasZoomAnchor)
        {
            _hasZoomAnchor = false;
        }
        
        if (!isZooming && !_isDragging && 
            (Math.Abs(_velocityX) > VelocityThreshold || Math.Abs(_velocityY) > VelocityThreshold))
        {
            _translateX += _velocityX;
            _translateY += _velocityY;
            _velocityX *= InertiaDamping;
            _velocityY *= InertiaDamping;
            needsUpdate = true;
        }
        
        if (!isZooming)
        {
            needsUpdate |= ApplyBoundsWithDamping();
        }
        
        if (needsUpdate)
        {
            ApplyTransform();
            UpdateZoomPercentage();
        }
    }
    
    #endregion
    
    #region 输入处理
    
    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_image?.Source == null || _container == null) return;
        
        var pointer = e.GetCurrentPoint(_container);
        var mouse = pointer.Position;
        var delta = pointer.Properties.MouseWheelDelta;
        
        if (delta == 0) return;
        
        double scaleFactor = Math.Pow(1.0015, delta);
        double newTarget = _targetZoomScale * scaleFactor;
        
        var (minZoom, maxZoom) = GetZoomLimits();
        
        double originalScaleForFit = 1.0;
        if (OriginalWidth > 0 && OriginalHeight > 0)
        {
            originalScaleForFit = CalculateOriginalScale();
        }
        
        if (_justSnappedTo100Percent)
        {
            _snapStayCounter++;
            if (_snapStayCounter < SnapStayCount)
            {
                newTarget = originalScaleForFit;
            }
            else
            {
                _justSnappedTo100Percent = false;
                _snapStayCounter = 0;
            }
        }
        else
        {
            bool wasBelow100 = _targetZoomScale < originalScaleForFit * (1.0 - SnapThreshold);
            bool wasAbove100 = _targetZoomScale > originalScaleForFit * (1.0 + SnapThreshold);
            bool willBeAbove100 = newTarget > originalScaleForFit * (1.0 + SnapThreshold);
            bool willBeBelow100 = newTarget < originalScaleForFit * (1.0 - SnapThreshold);
            bool isNear100 = Math.Abs(_targetZoomScale - originalScaleForFit) < originalScaleForFit * SnapThreshold * 1.5;
            
            if ((wasBelow100 && willBeAbove100) || (wasAbove100 && willBeBelow100) || isNear100)
            {
                newTarget = originalScaleForFit;
                _justSnappedTo100Percent = true;
                _snapStayCounter = 0;
            }
        }
        
        newTarget = Math.Clamp(newTarget, minZoom, maxZoom);
        
        var containerCenterX = _container.ActualWidth / 2;
        var containerCenterY = _container.ActualHeight / 2;
        
        var mouseRelativeToCenterX = mouse.X - containerCenterX;
        var mouseRelativeToCenterY = mouse.Y - containerCenterY;
        
        _zoomAnchorImgX = (mouseRelativeToCenterX - _translateX) / _zoomScale;
        _zoomAnchorImgY = (mouseRelativeToCenterY - _translateY) / _zoomScale;
        _zoomAnchorScreenX = mouseRelativeToCenterX;
        _zoomAnchorScreenY = mouseRelativeToCenterY;
        _hasZoomAnchor = true;
        
        _targetZoomScale = newTarget;
        e.Handled = true;
    }
    
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!CanPan() && !_isDragging) return;
        
        var pointer = e.GetCurrentPoint(_container);
        if (!pointer.Properties.IsLeftButtonPressed) return;
        
        _isDragging = true;
        _lastDragPoint = pointer.Position;
        _container!.CapturePointer(e.Pointer);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        
        _velocityX = 0;
        _velocityY = 0;
        _hasZoomAnchor = false;
    }
    
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        
        var pointer = e.GetCurrentPoint(_container);
        var currentPoint = pointer.Position;
        
        var deltaX = currentPoint.X - _lastDragPoint.X;
        var deltaY = currentPoint.Y - _lastDragPoint.Y;
        
        if (CanPanHorizontal())
        {
            _translateX += deltaX;
            _velocityX = deltaX;
        }
        else
        {
            _velocityX = 0;
        }
        
        if (CanPanVertical())
        {
            _translateY += deltaY;
            _velocityY = deltaY;
        }
        else
        {
            _velocityY = 0;
        }
        
        _lastDragPoint = currentPoint;
        ApplyTransform();
    }
    
    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        
        _isDragging = false;
        _container!.ReleasePointerCapture(e.Pointer);
        UpdateCursor();
    }
    
    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            ProtectedCursor = null;
        }
    }
    
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        ResetZoom();
    }
    
    #endregion
    
    #region 边界处理
    
    private void ClampTranslation()
    {
        if (_image?.Source == null || _container == null) return;
        
        var containerWidth = _container.ActualWidth;
        var containerHeight = _container.ActualHeight;
        
        if (containerWidth <= 0 || containerHeight <= 0) return;
        
        var (imageWidth, imageHeight) = GetRotatedImageSize();
        
        if (imageWidth <= containerWidth)
        {
            _translateX = 0;
        }
        else
        {
            var maxTranslateX = imageWidth / 2 - containerWidth / 2;
            _translateX = Math.Max(-maxTranslateX, Math.Min(maxTranslateX, _translateX));
        }
        
        if (imageHeight <= containerHeight)
        {
            _translateY = 0;
        }
        else
        {
            var maxTranslateY = imageHeight / 2 - containerHeight / 2;
            _translateY = Math.Max(-maxTranslateY, Math.Min(maxTranslateY, _translateY));
        }
    }
    
    private bool ApplyBoundsWithDamping()
    {
        if (_image?.Source == null || _container == null) return false;
        
        var containerWidth = _container.ActualWidth;
        var containerHeight = _container.ActualHeight;
        
        if (containerWidth <= 0 || containerHeight <= 0) return false;
        
        var (imageWidth, imageHeight) = GetRotatedImageSize();
        bool needsUpdate = false;
        
        if (imageWidth <= containerWidth)
        {
            if (Math.Abs(_translateX) > 0.5)
            {
                _translateX *= (1 - SpringBackFactor);
                _velocityX = 0;
                needsUpdate = true;
            }
            else if (Math.Abs(_translateX) > 0.01)
            {
                _translateX = 0;
                needsUpdate = true;
            }
        }
        else
        {
            var maxTranslateX = imageWidth / 2 - containerWidth / 2;
            
            if (_translateX > maxTranslateX)
            {
                if (!_isDragging)
                {
                    _translateX += (maxTranslateX - _translateX) * SpringBackFactor;
                    _velocityX = 0;
                }
                else
                {
                    _translateX = maxTranslateX + (_translateX - maxTranslateX) * BoundaryDamping;
                    _velocityX *= -0.3;
                }
                needsUpdate = true;
            }
            else if (_translateX < -maxTranslateX)
            {
                if (!_isDragging)
                {
                    _translateX += (-maxTranslateX - _translateX) * SpringBackFactor;
                    _velocityX = 0;
                }
                else
                {
                    _translateX = -maxTranslateX + (_translateX + maxTranslateX) * BoundaryDamping;
                    _velocityX *= -0.3;
                }
                needsUpdate = true;
            }
        }
        
        if (imageHeight <= containerHeight)
        {
            if (Math.Abs(_translateY) > 0.5)
            {
                _translateY *= (1 - SpringBackFactor);
                _velocityY = 0;
                needsUpdate = true;
            }
            else if (Math.Abs(_translateY) > 0.01)
            {
                _translateY = 0;
                needsUpdate = true;
            }
        }
        else
        {
            var maxTranslateY = imageHeight / 2 - containerHeight / 2;
            
            if (_translateY > maxTranslateY)
            {
                if (!_isDragging)
                {
                    _translateY += (maxTranslateY - _translateY) * SpringBackFactor;
                    _velocityY = 0;
                }
                else
                {
                    _translateY = maxTranslateY + (_translateY - maxTranslateY) * BoundaryDamping;
                    _velocityY *= -0.3;
                }
                needsUpdate = true;
            }
            else if (_translateY < -maxTranslateY)
            {
                if (!_isDragging)
                {
                    _translateY += (-maxTranslateY - _translateY) * SpringBackFactor;
                    _velocityY = 0;
                }
                else
                {
                    _translateY = -maxTranslateY + (_translateY + maxTranslateY) * BoundaryDamping;
                    _velocityY *= -0.3;
                }
                needsUpdate = true;
            }
        }
        
        return needsUpdate;
    }
    
    #endregion
    
    #region 辅助方法
    
    private void ApplyTransform()
    {
        if (_scaleTransform == null || _rotateTransform == null || _translateTransform == null) return;
        
        _scaleTransform.ScaleX = _zoomScale * (_flipHorizontal ? -1 : 1);
        _scaleTransform.ScaleY = _zoomScale * (_flipVertical ? -1 : 1);
        _rotateTransform.Angle = _rotation;
        _translateTransform.X = _translateX;
        _translateTransform.Y = _translateY;
    }
    
    private void UpdateZoomPercentage()
    {
        if (OriginalWidth <= 0 || OriginalHeight <= 0)
        {
            ZoomPercentage = (int)Math.Round(_zoomScale * 100);
            ZoomChanged?.Invoke(this, ZoomPercentage);
            return;
        }
        
        var fitScale = CalculateFitToScreenScale();
        var dpiScale = XamlRoot?.RasterizationScale ?? 1.0;
        
        if (fitScale > 0 && !double.IsNaN(fitScale) && !double.IsInfinity(fitScale))
        {
            var originalPercentage = (_zoomScale * fitScale * dpiScale) * 100;
            
            if (!double.IsNaN(originalPercentage) && !double.IsInfinity(originalPercentage))
            {
                ZoomPercentage = Math.Max(0, Math.Min(1000, (int)Math.Round(originalPercentage)));
            }
            else
            {
                ZoomPercentage = (int)Math.Round(_zoomScale * 100);
            }
        }
        else
        {
            ZoomPercentage = (int)Math.Round(_zoomScale * 100);
        }
        
        ZoomChanged?.Invoke(this, ZoomPercentage);
        UpdateCursor();
    }
    
    private void UpdateCursor()
    {
        if (CanPan() && !_isDragging)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        }
        else if (!_isDragging)
        {
            ProtectedCursor = null;
        }
    }
    
    private bool CanPan()
    {
        if (_image?.Source == null || _container == null) return false;
        
        var (imageWidth, imageHeight) = GetRotatedImageSize();
        return imageWidth > _container.ActualWidth || imageHeight > _container.ActualHeight;
    }
    
    private bool CanPanHorizontal()
    {
        if (_image == null || _container == null) return false;
        var (imageWidth, _) = GetRotatedImageSize();
        return imageWidth > _container.ActualWidth;
    }
    
    private bool CanPanVertical()
    {
        if (_image == null || _container == null) return false;
        var (_, imageHeight) = GetRotatedImageSize();
        return imageHeight > _container.ActualHeight;
    }
    
    private (double width, double height) GetRotatedImageSize()
    {
        if (_image == null) return (0, 0);
        
        var actualWidth = _image.ActualWidth * _zoomScale;
        var actualHeight = _image.ActualHeight * _zoomScale;
        
        if (_rotation == 90 || _rotation == 270)
        {
            return (actualHeight, actualWidth);
        }
        return (actualWidth, actualHeight);
    }
    
    private void NormalizeRotation()
    {
        _rotation = _rotation % 360;
        if (_rotation < 0) _rotation += 360;
    }
    
    private double CalculateFitToScreenScale()
    {
        if (OriginalWidth <= 0 || OriginalHeight <= 0) return 1.0;
        if (_container == null) return 1.0;
        
        var containerWidth = _container.ActualWidth;
        var containerHeight = _container.ActualHeight;
        
        if (containerWidth <= 0 || containerHeight <= 0) return 1.0;
        
        var scaleX = containerWidth / OriginalWidth;
        var scaleY = containerHeight / OriginalHeight;
        
        return Math.Min(scaleX, scaleY);
    }
    
    private double CalculateOriginalScale()
    {
        var fitScale = CalculateFitToScreenScale();
        if (fitScale <= 0) return 1.0;
        
        var dpiScale = XamlRoot?.RasterizationScale ?? 1.0;
        return (1.0 / fitScale) / dpiScale;
    }
    
    private (double minZoom, double maxZoom) GetZoomLimits()
    {
        if (OriginalWidth <= 0 || OriginalHeight <= 0)
        {
            return (MinZoom, MaxZoom);
        }
        
        var fitScale = CalculateFitToScreenScale();
        var dpiScale = XamlRoot?.RasterizationScale ?? 1.0;
        
        if (fitScale <= 0)
        {
            return (MinZoom, MaxZoom);
        }
        
        var minZoomForFit = MinZoom / (fitScale * dpiScale);
        var maxZoomForFit = MaxZoom / (fitScale * dpiScale);
        
        return (minZoomForFit, maxZoomForFit);
    }
    
    #endregion
}
