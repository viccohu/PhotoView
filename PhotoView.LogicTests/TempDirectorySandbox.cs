namespace PhotoView.LogicTests;

internal sealed class TempDirectorySandbox : IDisposable
{
    public TempDirectorySandbox()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "PhotoViewLogicTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
