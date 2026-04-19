using System.Runtime.InteropServices;
using System.Threading;
using PhotoView.Contracts.Services;

namespace PhotoView.Services;

public sealed class ExternalDeviceWatcherService : IExternalDeviceWatcherService, IDisposable
{
    private const int DebounceDelayMs = 500;
    private const uint WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVNODES_CHANGED = 0x0007;
    private static readonly nuint SubclassId = unchecked((nuint)0x5048564558544457UL); // PHVEXTDW

    private readonly object _lock = new();
    private readonly SubclassProc _subclassProc;
    private Timer? _debounceTimer;
    private nint _hwnd;
    private bool _isAttached;
    private bool _isDisposed;

    public event EventHandler? ExternalDevicesChanged;

    public ExternalDeviceWatcherService()
    {
        _subclassProc = WndProc;
    }

    public void AttachWindow(nint hwnd)
    {
        if (hwnd == 0)
            return;

        lock (_lock)
        {
            if (_isDisposed)
                return;

            if (_isAttached && _hwnd == hwnd)
                return;

            DetachWindowCore();

            if (!SetWindowSubclass(hwnd, _subclassProc, SubclassId, 0))
            {
                System.Diagnostics.Debug.WriteLine("[ExternalDeviceWatcher] SetWindowSubclass failed.");
                return;
            }

            _hwnd = hwnd;
            _isAttached = true;
        }
    }

    public void DetachWindow()
    {
        lock (_lock)
        {
            DetachWindowCore();
        }
    }

    private void DetachWindowCore()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        if (_isAttached && _hwnd != 0)
        {
            RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
        }

        _hwnd = 0;
        _isAttached = false;
    }

    private nint WndProc(
        nint hwnd,
        uint msg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData)
    {
        if (msg == WM_DEVICECHANGE && IsExternalDeviceChange(wParam))
        {
            ScheduleChangeNotification();
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private static bool IsExternalDeviceChange(nint wParam)
    {
        var eventCode = wParam.ToInt64();
        return eventCode == DBT_DEVICEARRIVAL ||
               eventCode == DBT_DEVICEREMOVECOMPLETE ||
               eventCode == DBT_DEVNODES_CHANGED;
    }

    private void ScheduleChangeNotification()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _debounceTimer ??= new Timer(OnDebounceTimerTick);
            _debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
        }
    }

    private void OnDebounceTimerTick(object? state)
    {
        ExternalDevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            DetachWindowCore();
        }
    }

    private delegate nint SubclassProc(
        nint hwnd,
        uint msg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        nuint uIdSubclass,
        nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint hWnd,
        uint uMsg,
        nint wParam,
        nint lParam);
}
