namespace PhotoView.LogicTests;

internal static class Program
{
    public static int Main()
    {
        try
        {
            FilterViewModelChecks.Run();
            ImageFormatRegistryChecks.Run();
            PreviewSourceChecks.Run();
            PreviewWorkspaceServiceChecks.Run();
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
