using PhotoView.Models;

namespace PhotoView.LogicTests;

internal static class ImageFormatRegistryChecks
{
    public static void Run()
    {
        NormalizeExtension_AddsLeadingDotAndLowercases();
        GetFormatPriority_RespectsPsdPreference();
        RawExtensions_AreRecognized();
    }

    private static void NormalizeExtension_AddsLeadingDotAndLowercases()
    {
        var normalized = ImageFormatRegistry.NormalizeExtension(" JPG ");

        TestAssert.Equal(".jpg", normalized, "NormalizeExtension should trim, lowercase and add a dot.");
    }

    private static void GetFormatPriority_RespectsPsdPreference()
    {
        var preferred = ImageFormatRegistry.GetFormatPriority(".psd", preferPsdAsPrimaryPreview: true);
        var standard = ImageFormatRegistry.GetFormatPriority(".jpg", preferPsdAsPrimaryPreview: false);

        TestAssert.True(preferred < standard, "PSD should outrank JPG when PSD preference is enabled.");
    }

    private static void RawExtensions_AreRecognized()
    {
        TestAssert.True(ImageFormatRegistry.IsRaw(".cr3"), "CR3 should be recognized as RAW.");
        TestAssert.True(ImageFormatRegistry.IsSupported(".psb"), "PSB should be recognized as supported.");
    }
}
