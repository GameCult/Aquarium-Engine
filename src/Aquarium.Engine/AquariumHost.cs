using System.Diagnostics;
using Aquarium.Engine.Platform;
using Aquarium.Engine.Render;

namespace Aquarium.Engine;

public static class AquariumHost
{
    public static int Run(string[] args)
    {
        var runtime = new AquariumRuntime(new AquariumRuntimeOptions(ParseHeadless(args)));
        using var window = Win32Window.Create("Aquarium Engine", 1280, 720);
        using var renderer = new D3D11Renderer(window.Handle, window.ClientWidth, window.ClientHeight);

        runtime.Start();

        var frameClock = Stopwatch.StartNew();
        var lastFrame = frameClock.Elapsed;
        var frames = 0;

        while (window.PumpMessages())
        {
            var now = frameClock.Elapsed;
            var deltaSeconds = (float)(now - lastFrame).TotalSeconds;
            lastFrame = now;

            runtime.Update(deltaSeconds);
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
}
