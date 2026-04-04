using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PhotoView.Models;
using Windows.Storage;

namespace PhotoView.Services;

public class RatingCacheService
{
    private const string CacheFileName = "rating_cache.json";
    private readonly string _cacheFilePath;
    private Dictionary<string, RatingCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _isInitialized;

    public RatingCacheService()
    {
        _cacheFilePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, CacheFileName);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            if (File.Exists(_cacheFilePath))
            {
                var json = await File.ReadAllTextAsync(_cacheFilePath);
                var data = JsonConvert.DeserializeObject<RatingCacheData>(json);
                if (data?.Entries != null)
                {
                    foreach (var entry in data.Entries)
                    {
                        _cache[entry.FilePath] = entry;
                    }
                }
            }
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RatingCacheService] 初始化失败: {ex.Message}");
            _cache = new Dictionary<string, RatingCacheEntry>(StringComparer.OrdinalIgnoreCase);
            _isInitialized = true;
        }
    }

    public uint? GetRating(string filePath)
    {
        if (_cache.TryGetValue(filePath, out var entry))
        {
            return entry.Rating;
        }
        return null;
    }

    public async Task SetRatingAsync(string filePath, uint rating)
    {
        var entry = new RatingCacheEntry
        {
            FilePath = filePath,
            Rating = rating,
            LastModified = DateTime.UtcNow
        };

        _cache[filePath] = entry;
        await SaveCacheAsync();
    }

    private async Task SaveCacheAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var data = new RatingCacheData
            {
                Entries = new List<RatingCacheEntry>(_cache.Values)
            };

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            await File.WriteAllTextAsync(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RatingCacheService] 保存缓存失败: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private class RatingCacheData
    {
        public List<RatingCacheEntry>? Entries { get; set; }
    }
}
