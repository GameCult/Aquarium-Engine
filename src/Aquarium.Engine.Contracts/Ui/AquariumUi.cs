namespace Aquarium.Engine.Ui;

public sealed class AquariumUiDocument
{
    private readonly List<AquariumUiPanel> panels = [];

    public static AquariumUiDocument Empty { get; } = new();

    public IReadOnlyList<AquariumUiPanel> Panels => panels;

    public AquariumUiDocument Panel(string title, Action<AquariumUiPanelBuilder> compose)
    {
        return Panel(title, 18.0f, 82.0f, 360.0f, compose);
    }

    public AquariumUiDocument Panel(string title, float left, float top, float width, Action<AquariumUiPanelBuilder> compose)
    {
        var controls = new List<AquariumUiControl>();
        compose(new AquariumUiPanelBuilder(controls));
        panels.Add(new AquariumUiPanel(title, left, top, width, controls));
        return this;
    }
}

public sealed record AquariumUiPanel(
    string Title,
    float Left,
    float Top,
    float Width,
    IReadOnlyList<AquariumUiControl> Controls);

public sealed class AquariumUiPanelBuilder(List<AquariumUiControl> controls)
{
    public AquariumUiPanelBuilder Section(string title, Func<bool>? isVisible = null)
    {
        controls.Add(new AquariumUiSection(title, isVisible));
        return this;
    }

    public AquariumUiPanelBuilder Button(string label, Action action, string? tooltip = null, Func<bool>? isVisible = null)
    {
        controls.Add(new AquariumUiButton(label, action, tooltip, isVisible));
        return this;
    }

    public AquariumUiPanelBuilder Toggle(string label, Func<bool> read, Action<bool> write, string? tooltip = null, Func<bool>? isVisible = null)
    {
        controls.Add(new AquariumUiToggle(label, read, write, tooltip, isVisible));
        return this;
    }

    public AquariumUiPanelBuilder Slider(string label, Func<float> read, Action<float> write, float min, float max, string format = "0.###", string? tooltip = null, Func<bool>? isVisible = null)
    {
        controls.Add(new AquariumUiFloatSlider(label, read, write, min, max, format, tooltip, isVisible));
        return this;
    }

    public AquariumUiPanelBuilder Slider(string label, Func<int> read, Action<int> write, int min, int max, string? tooltip = null, Func<bool>? isVisible = null)
    {
        controls.Add(new AquariumUiIntSlider(label, read, write, min, max, tooltip, isVisible));
        return this;
    }

    public AquariumUiPanelBuilder Options(string label, Func<int> read, Action<int> write, IReadOnlyList<AquariumUiOption> options, string? tooltip = null, Func<bool>? isVisible = null)
    {
        controls.Add(new AquariumUiOptions(label, read, write, options, tooltip, isVisible));
        return this;
    }
}

public readonly record struct AquariumUiOption(int Value, string Label);

public abstract record AquariumUiControl(string Label, string? Tooltip, Func<bool>? IsVisible)
{
    public bool Visible => IsVisible?.Invoke() ?? true;
}

public sealed record AquariumUiSection(string Label, Func<bool>? IsVisible = null)
    : AquariumUiControl(Label, null, IsVisible);

public sealed record AquariumUiButton(string Label, Action Action, string? Tooltip = null, Func<bool>? IsVisible = null)
    : AquariumUiControl(Label, Tooltip, IsVisible);

public sealed record AquariumUiToggle(string Label, Func<bool> Read, Action<bool> Write, string? Tooltip = null, Func<bool>? IsVisible = null)
    : AquariumUiControl(Label, Tooltip, IsVisible);

public sealed record AquariumUiFloatSlider(string Label, Func<float> Read, Action<float> Write, float Min, float Max, string Format = "0.###", string? Tooltip = null, Func<bool>? IsVisible = null)
    : AquariumUiControl(Label, Tooltip, IsVisible);

public sealed record AquariumUiIntSlider(string Label, Func<int> Read, Action<int> Write, int Min, int Max, string? Tooltip = null, Func<bool>? IsVisible = null)
    : AquariumUiControl(Label, Tooltip, IsVisible);

public sealed record AquariumUiOptions(string Label, Func<int> Read, Action<int> Write, IReadOnlyList<AquariumUiOption> Options, string? Tooltip = null, Func<bool>? IsVisible = null)
    : AquariumUiControl(Label, Tooltip, IsVisible);
