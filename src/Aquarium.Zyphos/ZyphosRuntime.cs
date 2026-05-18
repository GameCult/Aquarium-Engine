using System.Numerics;
using Aquarium.Engine;
using Aquarium.Engine.Audio;
using Aquarium.Engine.Input;
using Aquarium.Engine.Render;
using Aquarium.Engine.Ui;

namespace Aquarium.Zyphos;

public sealed class ZyphosRuntime : IAquariumRuntime
{
    private float timeSeconds;
    private float previousTimeSeconds;
    private float orbitYaw = -0.28f;
    private float orbitPitch = 0.34f;
    private float orbitDistance = 15.6f;
    private float timeScale = 1.0f;
    private bool autoOrbit = true;

    public AquariumRuntimeOptions Options { get; private set; }

    public GraphicsSettings GraphicsSettings { get; set; } = new(0, 1.08f, 0.32f, 0.05f);

    public AquariumRenderPlan RenderPlan { get; } = ZyphosRenderPlan.Create();

    public AquariumUiDocument Ui { get; }

    public AquariumAudioDocument Audio { get; } = new();

    public AquariumSynthDocument Synth { get; } = AquariumSynthDocument.Empty;

    public AquariumFrame Frame => new(
        new ViewFrame(Vector2.Zero, 32.0f),
        CameraPosition(),
        timeSeconds,
        Vector2.Zero,
        ZyphosSceneBuilder.Build(timeSeconds, previousTimeSeconds));

    public ZyphosRuntime()
    {
        Ui = new AquariumUiDocument()
            .Panel("Zyphos", 18.0f, 82.0f, 340.0f, panel =>
            {
                panel.Section("Planetary Demo");
                panel.Toggle("Auto Orbit", () => autoOrbit, value => autoOrbit = value);
                panel.Slider("Time Scale", () => timeScale, value => timeScale = value, 0.0f, 4.0f, "0.00");
                panel.Slider("Orbit Distance", () => orbitDistance, value => orbitDistance = value, 9.0f, 24.0f, "0.0");
                panel.Readout("Runtime", () => $"{timeSeconds:0.0}s");
                panel.Readout("Camera", () => $"{orbitDistance:0.0} wu / yaw {orbitYaw:0.00}");
                panel.Readout("Objects", () => "spherical terrain, atmosphere, moon");
            })
            .Command("zyphos", _ => "Zyphos: planetary-scale client demo loaded.", "Report Zyphos demo status.");
    }

    public AquariumFrame ComposeFrame(AquariumFrame frame, AquariumFrameInput input)
    {
        return frame;
    }

    public void Start()
    {
        Console.WriteLine("Zyphos planetary demo booted.");
    }

    public void Update(float deltaSeconds, InputState input)
    {
        previousTimeSeconds = timeSeconds;
        var safeDelta = Math.Max(deltaSeconds, 0.0f);
        timeSeconds += safeDelta * MathF.Max(timeScale, 0.0f);

        if (autoOrbit)
        {
            orbitYaw += safeDelta * 0.055f;
        }

        if (input.IsKeyDown(KeyCode.A))
        {
            orbitYaw -= safeDelta * 0.85f;
        }

        if (input.IsKeyDown(KeyCode.D))
        {
            orbitYaw += safeDelta * 0.85f;
        }

        if (input.IsKeyDown(KeyCode.W))
        {
            orbitPitch += safeDelta * 0.45f;
        }

        if (input.IsKeyDown(KeyCode.S))
        {
            orbitPitch -= safeDelta * 0.45f;
        }

        if (MathF.Abs(input.WheelDelta) > 0.0f)
        {
            orbitDistance = Math.Clamp(orbitDistance - input.WheelDelta * 0.8f, 8.0f, 26.0f);
        }

        orbitPitch = Math.Clamp(orbitPitch, 0.12f, 0.86f);
    }

    public void FlushState()
    {
    }

    public void Dispose()
    {
    }

    internal void SetOptions(AquariumRuntimeOptions options)
    {
        Options = options;
    }

    private Vector3 CameraPosition()
    {
        var horizontal = MathF.Cos(orbitPitch) * orbitDistance;
        return new Vector3(
            MathF.Sin(orbitYaw) * horizontal,
            -MathF.Cos(orbitYaw) * horizontal,
            MathF.Sin(orbitPitch) * orbitDistance + 3.1f);
    }
}

public sealed class ZyphosRuntimeFactory : IAquariumRuntimeFactory
{
    public IAquariumRuntime Create(AquariumRuntimeOptions options)
    {
        var runtime = new ZyphosRuntime();
        runtime.SetOptions(options);
        return runtime;
    }
}
