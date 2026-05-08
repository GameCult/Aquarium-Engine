using System.Runtime.InteropServices;

namespace Aquarium.Engine.Platform;

public sealed class Win32Window : IDisposable
{
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_SIZE = 0x0005;
    private const uint WM_CLOSE = 0x0010;
    private const uint PM_REMOVE = 0x0001;
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint WS_VISIBLE = 0x10000000;
    private const int SW_SHOW = 5;
    private const int IDC_ARROW = 32512;

    private readonly WndProc windowProcedure;
    private readonly string className;
    private bool disposed;

    private Win32Window(IntPtr handle, string className, WndProc windowProcedure, int width, int height)
    {
        Handle = handle;
        this.className = className;
        this.windowProcedure = windowProcedure;
        ClientWidth = width;
        ClientHeight = height;
    }

    public IntPtr Handle { get; }

    public int ClientWidth { get; private set; }

    public int ClientHeight { get; private set; }

    public static Win32Window Create(string title, int width, int height)
    {
        var className = $"AquariumEngineWindow-{Guid.NewGuid():N}";
        var instance = GetModuleHandle(null);
        WndProc? windowProcedure = null;

        windowProcedure = (handle, message, wParam, lParam) =>
        {
            switch (message)
            {
                case WM_CLOSE:
                    DestroyWindow(handle);
                    return IntPtr.Zero;
                case WM_DESTROY:
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }

            return DefWindowProc(handle, message, wParam, lParam);
        };

        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = windowProcedure,
            hInstance = instance,
            hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
            lpszClassName = className,
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }

        var handle = CreateWindowEx(
            0,
            className,
            title,
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }

        ShowWindow(handle, SW_SHOW);
        UpdateWindow(handle);

        return new Win32Window(handle, className, windowProcedure, width, height);
    }

    public bool PumpMessages()
    {
        while (PeekMessage(out var message, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            if (message.message == 0x0012)
            {
                return false;
            }

            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        if (GetClientRect(Handle, out var rect))
        {
            ClientWidth = Math.Max(1, rect.right - rect.left);
            ClientHeight = Math.Max(1, rect.bottom - rect.top);
        }

        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (Handle != IntPtr.Zero)
        {
            DestroyWindow(Handle);
        }

        GC.SuppressFinalize(this);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int commandShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateWindow(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PeekMessage(out MSG message, IntPtr handle, uint filterMin, uint filterMax, uint removeMessage);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr instance, int cursorName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr handle, out RECT rect);

    private delegate IntPtr WndProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}
