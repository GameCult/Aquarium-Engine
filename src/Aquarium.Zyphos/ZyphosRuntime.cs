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
    private ZyphosSpatialDomainKey selectedDomainKey = ZyphosSpatialDomainCatalog.Planet;
    private readonly Dictionary<ZyphosSpatialDomainKey, bool> expandedDomains = new()
    {
        [ZyphosSpatialDomainCatalog.Solar] = true,
        [ZyphosSpatialDomainCatalog.Orbital] = true,
        [ZyphosSpatialDomainCatalog.Planet] = true,
        [ZyphosSpatialDomainCatalog.PlanetLatLong] = true,
    };

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
                panel.Slider("Orbit Distance", () => orbitDistance, value => orbitDistance = value, 9.0f, 72.0f, "0.0");
                panel.Readout("Runtime", () => $"{timeSeconds:0.0}s");
                panel.Readout("Camera", () => $"{ZyphosCameraComposer.DisplayName(selectedDomainKey)} / {orbitDistance:0.0} wu / yaw {orbitYaw:0.00}");
                panel.Readout("Terrain DSL", () => ZyphosFractalTerrain.Summary);
                panel.Readout("Binary", () => $"Umbros {ZyphosUmbrosSystem.UmbrosAngularDiameterDegrees:0.0} deg / {ZyphosUmbrosSystem.SeparationInZyphosRadii:0.0} Rz");
                panel.Readout("Objects", () => "fractal height DSL, atmosphere, Umbros");
            })
            .Panel("Navigation", -18.0f, 82.0f, 316.0f, fadeWhenMouseDistant: true, panel =>
            {
                panel.Section("Camera");
                panel.Toggle("Auto Orbit", () => autoOrbit, value => autoOrbit = value);
                panel.Readout("Mode", () => ZyphosCameraComposer.DisplayName(selectedDomainKey));
                panel.Readout("Pivot", CurrentPivotLabel);
                panel.Readout("Mouse", () => "drag orbit / wheel zoom");
                panel.Section("Spatial Domains");
                foreach (var domain in ZyphosSpatialDomainCatalog.Domains)
                {
                    var captured = domain;
                    panel.TreeItem(
                        captured.Label,
                        ZyphosSpatialDomainCatalog.DepthOf(captured),
                        ZyphosSpatialDomainCatalog.ChildrenOf(captured.Key).Count > 0,
                        () => IsExpanded(captured.Key),
                        value => SetExpanded(captured.Key, value),
                        () => selectedDomainKey == captured.Key,
                        () => SelectDomain(captured.Key),
                        captured.Detail,
                        $"{captured.Kind} domain `{captured.Key}`.",
                        () => IsDomainVisible(captured));
                }
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

        if (input.LeftMouseDown && input.MouseDelta != Vector2.Zero)
        {
            autoOrbit = false;
            orbitYaw -= input.MouseDelta.X * 0.0065f;
            orbitPitch += input.MouseDelta.Y * 0.0045f;
        }

        if (input.RightMouseDown && input.MouseDelta != Vector2.Zero)
        {
            autoOrbit = false;
            orbitYaw -= input.MouseDelta.X * 0.0025f;
            orbitDistance = Math.Clamp(orbitDistance + input.MouseDelta.Y * 0.055f, 8.0f, 84.0f);
        }

        if (input.IsKeyPressed(KeyCode.Digit1))
        {
            SelectDomain(ZyphosSpatialDomainCatalog.Planet);
        }

        if (input.IsKeyPressed(KeyCode.Digit2))
        {
            SelectDomain(ZyphosSpatialDomainCatalog.Umbros);
        }

        if (input.IsKeyPressed(KeyCode.Digit3))
        {
            SelectDomain(ZyphosSpatialDomainCatalog.Orbital);
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
        return ZyphosCameraComposer.Compose(selectedDomainKey, orbitYaw, orbitPitch, orbitDistance, timeSeconds);
    }

    private void SelectDomain(ZyphosSpatialDomainKey key)
    {
        selectedDomainKey = key;
        var domain = ZyphosSpatialDomainCatalog.GetRequired(key);
        orbitDistance = Math.Clamp(orbitDistance, 8.0f, MathF.Max(84.0f, domain.NavigationRadius * 2.0f));
        while (ZyphosSpatialDomainCatalog.ParentOf(domain) is { } parent)
        {
            expandedDomains[parent.Key] = true;
            domain = parent;
        }
    }

    private string CurrentPivotLabel()
    {
        var domain = ZyphosSpatialDomainCatalog.GetRequired(selectedDomainKey);
        var parent = ZyphosSpatialDomainCatalog.ParentOf(domain);
        return parent is null ? domain.Label : parent.Label;
    }

    private bool IsExpanded(ZyphosSpatialDomainKey key)
    {
        return expandedDomains.TryGetValue(key, out var expanded) && expanded;
    }

    private void SetExpanded(ZyphosSpatialDomainKey key, bool expanded)
    {
        expandedDomains[key] = expanded;
    }

    private bool IsDomainVisible(ZyphosSpatialDomain domain)
    {
        while (ZyphosSpatialDomainCatalog.ParentOf(domain) is { } parent)
        {
            if (!IsExpanded(parent.Key))
            {
                return false;
            }

            domain = parent;
        }

        return true;
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
