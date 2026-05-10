using System.Diagnostics;
using Aquarium.Engine.Input;
using Aquarium.Engine.Live;
using Aquarium.Engine.Platform;
using Aquarium.Engine.Render;
using System.Numerics;

namespace Aquarium.Engine;

public static class AquariumHost
{
    public static int Run(string[] args)
    {
        var runtimeOptions = new AquariumRuntimeOptions(ParseHeadless(args), ParseCachePath(args), ParseRenderDebugMode(args));
        using var runtimeLoader = new LiveRuntimeLoader(runtimeOptions, ParseLiveAssemblyPath(args), ParseLiveReloadPointerPath(args));
        var runtime = runtimeLoader.Load();
        var input = new InputState();
        var width = runtime.Options.Headless ? 640 : 1280;
        var height = runtime.Options.Headless ? 360 : 720;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Aquarium-Engine-Icon.ico");
        using var window = Win32Window.Create("Epiphany Aquarium Engine", width, height, input, iconPath, visible: !runtime.Options.Headless);
        window.PaintSplash("Aquarium", "Preparing runtime state");
        using var renderer = CreateRenderer(
            ParseRendererBackend(args),
            window.Handle,
            window.ClientWidth,
            window.ClientHeight,
            ParseShaderPath(args),
            runtime.GraphicsSettings,
            body => window.PaintSplash("Aquarium", body));
        var settingsRuntime = runtimeLoader.Runtime;

        var frameClock = Stopwatch.StartNew();
        var lastFrame = frameClock.Elapsed;
        var frames = 0;
        var readyFrames = 0;
        var requiredReadyFrames = ParseHeadlessReadyFrames();

        while (true)
        {
            input.BeginFrame();
            if (!window.PumpMessages())
            {
                break;
            }

            var now = frameClock.Elapsed;
            var deltaSeconds = (float)(now - lastFrame).TotalSeconds;
            lastFrame = now;

            renderer.UpdateDebugUi(input);
            ApplyRendererDebugInput(renderer, input);
            SyncRendererSettingsToRuntime(renderer, runtimeLoader.Runtime);
            runtimeLoader.Update(deltaSeconds, input);
            if (!ReferenceEquals(settingsRuntime, runtimeLoader.Runtime))
            {
                settingsRuntime = runtimeLoader.Runtime;
                renderer.ApplyGraphicsSettings(settingsRuntime.GraphicsSettings);
            }

            var renderFrame = runtimeLoader.Runtime.Frame with
            {
                CursorWorld = ProjectMouseToGridPlane(
                    input.MousePosition,
                    window.ClientWidth,
                    window.ClientHeight,
                    runtimeLoader.Runtime.Frame.CameraPosition,
                    runtimeLoader.Runtime.Frame.Grid.Center),
            };
            renderer.Render(renderFrame, window.ClientWidth, window.ClientHeight);
            if (!runtime.Options.Headless && !renderer.HasPresentedReadyFrame)
            {
                window.PaintSplash("Aquarium", "Compiling renderer pipelines");
            }

            frames++;
            if (renderer.HasPresentedReadyFrame)
            {
                readyFrames++;
            }

            if (runtime.Options.Headless && frames >= 2 && readyFrames >= requiredReadyFrames)
            {
                Console.WriteLine("Headless Aquarium completed requested frames.");
                break;
            }
        }

        return 0;
    }

    private static bool ParseHeadless(IEnumerable<string> args)
    {
        return args.Any(arg => string.Equals(arg, "--headless", StringComparison.OrdinalIgnoreCase));
    }

    private static int ParseHeadlessReadyFrames()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("AQUARIUM_HEADLESS_READY_FRAMES"), out var value)
            ? Math.Max(1, value)
            : 1;
    }

    private static string? ParseCachePath(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--cache", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_CULTCACHE_PATH");
    }

    private static string? ParseShaderPath(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--shader-source", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_SHADER_SOURCE");
    }

    private static string? ParseLiveAssemblyPath(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--live-assembly", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_LIVE_ASSEMBLY");
    }

    private static string? ParseLiveReloadPointerPath(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--live-reload-pointer", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_LIVE_RELOAD_POINTER");
    }

    private static int? ParseRenderDebugMode(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--render-debug", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(values[index + 1], out var mode))
            {
                return mode;
            }
        }

        return int.TryParse(Environment.GetEnvironmentVariable("AQUARIUM_RENDER_DEBUG_MODE"), out var environmentMode)
            ? environmentMode
            : null;
    }

    private static string ParseRendererBackend(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--renderer", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_RENDERER") ?? "d3d12";
    }

    private static IAquariumRenderer CreateRenderer(
        string rendererBackend,
        IntPtr windowHandle,
        int width,
        int height,
        string? shaderPath,
        GraphicsSettings graphicsSettings,
        Action<string>? startupProgress)
    {
        if (string.Equals(rendererBackend, "d3d12", StringComparison.OrdinalIgnoreCase))
        {
            return new D3D12Renderer(windowHandle, width, height, shaderPath, graphicsSettings, startupProgress);
        }

        if (!string.Equals(rendererBackend, "d3d11", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown renderer backend '{rendererBackend}'. Expected d3d11 or d3d12.");
        }

        return new D3D11Renderer(windowHandle, width, height, shaderPath, graphicsSettings, startupProgress);
    }

    private static void SyncRendererSettingsToRuntime(IAquariumRenderer renderer, IAquariumRuntime runtime)
    {
        var settings = renderer.CaptureGraphicsSettings();
        if (settings != runtime.GraphicsSettings)
        {
            runtime.GraphicsSettings = settings;
        }
    }

    private static Vector2 ProjectMouseToGridPlane(Vector2 mousePosition, int width, int height, Vector3 cameraPosition, Vector2 gridCenter)
    {
        if (width <= 0 || height <= 0)
        {
            return gridCenter;
        }

        var pixel = new Vector2(
            Math.Clamp(mousePosition.X, 0.0f, MathF.Max(width - 1.0f, 0.0f)),
            MathF.Max(height, 1.0f) - Math.Clamp(mousePosition.Y, 0.0f, MathF.Max(height - 1.0f, 0.0f)));
        var rayDirection = RayDirectionForPixel(pixel, width, height, cameraPosition, gridCenter);
        if (MathF.Abs(rayDirection.Z) < 0.0001f)
        {
            return gridCenter;
        }

        var travel = -cameraPosition.Z / rayDirection.Z;
        if (travel <= 0.0f || !float.IsFinite(travel))
        {
            return gridCenter;
        }

        var world = cameraPosition + rayDirection * travel;
        return new Vector2(world.X, world.Y);
    }

    private static Vector3 RayDirectionForPixel(Vector2 pixel, int width, int height, Vector3 cameraPosition, Vector2 gridCenter)
    {
        var resolution = new Vector2(width, height);
        var ndc = ((pixel * 2.0f) - resolution) / MathF.Max(height, 1.0f);
        var target = new Vector3(gridCenter.X, gridCenter.Y, 0.0f);
        var forward = Vector3.Normalize(target - cameraPosition);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var up = Vector3.Cross(right, forward);
        return Vector3.Normalize(forward * 1.6f + right * ndc.X + up * ndc.Y);
    }

    private static void ApplyRendererDebugInput(IAquariumRenderer renderer, InputState input)
    {
        if (input.IsKeyPressed(KeyCode.RenderDebugCycle))
        {
            renderer.CycleRenderDebugMode();
        }

        if (input.IsKeyPressed(KeyCode.Digit0))
        {
            renderer.RenderDebugMode = 0;
        }
        else if (input.IsKeyPressed(KeyCode.Digit1))
        {
            renderer.RenderDebugMode = 1;
        }
        else if (input.IsKeyPressed(KeyCode.Digit2))
        {
            renderer.RenderDebugMode = 2;
        }
        else if (input.IsKeyPressed(KeyCode.Digit3))
        {
            renderer.RenderDebugMode = 3;
        }
        else if (input.IsKeyPressed(KeyCode.Digit4))
        {
            renderer.RenderDebugMode = 4;
        }
        else if (input.IsKeyPressed(KeyCode.Digit5))
        {
            renderer.RenderDebugMode = 5;
        }
        else if (input.IsKeyPressed(KeyCode.Digit6))
        {
            renderer.RenderDebugMode = 6;
        }
        else if (input.IsKeyPressed(KeyCode.Digit7))
        {
            renderer.RenderDebugMode = 7;
        }
        else if (input.IsKeyPressed(KeyCode.Digit8))
        {
            renderer.RenderDebugMode = 8;
        }
        else if (input.IsKeyPressed(KeyCode.Digit9))
        {
            renderer.RenderDebugMode = 9;
        }
    }
}
