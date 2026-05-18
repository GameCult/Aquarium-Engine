namespace Aquarium.Engine.Ui;

public sealed class AquariumUiDocument
{
    private readonly List<AquariumUiPanel> panels = [];
    private readonly List<AquariumConsoleCommand> commands = [];

    public static AquariumUiDocument Empty { get; } = new();

    public IReadOnlyList<AquariumUiPanel> Panels => panels;

    public IReadOnlyList<AquariumConsoleCommand> Commands => commands;

    public AquariumUiDocument Panel(string title, Action<AquariumUiPanelBuilder> compose)
    {
        return Panel(title, 18.0f, 82.0f, 360.0f, compose);
    }

    public AquariumUiDocument Panel(string title, float left, float top, float width, Action<AquariumUiPanelBuilder> compose)
    {
        return Panel(title, left, top, width, fadeWhenMouseDistant: false, compose);
    }

    public AquariumUiDocument Panel(string title, float left, float top, float width, bool fadeWhenMouseDistant, Action<AquariumUiPanelBuilder> compose)
    {
        var controls = new List<AquariumUiControl>();
        compose(new AquariumUiPanelBuilder(controls));
        panels.Add(new AquariumUiPanel(title, left, top, width, controls, fadeWhenMouseDistant));
        return this;
    }

    public AquariumUiDocument Command(string name, Func<IReadOnlyList<string>, string> execute, string description = "")
    {
        commands.Add(new AquariumConsoleCommand(name, execute, description));
        return this;
    }
}

public sealed record AquariumUiPanel(
    string Title,
    float Left,
    float Top,
    float Width,
    IReadOnlyList<AquariumUiControl> Controls,
    bool FadeWhenMouseDistant = false);

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

    public AquariumUiPanelBuilder Text(string label, Func<string> read, Action<string> write, string? tooltip = null, Func<bool>? isVisible = null)
    {
        controls.Add(new AquariumUiText(label, read, write, tooltip, isVisible));
        return this;
    }

    public AquariumUiPanelBuilder TextBox(string label, Func<string> read, Action<string> write, int lines = 3, bool acceptsReturn = true, Action? submit = null, bool monospace = false, bool alignBottom = false, string? tooltip = null, Func<bool>? isVisible = null)
    {
        controls.Add(new AquariumUiTextBox(label, read, write, Math.Max(1, lines), acceptsReturn, submit, monospace, alignBottom, tooltip, isVisible));
        return this;
    }

    public AquariumUiPanelBuilder Readout(string label, Func<string> read, string? tooltip = null, Func<bool>? isVisible = null)
    {
        controls.Add(new AquariumUiReadout(label, read, tooltip, isVisible));
        return this;
    }
}

public readonly record struct AquariumUiOption(int Value, string Label);

public sealed record AquariumConsoleCommand(string Name, Func<IReadOnlyList<string>, string> Execute, string Description = "");

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

public sealed record AquariumUiText(string Label, Func<string> Read, Action<string> Write, string? Tooltip = null, Func<bool>? IsVisible = null)
    : AquariumUiControl(Label, Tooltip, IsVisible);

public sealed record AquariumUiTextBox(string Label, Func<string> Read, Action<string> Write, int Lines = 3, bool AcceptsReturn = true, Action? Submit = null, bool Monospace = false, bool AlignBottom = false, string? Tooltip = null, Func<bool>? IsVisible = null)
    : AquariumUiControl(Label, Tooltip, IsVisible);

public sealed record AquariumUiReadout(string Label, Func<string> Read, string? Tooltip = null, Func<bool>? IsVisible = null)
    : AquariumUiControl(Label, Tooltip, IsVisible);
