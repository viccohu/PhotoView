using PhotoView.Helpers;
using PhotoView.Services;
using System;
using System.Threading.Tasks;

namespace PhotoView.Models;

public partial class ImageFileInfo
{
    private uint _rating;
    private bool _isRatingLoading = true;
    private bool _isRatingLoaded;
    private bool _isRatingLoadRequested;
    private int _ratingEditVersion;
    private RatingSource _ratingSource = RatingSource.Unknown;
    private DateTime? _dateTaken;

    public uint Rating
    {
        get => _rating;
        set
        {
            lock (_thumbnailLoadLock)
            {
                _ratingEditVersion++;
                SetRatingCore(value);
                IsRatingLoaded = true;
                IsRatingLoading = false;
                _isRatingLoadRequested = false;
            }
        }
    }

    public RatingSource RatingSource
    {
        get => _ratingSource;
        set => SetProperty(ref _ratingSource, value);
    }

    public double RatingValue
    {
        get => _rating == 0 ? -1 : RatingToStars(_rating);
        set
        {
            uint newRating;

            if (value < 0 || value == 0)
            {
                newRating = 0;
            }
            else
            {
                var stars = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                stars = Math.Clamp(stars, 1, 5);
                newRating = StarsToRating(stars);
            }

            if (newRating != _rating)
            {
                Rating = newRating;
            }
        }
    }

    public static int RatingToStars(uint rating)
    {
        if (rating == 0) return 0;
        if (rating >= 1 && rating <= 12) return 1;
        if (rating >= 13 && rating <= 37) return 2;
        if (rating >= 38 && rating <= 62) return 3;
        if (rating >= 63 && rating <= 87) return 4;
        if (rating >= 88 && rating <= 99) return 5;
        return 0;
    }

    public static uint StarsToRating(int stars)
    {
        return stars switch
        {
            0 => 0,
            1 => 1,
            2 => 25,
            3 => 50,
            4 => 75,
            5 => 99,
            _ => 0
        };
    }

    public bool IsRatingLoading
    {
        get => _isRatingLoading;
        set => SetProperty(ref _isRatingLoading, value);
    }

    public bool IsRatingLoaded
    {
        get => _isRatingLoaded;
        private set => SetProperty(ref _isRatingLoaded, value);
    }

    public void SetRatingFromProperties(uint rating, RatingSource source)
    {
        lock (_thumbnailLoadLock)
        {
            if (_isRatingLoaded)
            {
                return;
            }

            _isRatingLoadRequested = true;
            SetRatingCore(rating);
            RatingSource = source;
            IsRatingLoaded = true;
            IsRatingLoading = false;
        }
    }

    public DateTime? DateTaken
    {
        get => _dateTaken;
        private set => SetProperty(ref _dateTaken, value);
    }

    public void SetDateTakenFromProperties(DateTime? dateTaken)
    {
        DateTaken = ImageMetadataDateHelper.NormalizeDateTaken(dateTaken, ImageFileType);
    }

    public async Task EnsureRatingAsync(RatingService ratingService)
    {
        var editVersion = BeginRatingPreload();
        if (editVersion < 0)
        {
            return;
        }

        await LoadRatingAsync(ratingService, editVersion);
    }

    public async Task LoadRatingAsync(RatingService ratingService)
    {
        var editVersion = BeginRatingPreload();
        if (editVersion < 0)
        {
            return;
        }

        await LoadRatingAsync(ratingService, editVersion);
    }

    private async Task LoadRatingAsync(RatingService ratingService, int editVersion)
    {
        try
        {
            var (rating, source) = await ratingService.GetRatingAsync(ImageFile);
            ApplyLoadedRating(rating, source, editVersion);
        }
        catch
        {
            CancelRatingPreload(editVersion);
            throw;
        }
    }

    internal int BeginRatingPreload()
    {
        if (AppLifetime.IsShuttingDown || IsRatingLoaded)
        {
            return -1;
        }

        lock (_thumbnailLoadLock)
        {
            if (_isRatingLoadRequested || _isRatingLoaded)
            {
                return -1;
            }

            _isRatingLoadRequested = true;
            IsRatingLoading = true;
            return _ratingEditVersion;
        }
    }

    internal void ApplyLoadedRating(uint rating, RatingSource source, int expectedEditVersion)
    {
        lock (_thumbnailLoadLock)
        {
            if (_ratingEditVersion == expectedEditVersion)
            {
                SetRatingCore(rating);
                RatingSource = source;
                IsRatingLoaded = true;
            }

            IsRatingLoading = false;
            _isRatingLoadRequested = false;
        }
    }

    internal void CancelRatingPreload(int expectedEditVersion)
    {
        lock (_thumbnailLoadLock)
        {
            if (_ratingEditVersion != expectedEditVersion)
            {
                return;
            }

            IsRatingLoading = false;
            _isRatingLoadRequested = false;
        }
    }

    private void SetRatingCore(uint rating)
    {
        if (SetProperty(ref _rating, rating))
        {
            OnPropertyChanged(nameof(RatingValue));
        }
    }

    public async Task SetRatingAsync(RatingService ratingService, uint newRating)
    {
        var source = ratingService.IsWinRTRatingSupported(ImageFile.FileType)
            ? RatingSource.WinRT
            : RatingSource.Cache;

        lock (_thumbnailLoadLock)
        {
            _ratingEditVersion++;
            SetRatingCore(newRating);
            RatingSource = source;
            IsRatingLoaded = true;
            IsRatingLoading = false;
            _isRatingLoadRequested = false;
        }

        try
        {
            await ratingService.SetRatingAsync(ImageFile, newRating);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SetRatingAsync] error: {ex.Message}");
        }
    }
}
