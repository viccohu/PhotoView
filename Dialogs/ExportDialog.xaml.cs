using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ImageMagick;
using PhotoView.Contracts.Services;
using PhotoView.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace PhotoView.Dialogs;

public sealed partial class ExportDialog : ContentDialog
{
    private const int DefaultResizeLongSide = 1920;
    private const int DefaultResizePercent = 50;
    private const int DefaultJpegQuality = 100;

    private readonly ISettingsService _settingsService;
    private readonly List<ImageFileInfo> _images;
    private readonly DispatcherTimer _previewDebounceTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private CancellationTokenSource? _previewCancellationTokenSource;
    private bool _isExporting = false;
    private bool _isExportComplete = false;
    private string? _exportedFolderPath;

    public ExportDialog(ISettingsService settingsService, List<ImageFileInfo> images)
    {
        InitializeComponent();
        ApplyCurrentAppTheme();
        _settingsService = settingsService;
        _images = images.Where(img => !img.IsPendingDelete).ToList();
        
        LoadSettings();
        Opened += ExportDialog_Opened;
        Closing += ExportDialog_Closing;
        PrimaryButtonClick += ExportDialog_PrimaryButtonClick;
        
        ImageToggleButton.Checked += ImageToggleButton_Checked;
        ImageToggleButton.Unchecked += ImageToggleButton_Unchecked;
        RawToggleButton.Checked += RawToggleButton_Checked;
        RawToggleButton.Unchecked += RawToggleButton_Unchecked;
        SelectFolderButton.Click += SelectFolderButton_Click;
        ExportPathTextBox.TextChanged += ExportPathTextBox_TextChanged;
        ImageRatingComboBox.SelectionChanged += ExportOptionSelectionChanged;
        RawRatingComboBox.SelectionChanged += ExportOptionSelectionChanged;

        ResizeEnabledSwitch.Toggled += ResizeOptionChanged;
        ResizeModeComboBox.SelectionChanged += ResizeOptionSelectionChanged;
        ResizeLongSideTextBox.TextChanged += ResizeOptionTextChanged;
        ResizePercentTextBox.TextChanged += ResizeOptionTextChanged;
        ResizeFormatComboBox.SelectionChanged += ResizeOptionSelectionChanged;
        ResizeFilterComboBox.SelectionChanged += ResizeOptionSelectionChanged;
        ResizeQualitySlider.ValueChanged += ResizeQualitySlider_ValueChanged;

        _previewDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _previewDebounceTimer.Tick += PreviewDebounceTimer_Tick;

        ResetResizeOptions();
        UpdateResizeControlStates();
    }

    private void LoadSettings()
    {
        ImageToggleButton.IsChecked = _settingsService.ExportImageEnabled;
        RawToggleButton.IsChecked = _settingsService.ExportRawEnabled;
        ExportPathTextBox.Text = _settingsService.ExportLastFolderPath;
        ImageFolderTextBox.Text = _settingsService.ExportImageFolderName;
        RawFolderTextBox.Text = _settingsService.ExportRawFolderName;
        
        SetComboBoxSelection(ImageRatingComboBox, _settingsService.ExportImageMinRating);
        SetComboBoxSelection(RawRatingComboBox, _settingsService.ExportRawMinRating);
        
        UpdateControlStates();
    }

    private void SetComboBoxSelection(ComboBox comboBox, int rating)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem && comboBoxItem.Tag is string tag && int.TryParse(tag, out var tagRating) && tagRating == rating)
            {
                comboBox.SelectedItem = comboBoxItem;
                break;
            }
        }
    }

    private void UpdateControlStates()
    {
        ImageRatingComboBox.IsEnabled = ImageToggleButton.IsChecked == true;
        ImageFolderTextBox.IsEnabled = ImageToggleButton.IsChecked == true;
        RawRatingComboBox.IsEnabled = RawToggleButton.IsChecked == true;
        RawFolderTextBox.IsEnabled = RawToggleButton.IsChecked == true;
        
        var canExport = (ImageToggleButton.IsChecked == true || RawToggleButton.IsChecked == true) && 
                        !string.IsNullOrWhiteSpace(ExportPathTextBox.Text) &&
                        TryGetResizeOptions(out _, out _);
        IsPrimaryButtonEnabled = canExport && !_isExporting;
    }

    private void SaveSettings()
    {
        _settingsService.ExportImageEnabled = ImageToggleButton.IsChecked == true;
        _settingsService.ExportRawEnabled = RawToggleButton.IsChecked == true;
        _settingsService.ExportLastFolderPath = ExportPathTextBox.Text;
        _settingsService.ExportImageFolderName = ImageFolderTextBox.Text;
        _settingsService.ExportRawFolderName = RawFolderTextBox.Text;
        
        if (ImageRatingComboBox.SelectedItem is ComboBoxItem imageItem && imageItem.Tag is string imageTag && int.TryParse(imageTag, out var imageRating))
        {
            _settingsService.ExportImageMinRating = imageRating;
        }
        
        if (RawRatingComboBox.SelectedItem is ComboBoxItem rawItem && rawItem.Tag is string rawTag && int.TryParse(rawTag, out var rawRating))
        {
            _settingsService.ExportRawMinRating = rawRating;
        }
    }

    private void ExportDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        ApplyCurrentAppTheme();
        ResetResizeOptions();
        _isExporting = false;
        _isExportComplete = false;
        _exportedFolderPath = null;
        PrimaryButtonText = "开始导出";
        ExportProgressBar.Visibility = Visibility.Collapsed;
        StatusTextBlock.Text = "准备导出";
        UpdateResizeControlStates();
        QueuePreviewUpdate();
    }

    private void ApplyCurrentAppTheme()
    {
        if (App.MainWindow.Content is FrameworkElement rootElement)
        {
            RequestedTheme = rootElement.ActualTheme;
        }
    }

    private void ExportDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        SaveSettings();
        _cancellationTokenSource?.Cancel();
        _previewCancellationTokenSource?.Cancel();
    }

    private async void ExportDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        
        if (_isExportComplete && !string.IsNullOrEmpty(_exportedFolderPath))
        {
            OpenFolder(_exportedFolderPath);
            return;
        }
        
        if (_isExporting)
            return;
        
        _isExporting = true;
        IsPrimaryButtonEnabled = false;
        ExportProgressBar.Visibility = Visibility.Visible;
        
        _cancellationTokenSource = new CancellationTokenSource();
        
        try
        {
            await StartExportAsync(_cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "导出已取消";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"导出失败: {ex.Message}";
        }
        finally
        {
            _isExporting = false;
            if (!_isExportComplete)
            {
                IsPrimaryButtonEnabled = true;
            }
        }
    }

    private void OpenFolder(string folderPath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = folderPath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"打开文件夹失败: {ex.Message}";
        }
    }

    private async Task StartExportAsync(CancellationToken cancellationToken)
    {
        var basePath = ExportPathTextBox.Text.Trim();
        if (!TryGetResizeOptions(out var resizeOptions, out var validationError))
        {
            StatusTextBlock.Text = validationError;
            return;
        }
        
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }

        var exportJobs = BuildExportJobs(basePath, resizeOptions);
        if (exportJobs.Count == 0)
        {
            StatusTextBlock.Text = "没有符合条件的文件";
            return;
        }

        ExportProgressBar.Maximum = exportJobs.Count;
        ExportProgressBar.Value = 0;

        for (int i = 0; i < exportJobs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var job = exportJobs[i];
            var targetDir = Path.GetDirectoryName(job.TargetPath);

            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            if (job.Kind == ExportJobKind.ResizeAndEncode && job.ResizeOptions != null)
            {
                await Task.Run(() => ResizeAndEncodeImage(job.File.Path, job.TargetPath, job.ResizeOptions, cancellationToken), cancellationToken);
            }
            else
            {
                File.Copy(job.File.Path, job.TargetPath, true);
            }

            ExportProgressBar.Value = i + 1;
            StatusTextBlock.Text = $"正在导出: {i + 1}/{exportJobs.Count}";
        }

        StatusTextBlock.Text = $"导出完成: {exportJobs.Count} 个文件";
        _isExportComplete = true;
        _exportedFolderPath = basePath;
        PrimaryButtonText = "打开目标文件夹";
        IsPrimaryButtonEnabled = true;
    }

    private List<ExportJob> BuildExportJobs(string basePath, ExportResizeOptions? resizeOptions)
    {
        var exportImage = ImageToggleButton.IsChecked == true;
        var exportRaw = RawToggleButton.IsChecked == true;
        var imageMinRating = GetSelectedRating(ImageRatingComboBox);
        var rawMinRating = GetSelectedRating(RawRatingComboBox);
        var imageFolderName = ImageFolderTextBox.Text.Trim();
        var rawFolderName = RawFolderTextBox.Text.Trim();
        var usedTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exportJobs = new List<ExportJob>();

        foreach (var image in _images)
        {
            AddExportJobIfNeeded(image, exportImage, exportRaw, imageMinRating, rawMinRating, basePath, imageFolderName, rawFolderName, resizeOptions, usedTargetPaths, exportJobs);

            if (image.Group != null)
            {
                foreach (var alternateImage in image.AlternateFormats)
                {
                    AddExportJobIfNeeded(alternateImage, exportImage, exportRaw, imageMinRating, rawMinRating, basePath, imageFolderName, rawFolderName, resizeOptions, usedTargetPaths, exportJobs);
                }
            }
        }

        return exportJobs;
    }

    private static void AddExportJobIfNeeded(
        ImageFileInfo image,
        bool exportImage,
        bool exportRaw,
        int imageMinRating,
        int rawMinRating,
        string basePath,
        string imageFolderName,
        string rawFolderName,
        ExportResizeOptions? resizeOptions,
        HashSet<string> usedTargetPaths,
        List<ExportJob> exportJobs)
    {
        var ext = Path.GetExtension(image.ImageFile.Path).ToLowerInvariant();
        var rating = ImageFileInfo.RatingToStars(image.Rating);

        if (exportImage && IsImageExtension(ext) && rating >= imageMinRating)
        {
            var targetPath = GetTargetPath(basePath, imageFolderName, image.ImageFile.Path, resizeOptions?.Extension);
            targetPath = GetAvailableTargetPath(targetPath, usedTargetPaths);
            var kind = resizeOptions == null ? ExportJobKind.Copy : ExportJobKind.ResizeAndEncode;
            exportJobs.Add(new ExportJob(image.ImageFile, targetPath, kind, resizeOptions));
        }

        if (exportRaw && !IsImageExtension(ext) && rating >= rawMinRating)
        {
            var targetPath = GetTargetPath(basePath, rawFolderName, image.ImageFile.Path);
            targetPath = GetAvailableTargetPath(targetPath, usedTargetPaths);
            exportJobs.Add(new ExportJob(image.ImageFile, targetPath, ExportJobKind.Copy, null));
        }
    }

    private static void ResizeAndEncodeImage(
        string sourcePath,
        string targetPath,
        ExportResizeOptions resizeOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var image = new MagickImage(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();

        image.AutoOrient();
        var (targetWidth, targetHeight) = CalculateTargetSize(image.Width, image.Height, resizeOptions);

        image.FilterType = resizeOptions.FilterType;
        if (targetWidth != image.Width || targetHeight != image.Height)
        {
            image.Resize(targetWidth, targetHeight);
        }

        image.Format = resizeOptions.MagickFormat;
        if (resizeOptions.MagickFormat == MagickFormat.Jpeg)
        {
            image.Quality = (uint)resizeOptions.JpegQuality;
        }

        cancellationToken.ThrowIfCancellationRequested();
        image.Write(targetPath);
    }

    private static (uint Width, uint Height) CalculateTargetSize(uint originalWidth, uint originalHeight, ExportResizeOptions resizeOptions)
    {
        if (originalWidth == 0 || originalHeight == 0)
            return (1, 1);

        var scale = resizeOptions.Mode == ExportResizeMode.LongSide
            ? resizeOptions.LongSidePixels / (double)Math.Max(originalWidth, originalHeight)
            : resizeOptions.Percent / 100d;

        scale = Math.Min(scale, 1d);
        var targetWidth = Math.Max(1u, (uint)Math.Round(originalWidth * scale));
        var targetHeight = Math.Max(1u, (uint)Math.Round(originalHeight * scale));
        return (targetWidth, targetHeight);
    }

    private static string GetAvailableTargetPath(string targetPath, HashSet<string> usedTargetPaths)
    {
        var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);
        var candidate = targetPath;
        var index = 2;

        while (usedTargetPaths.Contains(candidate) || File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{nameWithoutExtension} ({index}){extension}");
            index++;
        }

        usedTargetPaths.Add(candidate);
        return candidate;
    }

    private int GetSelectedRating(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out var rating))
        {
            return rating;
        }
        return 1;
    }

    private static bool IsImageExtension(string ext)
    {
        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tiff" or ".tif" or ".webp";
    }

    private static string GetTargetPath(string basePath, string folderName, string sourcePath, string? targetExtension = null)
    {
        var fileName = string.IsNullOrWhiteSpace(targetExtension)
            ? Path.GetFileName(sourcePath)
            : $"{Path.GetFileNameWithoutExtension(sourcePath)}{targetExtension}";
        
        if (string.IsNullOrWhiteSpace(folderName) || folderName == "\\" || folderName == "/")
        {
            return Path.Combine(basePath, fileName);
        }
        else
        {
            return Path.Combine(basePath, folderName, fileName);
        }
    }

    private void ResetResizeOptions()
    {
        ResizeEnabledSwitch.IsOn = false;
        ResizeModeComboBox.SelectedIndex = 0;
        ResizeLongSideTextBox.Text = DefaultResizeLongSide.ToString();
        ResizePercentTextBox.Text = DefaultResizePercent.ToString();
        ResizeFormatComboBox.SelectedIndex = 0;
        ResizeFilterComboBox.SelectedIndex = 0;
        ResizeQualitySlider.Value = DefaultJpegQuality;
    }

    private void UpdateResizeControlStates()
    {
        var enabled = ResizeEnabledSwitch.IsOn;
        ResizeOptionsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        SetResizeInputControlsEnabled(enabled);

        var usePercent = IsPercentResizeModeSelected();
        ResizeLongSideTextBox.Visibility = usePercent ? Visibility.Collapsed : Visibility.Visible;
        ResizePercentTextBox.Visibility = usePercent ? Visibility.Visible : Visibility.Collapsed;
        ResizeInputHeaderTextBlock.Text = usePercent ? "长边尺寸（百分比）" : "长边尺寸（像素）";

        var outputFormat = GetSelectedOutputFormat();
        var qualityEnabled = enabled && outputFormat == ExportOutputFormat.Jpg;
        ResizeQualitySlider.IsEnabled = qualityEnabled;
        ResizeQualityTextBlock.Opacity = qualityEnabled ? 1d : 0.55d;
        ResizeQualityTextBlock.Text = outputFormat == ExportOutputFormat.Jpg
            ? $"质量：{(int)Math.Round(ResizeQualitySlider.Value)}%"
            : "质量：仅 JPG 生效";

        if (!enabled)
        {
            ResizeHintTextBlock.Text = "开启后仅处理照片导出，RAW 保持原文件复制。";
        }
        else if (!TryGetResizeOptions(out _, out var validationError))
        {
            ResizeHintTextBlock.Text = validationError;
        }
        else
        {
            ResizeHintTextBlock.Text = "保持比例，不放大小图。";
        }

        UpdateControlStates();
    }

    private void SetResizeInputControlsEnabled(bool enabled)
    {
        ResizeModeComboBox.IsEnabled = enabled;
        ResizeLongSideTextBox.IsEnabled = enabled;
        ResizePercentTextBox.IsEnabled = enabled;
        ResizeFormatComboBox.IsEnabled = enabled;
        ResizeFilterComboBox.IsEnabled = enabled;
    }

    private bool TryGetResizeOptions(out ExportResizeOptions? options, out string validationError)
    {
        options = null;
        validationError = string.Empty;

        if (!ResizeEnabledSwitch.IsOn)
            return true;

        var mode = IsPercentResizeModeSelected()
            ? ExportResizeMode.Percent
            : ExportResizeMode.LongSide;

        var longSide = DefaultResizeLongSide;
        var percent = DefaultResizePercent;

        if (mode == ExportResizeMode.LongSide)
        {
            if (!int.TryParse(ResizeLongSideTextBox.Text.Trim(), out longSide) || longSide < 1)
            {
                validationError = "请输入有效的最长边像素。";
                return false;
            }
        }
        else if (!int.TryParse(ResizePercentTextBox.Text.Trim(), out percent) || percent < 1)
        {
            validationError = "请输入有效的百分比。";
            return false;
        }

        var outputFormat = GetSelectedOutputFormat();
        options = new ExportResizeOptions(
            mode,
            longSide,
            percent,
            outputFormat,
            GetSelectedMagickFormat(outputFormat),
            GetOutputExtension(outputFormat),
            (int)Math.Round(ResizeQualitySlider.Value),
            GetSelectedFilterType());
        return true;
    }

    private bool IsPercentResizeModeSelected()
    {
        var tag = (ResizeModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        return string.Equals(tag, "Percent", StringComparison.Ordinal);
    }

    private ExportOutputFormat GetSelectedOutputFormat()
    {
        var tag = (ResizeFormatComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        return tag switch
        {
            "PNG" => ExportOutputFormat.Png,
            "TIF" => ExportOutputFormat.Tif,
            _ => ExportOutputFormat.Jpg
        };
    }

    private FilterType GetSelectedFilterType()
    {
        var tag = (ResizeFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        return tag switch
        {
            "Balanced" => FilterType.Mitchell,
            "Fast" => FilterType.Triangle,
            "Sharp" => FilterType.Catrom,
            _ => FilterType.Lanczos
        };
    }

    private static MagickFormat GetSelectedMagickFormat(ExportOutputFormat outputFormat)
    {
        return outputFormat switch
        {
            ExportOutputFormat.Png => MagickFormat.Png,
            ExportOutputFormat.Tif => MagickFormat.Tiff,
            _ => MagickFormat.Jpeg
        };
    }

    private static string GetOutputExtension(ExportOutputFormat outputFormat)
    {
        return outputFormat switch
        {
            ExportOutputFormat.Png => ".png",
            ExportOutputFormat.Tif => ".tif",
            _ => ".jpg"
        };
    }

    private void QueuePreviewUpdate()
    {
        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    private async void PreviewDebounceTimer_Tick(object? sender, object e)
    {
        _previewDebounceTimer.Stop();
        await UpdateResizePreviewAsync();
    }

    private async Task UpdateResizePreviewAsync()
    {
        _previewCancellationTokenSource?.Cancel();
        _previewCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _previewCancellationTokenSource.Token;

        if (!ResizeEnabledSwitch.IsOn)
        {
            ResizePreviewCurrentTextBlock.Text = "无可预览照片";
            ResizePreviewNewTextBlock.Text = "启用后显示";
            return;
        }

        var sample = GetPreviewSampleImage();
        if (sample == null)
        {
            ResizePreviewCurrentTextBlock.Text = "无可预览照片";
            ResizePreviewNewTextBlock.Text = "没有符合照片筛选条件的文件";
            return;
        }

        if (!TryGetResizeOptions(out var resizeOptions, out var validationError) || resizeOptions == null)
        {
            ResizePreviewCurrentTextBlock.Text = $"{sample.Width} x {sample.Height} 像素";
            ResizePreviewNewTextBlock.Text = validationError;
            return;
        }

        ResizePreviewCurrentTextBlock.Text = $"{sample.Width} x {sample.Height} 像素  {FormatByteSize(GetFileSize(sample.ImageFile.Path))}  {Path.GetExtension(sample.ImageFile.Path).TrimStart('.').ToUpperInvariant()}";

        var (targetWidth, targetHeight) = CalculateTargetSize((uint)Math.Max(1, sample.Width), (uint)Math.Max(1, sample.Height), resizeOptions);
        ResizePreviewNewTextBlock.Text = $"{targetWidth} x {targetHeight} 像素  估算中...  {resizeOptions.DisplayFormat}";

        try
        {
            var estimatedBytes = await Task.Run(() => EstimateEncodedSize(sample.ImageFile.Path, resizeOptions, cancellationToken), cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                ResizePreviewNewTextBlock.Text = $"{targetWidth} x {targetHeight} 像素  {FormatByteSize(estimatedBytes)}  {resizeOptions.DisplayFormat}";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ResizePreviewNewTextBlock.Text = $"{targetWidth} x {targetHeight} 像素  无法估算  {resizeOptions.DisplayFormat}";
            }
        }
    }

    private ImageFileInfo? GetPreviewSampleImage()
    {
        if (ImageToggleButton.IsChecked != true)
            return null;

        var minRating = GetSelectedRating(ImageRatingComboBox);
        return EnumerateExportImages()
            .FirstOrDefault(image =>
            {
                var ext = Path.GetExtension(image.ImageFile.Path).ToLowerInvariant();
                return IsImageExtension(ext) && ImageFileInfo.RatingToStars(image.Rating) >= minRating;
            });
    }

    private IEnumerable<ImageFileInfo> EnumerateExportImages()
    {
        foreach (var image in _images)
        {
            yield return image;

            if (image.Group == null)
                continue;

            foreach (var alternateImage in image.AlternateFormats)
            {
                yield return alternateImage;
            }
        }
    }

    private static long EstimateEncodedSize(string sourcePath, ExportResizeOptions resizeOptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var image = new MagickImage(sourcePath);
        image.AutoOrient();
        var (targetWidth, targetHeight) = CalculateTargetSize(image.Width, image.Height, resizeOptions);
        image.FilterType = resizeOptions.FilterType;
        if (targetWidth != image.Width || targetHeight != image.Height)
        {
            image.Resize(targetWidth, targetHeight);
        }

        image.Format = resizeOptions.MagickFormat;
        if (resizeOptions.MagickFormat == MagickFormat.Jpeg)
        {
            image.Quality = (uint)resizeOptions.JpegQuality;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        image.Write(stream);
        return stream.Length;
    }

    private static long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatByteSize(long bytes)
    {
        if (bytes <= 0)
            return "-";

        string[] units = { "B", "KB", "MB", "GB" };
        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= 1024d && unitIndex < units.Length - 1)
        {
            size /= 1024d;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{bytes} {units[unitIndex]}" : $"{size:0.#} {units[unitIndex]}";
    }

    private void ImageToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        UpdateControlStates();
        QueuePreviewUpdate();
    }

    private void ImageToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        UpdateControlStates();
        QueuePreviewUpdate();
    }

    private void RawToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        UpdateControlStates();
    }

    private void RawToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        UpdateControlStates();
    }

    private void ExportPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateControlStates();
    }

    private void ExportOptionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateControlStates();
        QueuePreviewUpdate();
    }

    private void ResizeOptionChanged(object sender, RoutedEventArgs e)
    {
        UpdateResizeControlStates();
        QueuePreviewUpdate();
    }

    private void ResizeOptionTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateResizeControlStates();
        QueuePreviewUpdate();
    }

    private void ResizeOptionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateResizeControlStates();
        QueuePreviewUpdate();
    }

    private void ResizeQualitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var snappedValue = Math.Clamp((int)Math.Round(e.NewValue / 10d) * 10, 10, 100);
        if (Math.Abs(ResizeQualitySlider.Value - snappedValue) > double.Epsilon)
        {
            ResizeQualitySlider.Value = snappedValue;
            return;
        }

        UpdateResizeControlStates();
        QueuePreviewUpdate();
    }

    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add("*");
        
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            ExportPathTextBox.Text = folder.Path;
            UpdateControlStates();
        }
    }

    private enum ExportResizeMode
    {
        LongSide,
        Percent
    }

    private enum ExportOutputFormat
    {
        Jpg,
        Png,
        Tif
    }

    private enum ExportJobKind
    {
        Copy,
        ResizeAndEncode
    }

    private sealed record ExportResizeOptions(
        ExportResizeMode Mode,
        int LongSidePixels,
        int Percent,
        ExportOutputFormat OutputFormat,
        MagickFormat MagickFormat,
        string Extension,
        int JpegQuality,
        FilterType FilterType)
    {
        public string DisplayFormat => OutputFormat switch
        {
            ExportOutputFormat.Png => "PNG",
            ExportOutputFormat.Tif => "TIF",
            _ => "JPG"
        };
    }

    private sealed record ExportJob(
        StorageFile File,
        string TargetPath,
        ExportJobKind Kind,
        ExportResizeOptions? ResizeOptions);
}
