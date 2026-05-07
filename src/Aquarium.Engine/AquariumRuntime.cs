using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;

namespace Aquarium.Engine;

public sealed class AquariumRuntime
{
    private readonly AquariumRuntimeOptions options;
    private readonly OrbitCameraRig cameraRig;

    private GridFrame gridFrame;

    public AquariumRuntime(AquariumRuntimeOptions options)
    {
        this.options = options;
        cameraRig = new OrbitCameraRig(
            target: Vector2.Zero,
            yawRadians: MathUtil.DegreesToRadians(35.0f),
            pitchRadians: MathUtil.DegreesToRadians(42.0f),
            distance: 24.0f);
        gridFrame = GridFrame.FromCamera(cameraRig);
    }

    public void Start(Scene rootScene)
    {
        _ = rootScene;
        Console.WriteLine("Aquarium Engine booted.");
        Console.WriteLine($"Grid center: {gridFrame.Center}, radius: {gridFrame.Radius:0.00}");
        Console.WriteLine("Stride is present as the host shell; Aquarium owns the invariants.");
    }

    public void Update(Scene rootScene, GameTime gameTime)
    {
        _ = rootScene;
        _ = gameTime;

        gridFrame = GridFrame.FromCamera(cameraRig);

        if (options.Headless)
        {
            Console.WriteLine("Headless smoke reached first update.");
            Environment.ExitCode = 0;
            Environment.Exit(0);
        }
    }
}

public readonly record struct AquariumRuntimeOptions(bool Headless);
