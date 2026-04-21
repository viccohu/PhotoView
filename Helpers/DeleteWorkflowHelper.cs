using PhotoView.Contracts.Services;
using PhotoView.Dialogs;
using PhotoView.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace PhotoView.Helpers;

internal static class DeleteWorkflowHelper
{
    private const int ProgressUpdateInterval = 16;
    private static readonly TimeSpan ProgressUpdateMaxGap = TimeSpan.FromMilliseconds(100);

    internal sealed record DeleteRunResult(List<ImageFileInfo> DeletedImages, int FailedCount);

    public static List<StorageFile> GetFilesToDelete(List<ImageFileInfo> pendingImages, List<string> selectedExtensions)
    {
        var selectedExtensionSet = selectedExtensions
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var files = new List<StorageFile>();

        foreach (var image in pendingImages)
        {
            if (image.ImageFile != null)
            {
                var extension = Path.GetExtension(image.ImageFile.Path).ToLowerInvariant();
                if (selectedExtensionSet.Contains(extension))
                {
                    files.Add(image.ImageFile);
                }
            }

            if (image.AlternateFormats == null)
            {
                continue;
            }

            foreach (var alternateImage in image.AlternateFormats)
            {
                if (alternateImage.ImageFile == null)
                {
                    continue;
                }

                var alternateExtension = Path.GetExtension(alternateImage.ImageFile.Path).ToLowerInvariant();
                if (selectedExtensionSet.Contains(alternateExtension))
                {
                    files.Add(alternateImage.ImageFile);
                }
            }
        }

        return files
            .DistinctBy(file => file.Path)
            .ToList();
    }

    public static Dictionary<string, ImageFileInfo> BuildPrimaryImagePathMap(IEnumerable<ImageFileInfo> pendingImages)
    {
        var pathMap = new Dictionary<string, ImageFileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in pendingImages)
        {
            var path = image.ImageFile?.Path;
            if (!string.IsNullOrWhiteSpace(path))
            {
                pathMap[path] = image;
            }
        }

        return pathMap;
    }

    public static async Task<DeleteRunResult> DeleteFilesAsync(
        IReadOnlyList<StorageFile> filesToDelete,
        IReadOnlyDictionary<string, ImageFileInfo> imagePathMap,
        DeleteConfirmDialog dialog,
        IThumbnailService thumbnailService,
        Action<string> logFailure)
    {
        var deletedImages = new List<ImageFileInfo>();
        var failedCount = 0;
        var deleteOptionCache = new Dictionary<string, StorageDeleteOption>(StringComparer.OrdinalIgnoreCase);
        var progressStopwatch = Stopwatch.StartNew();
        var lastProgressUpdateCount = 0;

        dialog.StartProgress();

        for (var index = 0; index < filesToDelete.Count; index++)
        {
            var file = filesToDelete[index];

            try
            {
                await DeleteFileToRecycleBinAsync(file, deleteOptionCache);
                thumbnailService.Invalidate(file);

                if (imagePathMap.TryGetValue(file.Path, out var image) && !deletedImages.Contains(image))
                {
                    deletedImages.Add(image);
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                logFailure($"{file.Path}: {ex.Message}");
            }

            var current = index + 1;
            if (ShouldUpdateProgress(current, filesToDelete.Count, lastProgressUpdateCount, progressStopwatch.Elapsed))
            {
                dialog.SetProgress(current, filesToDelete.Count);
                lastProgressUpdateCount = current;
                progressStopwatch.Restart();
            }
        }

        dialog.SetComplete();
        return new DeleteRunResult(deletedImages, failedCount);
    }

    private static bool ShouldUpdateProgress(int current, int total, int lastProgressUpdateCount, TimeSpan elapsedSinceLastUpdate)
    {
        if (current <= 1 || current >= total)
        {
            return true;
        }

        if (current - lastProgressUpdateCount >= ProgressUpdateInterval)
        {
            return true;
        }

        return elapsedSinceLastUpdate >= ProgressUpdateMaxGap;
    }

    private static async Task DeleteFileToRecycleBinAsync(StorageFile file, Dictionary<string, StorageDeleteOption> deleteOptionCache)
    {
        var deleteOption = GetDeleteOption(file.Path, deleteOptionCache);
        await file.DeleteAsync(deleteOption);
    }

    private static StorageDeleteOption GetDeleteOption(string filePath, Dictionary<string, StorageDeleteOption> deleteOptionCache)
    {
        var driveRoot = Path.GetPathRoot(filePath) ?? string.Empty;
        if (deleteOptionCache.TryGetValue(driveRoot, out var cachedDeleteOption))
        {
            return cachedDeleteOption;
        }

        var settingsService = App.GetService<ISettingsService>();
        var deleteOption = IsRemovableDrive(driveRoot) || !settingsService.DeleteToRecycleBin
            ? StorageDeleteOption.PermanentDelete
            : StorageDeleteOption.Default;

        deleteOptionCache[driveRoot] = deleteOption;
        return deleteOption;
    }

    private static bool IsRemovableDrive(string driveRoot)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            return false;
        }

        try
        {
            var driveInfo = new DriveInfo(driveRoot);
            return driveInfo.DriveType == DriveType.Removable;
        }
        catch
        {
            return false;
        }
    }
}
