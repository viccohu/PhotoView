using PhotoView.Helpers;

namespace PhotoView;

public sealed partial class MainWindow : WindowEx
{
    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();

        this.Closed += (_, __) =>
        {
            AppLifetime.IsShuttingDown = true;
        };
    }
}
