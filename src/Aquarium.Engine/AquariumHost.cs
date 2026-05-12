using System.Diagnostics;
using Aquarium.Engine.Input;
using Aquarium.Engine.Platform;
using Aquarium.Engine.Render;
using Aquarium.Engine.Runtime;

namespace Aquarium.Engine;

public static class AquariumHost
{
    public static int Run(string[] args)
    {
        var runtimeOptions = new AquariumRuntimeOptions(ParseHeadless(args), ParseCachePath(args), ParseRenderDebugMode(args));
        using var runtimeLoader = new ClientRuntimeLoader(runtimeOptions, ParseClientAssemblyPath(args), ParseClientReloadPointerPath(args));
        var runtime = runtimeLoader.Load();
        var input = new InputState();
        var width = runtime.Options.Headless ? 640 : 1280;
        var height = runtime.Options.Headless ? 360 : 720;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Aquarium-Engine-Icon.ico");
        using var window = Win32Window.Create("Aquarium Engine", width, height, input, iconPath, visible: !runtime.Options.Headless);
        window.PaintSplash("Aquarium", "Preparing runtime state");
        using var renderer = CreateRenderer(
            window.Handle,
            window.ClientWidth,
            window.ClientHeight,
            ParseShaderPath(args),
            runtime.RenderPlan,
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

            var renderFrame = runtimeLoader.Runtime.ComposeFrame(
                runtimeLoader.Runtime.Frame,
                new AquariumFrameInput(input.MousePosition, window.ClientWidth, window.ClientHeight));
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

    private static string? ParseClientAssemblyPath(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--client-assembly", StringComparison.OrdinalIgnoreCase)
                || string.Equals(values[index], "--live-assembly", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_CLIENT_ASSEMBLY")
            ?? Environment.GetEnvironmentVariable("AQUARIUM_LIVE_ASSEMBLY");
    }

    private static string? ParseClientReloadPointerPath(IReadOnlyCollection<string> args)
    {
        var values = args.ToArray();
        for (var index = 0; index < values.Length - 1; index++)
        {
            if (string.Equals(values[index], "--client-reload-pointer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(values[index], "--live-reload-pointer", StringComparison.OrdinalIgnoreCase))
            {
                return values[index + 1];
            }
        }

        return Environment.GetEnvironmentVariable("AQUARIUM_CLIENT_RELOAD_POINTER")
            ?? Environment.GetEnvironmentVariable("AQUARIUM_LIVE_RELOAD_POINTER");
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

    private static IAquariumRenderer CreateRenderer(
        IntPtr windowHandle,
        int width,
        int height,
        string? shaderPath,
        AquariumRenderPlan renderPlan,
        GraphicsSettings graphicsSettings,
        Action<string>? startupProgress)
    {
        return new D3D12Renderer(windowHandle, width, height, shaderPath, renderPlan, graphicsSettings, startupProgress);
    }

    private static void SyncRendererSettingsToRuntime(IAquariumRenderer renderer, IAquariumRuntime runtime)
    {
        var settings = renderer.CaptureGraphicsSettings();
        if (settings != runtime.GraphicsSettings)
        {
            runtime.GraphicsSettings = settings;
        }
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
    }
}
