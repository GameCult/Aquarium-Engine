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
    private const uint WM_CHAR = 0x0102;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
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
    private const int ICON_SIZE_SPLASH = 192;
    private const int IDC_ARROW = 32512;
    private const int TRANSPARENT = 1;
    private const int FW_THIN = 100;
    private const int FW_REGULAR = 400;
    private const uint FR_PRIVATE = 0x00000010;
    private const uint DEFAULT_CHARSET = 1;
    private const uint OUT_DEFAULT_PRECIS = 0;
    private const uint CLIP_DEFAULT_PRECIS = 0;
    private const uint ANTIALIASED_QUALITY = 4;
    private const uint DEFAULT_PITCH = 0;
    private const uint FF_DONTCARE = 0;
    private const uint DT_CENTER = 0x00000001;
    private const uint DT_VCENTER = 0x00000004;
    private const uint DT_SINGLELINE = 0x00000020;
    private const int LOGPIXELSY = 90;
    private const int VK_W = 0x57;
    private const int VK_A = 0x41;
    private const int VK_S = 0x53;
    private const int VK_D = 0x44;
    private const int VK_0 = 0x30;
    private const int VK_1 = 0x31;
    private const int VK_2 = 0x32;
    private const int VK_3 = 0x33;
    private const int VK_4 = 0x34;
    private const int VK_5 = 0x35;
    private const int VK_6 = 0x36;
    private const int VK_7 = 0x37;
    private const int VK_8 = 0x38;
    private const int VK_9 = 0x39;
    private const int VK_F1 = 0x70;
    private const int VK_F2 = 0x71;
    private const int VK_BACK = 0x08;
    private const int VK_DELETE = 0x2E;
    private const int VK_RETURN = 0x0D;
    private const int VK_LEFT = 0x25;
    private const int VK_RIGHT = 0x27;
    private const int VK_HOME = 0x24;
    private const int VK_END = 0x23;
    private const int VK_SHIFT = 0x10;
    private const int WHEEL_DELTA = 120;

    private readonly WndProc windowProcedure;
    private readonly string className;
    private readonly InputState input;
    private readonly IntPtr largeIcon;
    private readonly IntPtr smallIcon;
    private readonly IntPtr splashIcon;
    private bool disposed;

    private Win32Window(
        IntPtr handle,
        string className,
        WndProc windowProcedure,
        InputState input,
        int width,
        int height,
        IntPtr largeIcon,
        IntPtr smallIcon,
        IntPtr splashIcon)
    {
        Handle = handle;
        this.className = className;
        this.windowProcedure = windowProcedure;
        this.input = input;
        this.largeIcon = largeIcon;
        this.smallIcon = smallIcon;
        this.splashIcon = splashIcon;
        ClientWidth = width;
        ClientHeight = height;
    }

    public IntPtr Handle { get; }

    public int ClientWidth { get; private set; }

    public int ClientHeight { get; private set; }

    public static Win32Window Create(string title, int width, int height, InputState input, string? iconPath = null, bool visible = true)
    {
        var className = $"AquariumEngineWindow-{Guid.NewGuid():N}";
        var instance = GetModuleHandle(null);
        var largeIcon = LoadIconFromPath(iconPath, ICON_SIZE_LARGE);
        var smallIcon = LoadIconFromPath(iconPath, ICON_SIZE_SMALL);
        var splashIcon = LoadIconFromPath(iconPath, ICON_SIZE_SPLASH);
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
                    case WM_LBUTTONDOWN:
                        input.SetMouseButton(MouseButton.Left, true);
                        SetCapture(handle);
                        return IntPtr.Zero;
                    case WM_LBUTTONUP:
                        input.SetMouseButton(MouseButton.Left, false);
                        ReleaseCapture();
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
                    case WM_CHAR:
                        AddTextInput(input, (UIntPtr)wParam);
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

            var style = WS_OVERLAPPEDWINDOW;
            if (visible)
            {
                style |= WS_VISIBLE;
            }

            var handle = CreateWindowEx(
                0,
                classNamePointer,
                titlePointer,
                style,
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

            if (visible)
            {
                ShowWindow(handle, SW_SHOW);
            }

            if (largeIcon != IntPtr.Zero)
            {
                SendMessage(handle, WM_SETICON, ICON_BIG, largeIcon);
            }

            if (smallIcon != IntPtr.Zero)
            {
                SendMessage(handle, WM_SETICON, ICON_SMALL, smallIcon);
            }

            if (visible)
            {
                UpdateWindow(handle);
            }

            SetWindowText(handle, title);

            return new Win32Window(handle, className, windowProcedure, input, width, height, largeIcon, smallIcon, splashIcon);
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePointer);
            Marshal.FreeHGlobal(titlePointer);
        }
    }

    public void PaintSplash(string header = "Aquarium", string message = "Preparing Aquarium")
    {
        if (!GetClientRect(Handle, out var rect))
        {
            return;
        }

        ClientWidth = Math.Max(1, rect.right - rect.left);
        ClientHeight = Math.Max(1, rect.bottom - rect.top);

        var deviceContext = GetDC(Handle);
        if (deviceContext == IntPtr.Zero)
        {
            return;
        }

        try
        {
            PaintDiagonalGradient(deviceContext, ClientWidth, ClientHeight);

            if (splashIcon != IntPtr.Zero)
            {
                var iconSize = Math.Min(ICON_SIZE_SPLASH, Math.Max(96, Math.Min(ClientWidth, ClientHeight) / 3));
                var iconX = (ClientWidth - iconSize) / 2;
                var textAnchorY = (int)MathF.Round(ClientHeight * (2.0f / 3.0f));
                var iconY = Math.Max(24, textAnchorY - iconSize - 84);
                DrawIconEx(deviceContext, iconX, iconY, splashIcon, iconSize, iconSize, 0, IntPtr.Zero, 0x0003);
            }

            PaintSplashText(deviceContext, ClientWidth, ClientHeight, header, message);
            GdiFlush();
        }
        finally
        {
            ReleaseDC(Handle, deviceContext);
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

        if (splashIcon != IntPtr.Zero)
        {
            DestroyIcon(splashIcon);
        }

        GC.SuppressFinalize(this);
    }

    private static void PaintDiagonalGradient(IntPtr deviceContext, int width, int height)
    {
        var pixels = new uint[width * height];
        var denominator = Math.Max(1.0f, width + height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var diagonal = (x + y) / denominator;
                var vignetteX = x / Math.Max(1.0f, width - 1.0f) - 0.5f;
                var vignetteY = y / Math.Max(1.0f, height - 1.0f) - 0.5f;
                var vignette = Math.Clamp(1.0f - (vignetteX * vignetteX + vignetteY * vignetteY) * 0.72f, 0.0f, 1.0f);
                var t = SmoothStep(0.0f, 1.0f, diagonal);
                pixels[y * width + x] = LerpColor(0x05, 0x08, 0x18, 0x06, 0x1A, 0x3A, t, vignette);
            }
        }

        var bitmapInfo = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            },
        };

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            StretchDIBits(
                deviceContext,
                0,
                0,
                width,
                height,
                0,
                0,
                width,
                height,
                handle.AddrOfPinnedObject(),
                ref bitmapInfo,
                0,
                0x00CC0020);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void PaintSplashText(IntPtr deviceContext, int width, int height, string header, string message)
    {
        LoadPrivateSplashFont("Montserrat[wght].ttf");
        LoadPrivateSplashFont("UbuntuSans[wdth,wght].ttf");

        var oldBackgroundMode = SetBkMode(deviceContext, TRANSPARENT);
        var oldTextColor = SetTextColor(deviceContext, ColorRef(224, 244, 255));
        var headerFont = CreateSplashFont(deviceContext, 26, FW_THIN, "Montserrat");
        var messageFont = CreateSplashFont(deviceContext, 12, FW_REGULAR, "Ubuntu Sans");

        try
        {
            var anchorY = (int)MathF.Round(height * (2.0f / 3.0f));
            var headerText = header.ToUpperInvariant();
            var headerRect = new RECT
            {
                left = 0,
                top = anchorY - 30,
                right = width,
                bottom = anchorY + 24
            };
            var messageRect = new RECT
            {
                left = Math.Max(0, width / 2 - 360),
                top = anchorY + 28,
                right = Math.Min(width, width / 2 + 360),
                bottom = anchorY + 56
            };

            var oldFont = SelectObject(deviceContext, headerFont);
            DrawText(deviceContext, headerText, headerText.Length, ref headerRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
            SelectObject(deviceContext, messageFont);
            SetTextColor(deviceContext, ColorRef(142, 179, 190));
            DrawText(deviceContext, message, message.Length, ref messageRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
            SelectObject(deviceContext, oldFont);
        }
        finally
        {
            DeleteObject(messageFont);
            DeleteObject(headerFont);
            SetTextColor(deviceContext, oldTextColor);
            SetBkMode(deviceContext, oldBackgroundMode);
        }
    }

    private static IntPtr CreateSplashFont(IntPtr deviceContext, int pointSize, int weight, string faceName)
    {
        var dpiY = Math.Max(1, GetDeviceCaps(deviceContext, LOGPIXELSY));
        var height = -MulDiv(pointSize, dpiY, 72);
        return CreateFont(
            height,
            0,
            0,
            0,
            weight,
            0,
            0,
            0,
            DEFAULT_CHARSET,
            OUT_DEFAULT_PRECIS,
            CLIP_DEFAULT_PRECIS,
            ANTIALIASED_QUALITY,
            DEFAULT_PITCH | FF_DONTCARE,
            faceName);
    }

    private static void LoadPrivateSplashFont(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", fileName);
        if (File.Exists(path))
        {
            AddFontResourceEx(path, FR_PRIVATE, IntPtr.Zero);
        }
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var t = Math.Clamp((value - edge0) / Math.Max(edge1 - edge0, 0.0001f), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    private static uint LerpColor(byte r0, byte g0, byte b0, byte r1, byte g1, byte b1, float t, float intensity)
    {
        var r = (byte)Math.Clamp(MathF.Round((r0 + (r1 - r0) * t) * intensity), byte.MinValue, byte.MaxValue);
        var g = (byte)Math.Clamp(MathF.Round((g0 + (g1 - g0) * t) * intensity), byte.MinValue, byte.MaxValue);
        var b = (byte)Math.Clamp(MathF.Round((b0 + (b1 - b0) * t) * intensity), byte.MinValue, byte.MaxValue);
        return (uint)(b | (g << 8) | (r << 16));
    }

    private static uint ColorRef(byte red, byte green, byte blue)
    {
        return (uint)(red | (green << 8) | (blue << 16));
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
    private static extern IntPtr GetDC(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr handle, IntPtr deviceContext);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DrawIconEx(
        IntPtr deviceContext,
        int left,
        int top,
        IntPtr icon,
        int width,
        int height,
        uint stepIfAnimated,
        IntPtr flickerFreeBrush,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, nuint wParam, IntPtr lParam);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int StretchDIBits(
        IntPtr deviceContext,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        IntPtr bits,
        ref BITMAPINFO bitmapInfo,
        uint usage,
        uint rasterOperation);

    [DllImport("gdi32.dll")]
    private static extern bool GdiFlush();

    [DllImport("gdi32.dll", EntryPoint = "AddFontResourceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string name, uint flags, IntPtr reserved);

    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint charSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetBkMode(IntPtr deviceContext, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern uint SetTextColor(IntPtr deviceContext, uint color);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDeviceCaps(IntPtr deviceContext, int index);

    [DllImport("kernel32.dll")]
    private static extern int MulDiv(int number, int numerator, int denominator);

    [DllImport("user32.dll", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DrawText(IntPtr deviceContext, string text, int textLength, ref RECT rect, uint format);

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
            case VK_0:
                input.SetKey(KeyCode.Digit0, isDown);
                break;
            case VK_1:
                input.SetKey(KeyCode.Digit1, isDown);
                break;
            case VK_2:
                input.SetKey(KeyCode.Digit2, isDown);
                break;
            case VK_3:
                input.SetKey(KeyCode.Digit3, isDown);
                break;
            case VK_4:
                input.SetKey(KeyCode.Digit4, isDown);
                break;
            case VK_5:
                input.SetKey(KeyCode.Digit5, isDown);
                break;
            case VK_6:
                input.SetKey(KeyCode.Digit6, isDown);
                break;
            case VK_7:
                input.SetKey(KeyCode.Digit7, isDown);
                break;
            case VK_8:
                input.SetKey(KeyCode.Digit8, isDown);
                break;
            case VK_9:
                input.SetKey(KeyCode.Digit9, isDown);
                break;
            case VK_F1:
                input.SetKey(KeyCode.RenderDebugCycle, isDown);
                break;
            case VK_F2:
                input.SetKey(KeyCode.DebugUiToggle, isDown);
                break;
            case VK_BACK:
                input.SetKey(KeyCode.Backspace, isDown);
                break;
            case VK_DELETE:
                input.SetKey(KeyCode.Delete, isDown);
                break;
            case VK_RETURN:
                input.SetKey(KeyCode.Enter, isDown);
                break;
            case VK_LEFT:
                input.SetKey(KeyCode.LeftArrow, isDown);
                break;
            case VK_RIGHT:
                input.SetKey(KeyCode.RightArrow, isDown);
                break;
            case VK_HOME:
                input.SetKey(KeyCode.Home, isDown);
                break;
            case VK_END:
                input.SetKey(KeyCode.End, isDown);
                break;
            case VK_SHIFT:
                input.SetKey(KeyCode.Shift, isDown);
                break;
        }
    }

    private static void AddTextInput(InputState input, UIntPtr wParam)
    {
        var value = (char)wParam.ToUInt32();
        if (!char.IsControl(value) || value is '\r' or '\n' or '\b')
        {
            input.AddTextInput(value);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }
}
