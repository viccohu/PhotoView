using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoView.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace PhotoView.Services;

public class ImageSelectionService
{
    private readonly ObservableCollection<ImageFileInfo> _selectedImages = new();
    private ImageFileInfo? _lastSelectedImage;
    private bool _isSelectionActive;

    public ReadOnlyObservableCollection<ImageFileInfo> SelectedImages { get; }

    public bool IsSelectionActive => _isSelectionActive;

    public int SelectedCount => _selectedImages.Count;

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler? SelectionModeChanged;

    public ImageSelectionService()
    {
        SelectedImages = new ReadOnlyObservableCollection<ImageFileInfo>(_selectedImages);
    }

    public void HandleItemClick(ImageFileInfo image, bool isCtrlPressed, bool isShiftPressed)
    {
        if (isCtrlPressed)
        {
            ToggleSelection(image);
        }
        else if (isShiftPressed && _lastSelectedImage != null)
        {
            SelectRange(_lastSelectedImage, image);
        }
        else
        {
            SelectSingle(image);
        }
    }

    public void ToggleSelection(ImageFileInfo image)
    {
        if (_selectedImages.Contains(image))
        {
            _selectedImages.Remove(image);
            UpdateSelectionState();
        }
        else
        {
            _selectedImages.Add(image);
            _isSelectionActive = true;
        }

        _lastSelectedImage = image;
        RaiseSelectionChanged();
    }

    public void SelectSingle(ImageFileInfo image)
    {
        _selectedImages.Clear();
        _selectedImages.Add(image);
        _lastSelectedImage = image;
        _isSelectionActive = true;
        RaiseSelectionChanged();
    }

    public void SelectRange(ImageFileInfo fromImage, ImageFileInfo toImage, IList<ImageFileInfo>? sourceList = null)
    {
        if (sourceList == null)
        {
            SelectSingle(toImage);
            return;
        }

        var fromIndex = sourceList.IndexOf(fromImage);
        var toIndex = sourceList.IndexOf(toImage);

        if (fromIndex < 0 || toIndex < 0)
        {
            SelectSingle(toImage);
            return;
        }

        var startIndex = Math.Min(fromIndex, toIndex);
        var endIndex = Math.Max(fromIndex, toIndex);

        _selectedImages.Clear();

        for (var i = startIndex; i <= endIndex; i++)
        {
            _selectedImages.Add(sourceList[i]);
        }

        _lastSelectedImage = toImage;
        _isSelectionActive = true;
        RaiseSelectionChanged();
    }

    public void SelectAll(IList<ImageFileInfo> images)
    {
        _selectedImages.Clear();
        foreach (var image in images)
        {
            _selectedImages.Add(image);
        }

        _isSelectionActive = true;
        RaiseSelectionChanged();
    }

    public void ClearSelection()
    {
        _selectedImages.Clear();
        _lastSelectedImage = null;
        _isSelectionActive = false;
        RaiseSelectionChanged();
    }

    public bool IsSelected(ImageFileInfo image)
    {
        return _selectedImages.Contains(image);
    }

    private void UpdateSelectionState()
    {
        var wasActive = _isSelectionActive;
        _isSelectionActive = _selectedImages.Count > 0;

        if (wasActive != _isSelectionActive)
        {
            SelectionModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(
            Array.Empty<ImageFileInfo>(),
            _selectedImages.ToList()));
    }
}

public class SelectionChangedEventArgs : EventArgs
{
    public IList<ImageFileInfo> AddedItems { get; }
    public IList<ImageFileInfo> RemovedItems { get; }

    public SelectionChangedEventArgs(IList<ImageFileInfo> removedItems, IList<ImageFileInfo> addedItems)
    {
        RemovedItems = removedItems;
        AddedItems = addedItems;
    }
}
