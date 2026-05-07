using Stride.CommunityToolkit.Engine;
using Stride.Engine;
using Stride.Games;

namespace Aquarium.Engine;

public static class AquariumHost
{
    public static int Run(string[] args)
    {
        using var game = new Game();
        var runtime = new AquariumRuntime(new AquariumRuntimeOptions(ParseHeadless(args)));

        game.Run(
            context: null,
            start: runtime.Start,
            update: runtime.Update);

        return 0;
    }

    private static bool ParseHeadless(IEnumerable<string> args)
    {
        return args.Any(arg => string.Equals(arg, "--headless", StringComparison.OrdinalIgnoreCase));
    }
}
