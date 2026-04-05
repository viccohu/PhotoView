using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly ISettingsService _settingsService;
    private readonly List<ImageFileInfo> _images;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isExporting = false;
    private bool _isExportComplete = false;
    private string? _exportedFolderPath;

    public ExportDialog(ISettingsService settingsService, List<ImageFileInfo> images)
    {
        InitializeComponent();
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
                        !string.IsNullOrWhiteSpace(ExportPathTextBox.Text);
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
        _isExporting = false;
        _isExportComplete = false;
        _exportedFolderPath = null;
        PrimaryButtonText = "开始导出";
        ExportProgressBar.Visibility = Visibility.Collapsed;
        StatusTextBlock.Text = "准备导出";
    }

    private void ExportDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        SaveSettings();
        _cancellationTokenSource?.Cancel();
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
        var exportImage = ImageToggleButton.IsChecked == true;
        var exportRaw = RawToggleButton.IsChecked == true;
        var imageMinRating = GetSelectedRating(ImageRatingComboBox);
        var rawMinRating = GetSelectedRating(RawRatingComboBox);
        var imageFolderName = ImageFolderTextBox.Text.Trim();
        var rawFolderName = RawFolderTextBox.Text.Trim();
        var basePath = ExportPathTextBox.Text.Trim();
        
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }
        
        var filesToExport = new List<(StorageFile File, string TargetPath)>();
        
        foreach (var image in _images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var rating = ImageFileInfo.RatingToStars(image.Rating);
            
            if (exportImage)
            {
                var ext = Path.GetExtension(image.ImageFile.Path).ToLowerInvariant();
                if (IsImageExtension(ext) && rating >= imageMinRating)
                {
                    var targetPath = GetTargetPath(basePath, imageFolderName, image.ImageFile.Path);
                    filesToExport.Add((image.ImageFile, targetPath));
                }
            }
            
            if (exportRaw)
            {
                var ext = Path.GetExtension(image.ImageFile.Path).ToLowerInvariant();
                if (!IsImageExtension(ext) && rating >= rawMinRating)
                {
                    var targetPath = GetTargetPath(basePath, rawFolderName, image.ImageFile.Path);
                    filesToExport.Add((image.ImageFile, targetPath));
                }
            }
            
            if (image.Group != null)
            {
                foreach (var alternateImage in image.AlternateFormats)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var altExt = Path.GetExtension(alternateImage.ImageFile.Path).ToLowerInvariant();
                    var altRating = ImageFileInfo.RatingToStars(alternateImage.Rating);
                    
                    if (exportImage && IsImageExtension(altExt) && altRating >= imageMinRating)
                    {
                        var targetPath = GetTargetPath(basePath, imageFolderName, alternateImage.ImageFile.Path);
                        filesToExport.Add((alternateImage.ImageFile, targetPath));
                    }
                    
                    if (exportRaw && !IsImageExtension(altExt) && altRating >= rawMinRating)
                    {
                        var targetPath = GetTargetPath(basePath, rawFolderName, alternateImage.ImageFile.Path);
                        filesToExport.Add((alternateImage.ImageFile, targetPath));
                    }
                }
            }
        }
        
        if (filesToExport.Count == 0)
        {
            StatusTextBlock.Text = "没有符合条件的文件";
            return;
        }
        
        ExportProgressBar.Maximum = filesToExport.Count;
        ExportProgressBar.Value = 0;
        
        for (int i = 0; i < filesToExport.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var (file, targetPath) = filesToExport[i];
            var targetDir = Path.GetDirectoryName(targetPath);
            
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            
            File.Copy(file.Path, targetPath, true);
            
            ExportProgressBar.Value = i + 1;
            StatusTextBlock.Text = $"正在导出: {i + 1}/{filesToExport.Count}";
        }
        
        StatusTextBlock.Text = $"导出完成: {filesToExport.Count} 个文件";
        _isExportComplete = true;
        _exportedFolderPath = basePath;
        PrimaryButtonText = "打开目标文件夹";
        IsPrimaryButtonEnabled = true;
    }

    private int GetSelectedRating(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out var rating))
        {
            return rating;
        }
        return 1;
    }

    private bool IsImageExtension(string ext)
    {
        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tiff" or ".tif" or ".webp";
    }

    private string GetTargetPath(string basePath, string folderName, string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        
        if (string.IsNullOrWhiteSpace(folderName) || folderName == "\\" || folderName == "/")
        {
            return Path.Combine(basePath, fileName);
        }
        else
        {
            return Path.Combine(basePath, folderName, fileName);
        }
    }

    private void ImageToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        UpdateControlStates();
    }

    private void ImageToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        UpdateControlStates();
    }

    private void RawToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        UpdateControlStates();
    }

    private void RawToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        UpdateControlStates();
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
}
