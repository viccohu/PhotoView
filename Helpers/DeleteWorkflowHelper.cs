using PhotoView.Contracts.Services;
using PhotoView.Dialogs;
using PhotoView.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Storage;

namespace PhotoView.Helpers;

internal static class DeleteWorkflowHelper
{
    private const int ProgressUpdateInterval = 16;
    private const int ShellDeleteChunkSize = 24;
    private static readonly TimeSpan ProgressUpdateMaxGap = TimeSpan.FromMilliseconds(100);
    private const int FoDelete = 0x0003;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoErrorUi = 0x0400;

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
        dialog.SetProgress(0, Math.Max(1, filesToDelete.Count));

        void ReportProgress(int current)
        {
            if (ShouldUpdateProgress(current, filesToDelete.Count, lastProgressUpdateCount, progressStopwatch.Elapsed))
            {
                dialog.SetProgress(current, filesToDelete.Count);
                lastProgressUpdateCount = current;
                progressStopwatch.Restart();
            }
        }

        var recycleBinCandidates = new List<StorageFile>();
        var fallbackFiles = new List<StorageFile>();

        foreach (var file in filesToDelete)
        {
            var deleteOption = GetDeleteOption(file.Path, deleteOptionCache);
            if (deleteOption == StorageDeleteOption.Default)
            {
                recycleBinCandidates.Add(file);
            }
            else
            {
                fallbackFiles.Add(file);
            }
        }

        if (recycleBinCandidates.Count > 0)
        {
            var recycledPaths = await DeleteWithShellRecycleBinAsync(recycleBinCandidates, logFailure, ReportProgress);
            foreach (var recycledPath in recycledPaths)
            {
                var recycledFile = recycleBinCandidates.FirstOrDefault(file =>
                    string.Equals(file.Path, recycledPath, StringComparison.OrdinalIgnoreCase));
                if (recycledFile != null)
                {
                    thumbnailService.Invalidate(recycledFile);
                }

                if (imagePathMap.TryGetValue(recycledPath, out var image) && !deletedImages.Contains(image))
                {
                    deletedImages.Add(image);
                }
            }

            failedCount += recycleBinCandidates.Count - recycledPaths.Count;
        }

        if (fallbackFiles.Count > 0)
        {
            for (var index = 0; index < fallbackFiles.Count; index++)
            {
                var file = fallbackFiles[index];

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

                var current = recycleBinCandidates.Count + index + 1;
                ReportProgress(current);
            }
        }
        else
        {
            dialog.SetProgress(filesToDelete.Count, Math.Max(1, filesToDelete.Count));
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

    private static async Task<HashSet<string>> DeleteWithShellRecycleBinAsync(
        IReadOnlyList<StorageFile> filesToDelete,
        Action<string> logFailure,
        Action<int> reportProgress)
    {
        var deletedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedCount = 0;

        foreach (var driveGroup in filesToDelete
                     .Where(file => !string.IsNullOrWhiteSpace(file.Path))
                     .GroupBy(file => Path.GetPathRoot(file.Path) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var groupFiles = driveGroup
                .DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (groupFiles.Count == 0)
            {
                continue;
            }

            foreach (var chunk in groupFiles.Chunk(ShellDeleteChunkSize))
            {
                var chunkFiles = chunk.ToList();

                try
                {
                    var shellResult = await Task.Run(() => ShellRecycleBinDelete(driveGroup.Key, chunkFiles));
                    foreach (var deletedPath in shellResult.DeletedPaths)
                    {
                        deletedPaths.Add(deletedPath);
                    }

                    foreach (var failedPath in shellResult.FailedPaths)
                    {
                        logFailure($"{failedPath}: shell recycle delete failed");
                    }
                }
                catch (Exception ex)
                {
                    logFailure($"shell recycle delete failed for drive {driveGroup.Key}: {ex.Message}");

                    foreach (var file in chunkFiles)
                    {
                        try
                        {
                            await file.DeleteAsync(StorageDeleteOption.Default);
                            deletedPaths.Add(file.Path);
                        }
                        catch (Exception fallbackEx)
                        {
                            logFailure($"{file.Path}: {fallbackEx.Message}");
                        }

                        processedCount++;
                        reportProgress(processedCount);
                    }

                    continue;
                }

                processedCount += chunkFiles.Count;
                reportProgress(processedCount);
            }
        }

        return deletedPaths;
    }

    private static ShellDeleteResult ShellRecycleBinDelete(string driveRoot, IReadOnlyList<StorageFile> filesToDelete)
    {
        var sourcePaths = string.Join('\0', filesToDelete.Select(file => file.Path)) + "\0\0";
        var operation = new ShFileOpStruct
        {
            wFunc = FoDelete,
            pFrom = sourcePaths,
            fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi
        };

        var result = SHFileOperation(ref operation);
        var deletedPaths = new List<string>();
        var failedPaths = new List<string>();

        foreach (var file in filesToDelete)
        {
            if (!File.Exists(file.Path))
            {
                deletedPaths.Add(file.Path);
            }
            else
            {
                failedPaths.Add(file.Path);
            }
        }

        if (result != 0 && failedPaths.Count == 0)
        {
            failedPaths.AddRange(filesToDelete.Select(file => file.Path));
            deletedPaths.Clear();
        }

        if (operation.fAnyOperationsAborted && failedPaths.Count == 0)
        {
            failedPaths.AddRange(filesToDelete.Where(file => File.Exists(file.Path)).Select(file => file.Path));
            deletedPaths = filesToDelete.Where(file => !File.Exists(file.Path)).Select(file => file.Path).ToList();
        }

        return new ShellDeleteResult(driveRoot, deletedPaths, failedPaths, operation.fAnyOperationsAborted);
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

    private sealed record ShellDeleteResult(
        string DriveRoot,
        List<string> DeletedPaths,
        List<string> FailedPaths,
        bool UserCanceled);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);
}
