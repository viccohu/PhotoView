namespace PhotoView.Helpers;

public static class AppLifetime
{
    public static volatile bool IsShuttingDown = false;

    public static void BeginShutdown()
    {
        IsShuttingDown = true;
    }
}
