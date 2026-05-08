namespace Aquarium.Engine;

public sealed class AquariumRuntime
{
    private readonly OrbitCameraRig cameraRig;

    private GridFrame gridFrame;
    private float timeSeconds;

    public AquariumRuntime(AquariumRuntimeOptions options)
    {
        Options = options;
        cameraRig = new OrbitCameraRig(
            target: System.Numerics.Vector2.Zero,
            yawRadians: Angle.DegreesToRadians(35.0f),
            pitchRadians: Angle.DegreesToRadians(42.0f),
            distance: 24.0f);
        gridFrame = GridFrame.FromCamera(cameraRig);
    }

    public AquariumRuntimeOptions Options { get; }

    public AquariumFrame Frame => new(gridFrame, cameraRig.Position, timeSeconds);

    public void Start()
    {
        Console.WriteLine("Aquarium Engine booted.");
        Console.WriteLine($"Grid center: {gridFrame.Center}, radius: {gridFrame.Radius:0.00}");
        Console.WriteLine("Vortice D3D11 is present as the host shell; Aquarium owns the invariants.");
    }

    public void Update(float deltaSeconds)
    {
        _ = deltaSeconds;

        timeSeconds += Math.Max(deltaSeconds, 0.0f);
        gridFrame = GridFrame.FromCamera(cameraRig);
    }
}

public readonly record struct AquariumRuntimeOptions(bool Headless);

public readonly record struct AquariumFrame(GridFrame Grid, System.Numerics.Vector3 CameraPosition, float TimeSeconds);
