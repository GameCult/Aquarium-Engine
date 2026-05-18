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
    private ZyphosCameraFrame cameraFrame;

    public AquariumRuntimeOptions Options { get; private set; }

    public GraphicsSettings GraphicsSettings { get; set; } = new(0, 1.08f, 0.32f, 0.05f);

    public AquariumRenderPlan RenderPlan { get; } = ZyphosRenderPlan.Create();

    public AquariumUiDocument Ui { get; }

    public AquariumAudioDocument Audio { get; } = new();

    public AquariumSynthDocument Synth { get; } = AquariumSynthDocument.Empty;

    public AquariumFrame Frame
    {
        get
        {
            var shot = CurrentShot();
            return new AquariumFrame(
                new ViewFrame(new Vector2(shot.CameraTarget.X, shot.CameraTarget.Y), MathF.Max(32.0f, shot.EffectiveDistance * 1.15f)),
                shot.CameraPosition,
                shot.CameraTarget,
                timeSeconds,
                Vector2.Zero,
                ZyphosSceneBuilder.Build(timeSeconds, previousTimeSeconds));
        }
    }

    public ZyphosRuntime()
    {
        Ui = new AquariumUiDocument()
            .Panel("Zyphos", 18.0f, 82.0f, 340.0f, panel =>
            {
                panel.Section("Planetary Demo");
                panel.Toggle("Auto Orbit", () => autoOrbit, value => autoOrbit = value);
                panel.Slider("Time Scale", () => timeScale, value => timeScale = value, 0.0f, 4.0f, "0.00");
                panel.Slider("Frame", () => (int)cameraFrame, value => cameraFrame = (ZyphosCameraFrame)Math.Clamp(value, 0, 2), 0, 2);
                panel.Slider("Orbit Distance", () => orbitDistance, value => orbitDistance = value, 9.0f, 72.0f, "0.0");
                panel.Readout("Runtime", () => $"{timeSeconds:0.0}s");
                panel.Readout("Camera", () => $"{ZyphosCameraComposer.DisplayName(cameraFrame)} / {orbitDistance:0.0} wu / yaw {orbitYaw:0.00}");
                panel.Readout("Terrain DSL", () => ZyphosFractalTerrain.Summary);
                panel.Readout("Binary", () => $"Umbros {ZyphosUmbrosSystem.UmbrosAngularDiameterDegrees:0.0} deg / {ZyphosUmbrosSystem.SeparationInZyphosRadii:0.0} Rz");
                panel.Readout("Objects", () => "fractal height DSL, atmosphere, Umbros");
            })
            .Command("zyphos", _ => $"Zyphos: {ZyphosFractalTerrain.Summary}", "Report Zyphos demo status.")
            .Command("zyphos-fractal", _ => ZyphosFractalTerrain.DebugDump, "Dump the compiled Zyphos fractal terrain grammar.")
            .Command("zyphos-system", _ => $"Zyphos-Umbros: separation {ZyphosUmbrosSystem.SeparationInZyphosRadii:0.0} Zyphos radii, Umbros radius {ZyphosUmbrosSystem.UmbrosRadiusRatio:0.00} Zyphos, apparent diameter {ZyphosUmbrosSystem.UmbrosAngularDiameterDegrees:0.0} degrees.", "Report the modeled Zyphos/Umbros/star baseline.");
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
            orbitDistance = Math.Clamp(orbitDistance - input.WheelDelta * 1.4f, 8.0f, 84.0f);
        }

        if (input.IsKeyPressed(KeyCode.Digit1))
        {
            cameraFrame = ZyphosCameraFrame.Planet;
        }

        if (input.IsKeyPressed(KeyCode.Digit2))
        {
            cameraFrame = ZyphosCameraFrame.Umbros;
        }

        if (input.IsKeyPressed(KeyCode.Digit3))
        {
            cameraFrame = ZyphosCameraFrame.Binary;
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

    private ZyphosCameraShot CurrentShot()
    {
        return ZyphosCameraComposer.Compose(cameraFrame, orbitYaw, orbitPitch, orbitDistance, timeSeconds);
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
