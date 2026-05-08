using System.Diagnostics;
using Aquarium.Engine.Input;
using Aquarium.Engine.Platform;
using Aquarium.Engine.Render;

namespace Aquarium.Engine;

public static class AquariumHost
{
    public static int Run(string[] args)
    {
        using var runtime = new AquariumRuntime(new AquariumRuntimeOptions(ParseHeadless(args), ParseCachePath(args)));
        var input = new InputState();
        var width = runtime.Options.Headless ? 640 : 1280;
        var height = runtime.Options.Headless ? 360 : 720;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Aquarium-Engine-Icon.ico");
        using var window = Win32Window.Create("Epiphany Aquarium Engine", width, height, input, iconPath);
        window.PaintSplash();
        using var renderer = new D3D11Renderer(window.Handle, window.ClientWidth, window.ClientHeight);

        runtime.Start();

        var frameClock = Stopwatch.StartNew();
        var lastFrame = frameClock.Elapsed;
        var frames = 0;

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

            runtime.Update(deltaSeconds, input);
            renderer.Render(runtime.Frame);

            if (runtime.Options.Headless && ++frames >= 2)
            {
                break;
            }
        }

        return 0;
    }

    private static bool ParseHeadless(IEnumerable<string> args)
    {
        return args.Any(arg => string.Equals(arg, "--headless", StringComparison.OrdinalIgnoreCase));
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
}
