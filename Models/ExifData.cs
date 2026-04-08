using System;
using PhotoView.Services;

namespace PhotoView.Models;

public class ExifData
{
    #region 基本属性

    public DateTime? DateTaken { get; set; }

    public string? CameraManufacturer { get; set; }

    public string? CameraModel { get; set; }

    public uint Width { get; set; }

    public uint Height { get; set; }

    public ushort? Orientation { get; set; }

    public double? DpiX { get; set; }

    public double? DpiY { get; set; }

    public ushort? BitDepth { get; set; }

    public string? PixelFormat { get; set; }

    #endregion

    #region 拍摄参数

    public double? ExposureTime { get; set; }

    public double? FNumber { get; set; }

    public uint? ISOSpeed { get; set; }

    public double? FocalLength { get; set; }

    public double? FocalLengthInFilm { get; set; }

    public ushort? ExposureProgram { get; set; }

    public double? ExposureBias { get; set; }

    public double? MaxAperture { get; set; }

    #endregion

    #region 其他信息

    public ushort? Flash { get; set; }

    public ushort? MeteringMode { get; set; }

    public ushort? WhiteBalance { get; set; }

    public string? LensModel { get; set; }

    #endregion

    #region 评级信息

    public uint Rating { get; set; }

    public RatingSource RatingSource { get; set; }

    #endregion

    #region 辅助方法

    public string GetFormattedExposureTime()
    {
        if (ExposureTime == null) return string.Empty;

        double time = ExposureTime.Value;
        if (time < 1.0)
        {
            int denominator = (int)Math.Round(1.0 / time);
            return $"1/{denominator}s";
        }
        else
        {
            return $"{time:F1}s";
        }
    }

    public string GetFormattedFNumber()
    {
        if (FNumber == null) return string.Empty;
        return $"f/{FNumber.Value:F1}";
    }

    public string GetFormattedFocalLength()
    {
        if (FocalLength == null) return string.Empty;
        return $"{FocalLength.Value:F0}mm";
    }

    public string GetFormattedExposureProgram()
    {
        return ExposureProgram switch
        {
            1 => "Manual",
            2 => "Normal",
            3 => "Aperture Priority",
            4 => "Shutter Priority",
            5 => "Creative",
            6 => "Action",
            7 => "Portrait",
            8 => "Landscape",
            _ => "Unknown"
        };
    }

    public string GetFormattedFlash()
    {
        if (Flash == null) return string.Empty;

        // 简单处理，实际可以更详细
        return (Flash.Value & 0x01) != 0 ? "Flash" : "No Flash";
    }

    #endregion
}
