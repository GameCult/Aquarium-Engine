using System.Runtime.InteropServices;
using System.Numerics;
using Aquarium.Engine.Input;

namespace Aquarium.Engine.Platform;

public sealed class Win32Window : IDisposable
{
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const uint PM_REMOVE = 0x0001;
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint WS_VISIBLE = 0x10000000;
    private const int SW_SHOW = 5;
    private const uint WM_SETICON = 0x0080;
    private const nuint ICON_SMALL = 0;
    private const nuint ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const int ICON_SIZE_LARGE = 32;
    private const int ICON_SIZE_SMALL = 16;
    private const int IDC_ARROW = 32512;
    private const int VK_W = 0x57;
    private const int VK_A = 0x41;
    private const int VK_S = 0x53;
    private const int VK_D = 0x44;
    private const int WHEEL_DELTA = 120;

    private readonly WndProc windowProcedure;
    private readonly string className;
    private readonly InputState input;
    private readonly IntPtr largeIcon;
    private readonly IntPtr smallIcon;
    private bool disposed;

    private Win32Window(
        IntPtr handle,
        string className,
        WndProc windowProcedure,
        InputState input,
        int width,
        int height,
        IntPtr largeIcon,
        IntPtr smallIcon)
    {
        Handle = handle;
        this.className = className;
        this.windowProcedure = windowProcedure;
        this.input = input;
        this.largeIcon = largeIcon;
        this.smallIcon = smallIcon;
        ClientWidth = width;
        ClientHeight = height;
    }

    public IntPtr Handle { get; }

    public int ClientWidth { get; private set; }

    public int ClientHeight { get; private set; }

    public static Win32Window Create(string title, int width, int height, InputState input, string? iconPath = null)
    {
        var className = $"AquariumEngineWindow-{Guid.NewGuid():N}";
        var instance = GetModuleHandle(null);
        var largeIcon = LoadIconFromPath(iconPath, ICON_SIZE_LARGE);
        var smallIcon = LoadIconFromPath(iconPath, ICON_SIZE_SMALL);
        var classNamePointer = Marshal.StringToHGlobalUni(className);
        var titlePointer = Marshal.StringToHGlobalUni(title);
        WndProc? windowProcedure = null;

        try
        {
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
                    case WM_MOUSEMOVE:
                        input.SetMousePosition(new Vector2(GetSignedLowWord(lParam), GetSignedHighWord(lParam)));
                        return IntPtr.Zero;
                    case WM_MOUSEWHEEL:
                        input.AddWheelDelta(GetSignedHighWord(wParam) / (float)WHEEL_DELTA);
                        return IntPtr.Zero;
                    case WM_MBUTTONDOWN:
                        input.SetMouseButton(MouseButton.Middle, true);
                        SetCapture(handle);
                        return IntPtr.Zero;
                    case WM_MBUTTONUP:
                        input.SetMouseButton(MouseButton.Middle, false);
                        ReleaseCapture();
                        return IntPtr.Zero;
                    case WM_RBUTTONDOWN:
                        input.SetMouseButton(MouseButton.Right, true);
                        SetCapture(handle);
                        return IntPtr.Zero;
                    case WM_RBUTTONUP:
                        input.SetMouseButton(MouseButton.Right, false);
                        ReleaseCapture();
                        return IntPtr.Zero;
                    case WM_KEYDOWN:
                        SetKey(input, wParam, true);
                        return IntPtr.Zero;
                    case WM_KEYUP:
                        SetKey(input, wParam, false);
                        return IntPtr.Zero;
                }

                return DefWindowProc(handle, message, wParam, lParam);
            };

            var windowClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = windowProcedure,
                hInstance = instance,
                hIcon = largeIcon,
                hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
                lpszClassName = classNamePointer,
                hIconSm = smallIcon,
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
            }

            var handle = CreateWindowEx(
                0,
                classNamePointer,
                titlePointer,
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
            if (largeIcon != IntPtr.Zero)
            {
                SendMessage(handle, WM_SETICON, ICON_BIG, largeIcon);
            }

            if (smallIcon != IntPtr.Zero)
            {
                SendMessage(handle, WM_SETICON, ICON_SMALL, smallIcon);
            }

            UpdateWindow(handle);
            SetWindowText(handle, title);

            return new Win32Window(handle, className, windowProcedure, input, width, height, largeIcon, smallIcon);
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePointer);
            Marshal.FreeHGlobal(titlePointer);
        }
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

        if (largeIcon != IntPtr.Zero)
        {
            DestroyIcon(largeIcon);
        }

        if (smallIcon != IntPtr.Zero)
        {
            DestroyIcon(smallIcon);
        }

        GC.SuppressFinalize(this);
    }

    private static IntPtr LoadIconFromPath(string? iconPath, int size)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return IntPtr.Zero;
        }

        return LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        IntPtr className,
        IntPtr windowName,
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

    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowText(IntPtr handle, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string imageName, uint type, int desiredWidth, int desiredHeight, uint load);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, nuint wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateWindow(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW", SetLastError = true)]
    private static extern bool PeekMessage(out MSG message, IntPtr handle, uint filterMin, uint filterMax, uint removeMessage);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr instance, int cursorName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr handle, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private static void SetKey(InputState input, IntPtr virtualKey, bool isDown)
    {
        switch (virtualKey.ToInt32())
        {
            case VK_W:
                input.SetKey(KeyCode.W, isDown);
                break;
            case VK_A:
                input.SetKey(KeyCode.A, isDown);
                break;
            case VK_S:
                input.SetKey(KeyCode.S, isDown);
                break;
            case VK_D:
                input.SetKey(KeyCode.D, isDown);
                break;
        }
    }

    private static short GetSignedLowWord(IntPtr value) => unchecked((short)((long)value & 0xFFFF));

    private static short GetSignedHighWord(IntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));

    private static short GetSignedHighWord(UIntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));

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
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
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
