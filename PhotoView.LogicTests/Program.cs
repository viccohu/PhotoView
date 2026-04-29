namespace PhotoView.LogicTests;

internal static class Program
{
    public static int Main()
    {
        try
        {
            FilterViewModelChecks.Run();
            CollectPreviewLoadStateEvaluatorChecks.Run();
            GroupRatingSyncHelperChecks.Run();
            ImageFormatRegistryChecks.Run();
            MainPageLocalizationChecks.Run();
            PreviewSourceChecks.Run();
            PreviewWorkspaceServiceChecks.Run();
            ThumbnailRangeHelperChecks.Run();
            Console.WriteLine("All logic checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
