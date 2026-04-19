namespace PhotoView.Contracts.Services;

public interface IExternalDeviceWatcherService
{
    event EventHandler? ExternalDevicesChanged;

    void AttachWindow(nint hwnd);

    void DetachWindow();
}
