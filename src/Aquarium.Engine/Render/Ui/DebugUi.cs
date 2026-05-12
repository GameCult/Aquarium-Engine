using System.Globalization;
using System.Numerics;
using Aquarium.Engine.Input;
using Aquarium.Engine.Ui;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Aquarium.Engine.Render.Ui;

internal sealed class DebugUi
{
    private const float DefaultPanelLeft = 18.0f;
    private const float DefaultPanelTop = 82.0f;
    private const float DefaultPanelWidth = 360.0f;
    private const float HeaderHeight = 42.0f;
    private const float RowHeight = 31.0f;
    private const float RowGap = 6.0f;
    private const float SectionHeight = 22.0f;
    private const float LabelWidth = 168.0f;
    private const float TrackWidth = 104.0f;
    private const float TrackHeight = 5.0f;
    private const float ValueTrackGap = 12.0f;
    private const float CloseButtonSize = 20.0f;
    private const float TooltipWidth = 268.0f;
    private const float TooltipHeight = 34.0f;
    private const float TabRowHeight = 30.0f;
    private const float TabButtonGap = 6.0f;

    private readonly List<DebugUiControl> controls = [];
    private readonly IReadOnlyList<string> tabs;
    private readonly Func<int> readActiveTab;
    private readonly Action<int> writeActiveTab;
    private readonly float panelLeft;
    private readonly float panelTop;
    private readonly float panelWidth;
    private int? activeSliderId;
    private int? activeControlId;
    private int? focusedTextId;
    private bool closeButtonHovered;
    private bool closeButtonActive;
    private Rect panelBounds;
    private Vector2 mousePosition;

    public DebugUi(string title)
        : this(title, DefaultPanelLeft, DefaultPanelTop, DefaultPanelWidth, [], () => 0, _ => { })
    {
    }

    public DebugUi(string title, float left, float top, float width)
        : this(title, left, top, width, [], () => 0, _ => { })
    {
    }

    public DebugUi(string title, float left, float top, float width, IReadOnlyList<string> tabs, Func<int> readActiveTab, Action<int> writeActiveTab)
    {
        Title = title;
        panelLeft = left;
        panelTop = top;
        panelWidth = width;
        this.tabs = tabs;
        this.readActiveTab = readActiveTab;
        this.writeActiveTab = writeActiveTab;
    }

    public string Title { get; }

    public bool IsVisible { get; set; } = true;

    public bool WantsMouse { get; private set; }

    public static DebugUi FromContract(AquariumUiPanel source)
    {
        var ui = new DebugUi(source.Title, source.Left, source.Top, source.Width);
        foreach (var control in source.Controls)
        {
            ui.AddContractControl(control);
        }

        return ui;
    }

    public DebugUi Panel(Action<DebugUiPanel> compose)
    {
        var panel = new DebugUiPanel(controls);
        compose(panel);
        return this;
    }

    public void AddContractControl(AquariumUiControl control)
    {
        switch (control)
        {
            case AquariumUiSection section:
                controls.Add(new SectionControl(section.Label, () => section.Visible));
                break;
            case AquariumUiButton button:
                controls.Add(new ButtonControl(button.Label, button.Action, button.Tooltip, () => button.Visible));
                break;
            case AquariumUiToggle toggle:
                controls.Add(new ToggleControl(toggle.Label, toggle.Read, toggle.Write, toggle.Tooltip, () => toggle.Visible));
                break;
            case AquariumUiFloatSlider slider:
                controls.Add(new FloatSliderControl(slider.Label, slider.Read, slider.Write, slider.Min, slider.Max, slider.Format, slider.Tooltip, () => slider.Visible));
                break;
            case AquariumUiIntSlider slider:
                controls.Add(new IntSliderControl(slider.Label, slider.Read, slider.Write, slider.Min, slider.Max, slider.Tooltip, () => slider.Visible));
                break;
            case AquariumUiOptions options:
                controls.Add(new OptionControl(
                    options.Label,
                    options.Read,
                    options.Write,
                    options.Options.Select(option => new DebugUiOption(option.Value, option.Label)).ToArray(),
                    options.Tooltip,
                    () => options.Visible));
                break;
            case AquariumUiText text:
                controls.Add(new TextControl(text.Label, text.Read, text.Write, text.Tooltip, () => text.Visible));
                break;
            case AquariumUiTextBox textBox:
                controls.Add(new TextBoxControl(textBox.Label, textBox.Read, textBox.Write, textBox.Lines, textBox.AcceptsReturn, textBox.Submit, textBox.Tooltip, () => textBox.Visible));
                break;
            case AquariumUiReadout readout:
                controls.Add(new ReadoutControl(readout.Label, readout.Read, readout.Tooltip, () => readout.Visible));
                break;
        }
    }

    public void Update(InputState input)
    {
        if (input.IsKeyPressed(KeyCode.DebugUiToggle))
        {
            IsVisible = !IsVisible;
        }

        if (!IsVisible)
        {
            WantsMouse = false;
            activeSliderId = null;
            focusedTextId = null;
            return;
        }

        Layout();
        mousePosition = input.MousePosition;
        WantsMouse = Contains(panelBounds, mousePosition);
        closeButtonHovered = Contains(CloseButtonBounds(), mousePosition);
        closeButtonActive = closeButtonHovered && input.LeftMouseDown;
        foreach (var control in controls.Where(control => control.IsVisible))
        {
            control.UpdateHover(mousePosition);
            control.IsActive = control.Id == activeControlId && input.LeftMouseDown;
        }

        if (!input.LeftMouseDown)
        {
            activeSliderId = null;
            activeControlId = null;
        }

        if (activeSliderId is { } sliderId)
        {
            if (controls.FirstOrDefault(control => control.IsVisible && control.Id == sliderId) is SliderControl activeSlider)
            {
                activeSlider.IsActive = true;
                activeSlider.Drag(mousePosition);
            }

            return;
        }

        if (!input.IsMousePressed(MouseButton.Left))
        {
            ApplyTextInput(input);
            return;
        }

        if (closeButtonHovered)
        {
            IsVisible = false;
            focusedTextId = null;
            return;
        }

        var tabIndex = HitTestTab(mousePosition);
        if (tabIndex >= 0)
        {
            writeActiveTab(tabIndex);
            focusedTextId = null;
            activeSliderId = null;
            activeControlId = null;
            return;
        }

        DebugUiControl? clickedControl = null;
        focusedTextId = null;
        for (var index = controls.Count - 1; index >= 0; index--)
        {
            var control = controls[index];
            if (!control.IsVisible)
            {
                continue;
            }

            if (!control.HitTest(mousePosition))
            {
                continue;
            }

            control.Click(mousePosition);
            activeControlId = control.Id;
            control.IsActive = true;
            if (control is ITextInputControl)
            {
                focusedTextId = control.Id;
            }

            if (control is SliderControl slider)
            {
                activeSliderId = slider.Id;
                slider.Drag(mousePosition);
            }

            clickedControl = control;
            break;
        }

        foreach (var control in controls.Where(control => control.IsVisible))
        {
            if (!ReferenceEquals(control, clickedControl))
            {
                control.DismissTransientState();
            }
        }

        ApplyTextInput(input);
    }

    private void ApplyTextInput(InputState input)
    {
        if (focusedTextId is not { } textId)
        {
            return;
        }

        if (controls.FirstOrDefault(control => control.IsVisible && control.Id == textId) is ITextInputControl textControl)
        {
            textControl.ApplyTextInput(input);
        }
    }

    public void Draw(
        ID2D1RenderTarget target,
        IDWriteTextFormat titleFormat,
        IDWriteTextFormat smallFormat,
        ID2D1SolidColorBrush panelBrush,
        ID2D1SolidColorBrush rowBrush,
        ID2D1SolidColorBrush hoverRowBrush,
        ID2D1SolidColorBrush activeRowBrush,
        ID2D1SolidColorBrush outlineBrush,
        ID2D1SolidColorBrush primaryBrush,
        ID2D1SolidColorBrush quietBrush,
        ID2D1SolidColorBrush accentBrush,
        ID2D1SolidColorBrush accentHoverBrush,
        ID2D1SolidColorBrush accentActiveBrush,
        ID2D1SolidColorBrush dimAccentBrush,
        ID2D1SolidColorBrush trackHoverBrush,
        ID2D1SolidColorBrush trackActiveBrush,
        int viewportWidth,
        int viewportHeight)
    {
        if (!IsVisible)
        {
            return;
        }

        Layout();

        target.FillRectangle(panelBounds, panelBrush);
        target.DrawRectangle(panelBounds, outlineBrush, 1.0f);
        target.DrawLine(
            new Vector2(panelBounds.Left, panelBounds.Top + HeaderHeight),
            new Vector2(panelBounds.Right, panelBounds.Top + HeaderHeight),
            outlineBrush,
            1.0f);
        if (tabs.Count > 0)
        {
            target.DrawLine(
                new Vector2(panelBounds.Left, panelBounds.Top + HeaderHeight + TabRowHeight),
                new Vector2(panelBounds.Right, panelBounds.Top + HeaderHeight + TabRowHeight),
                outlineBrush,
                1.0f);
        }

        target.DrawText(
            Title,
            titleFormat,
            RectFromEdges(panelBounds.Left + 12.0f, panelBounds.Top + 8.0f, panelBounds.Right - 42.0f, panelBounds.Top + HeaderHeight - 4.0f),
            primaryBrush,
            DrawTextOptions.Clip);

        var close = CloseButtonBounds();
        if (closeButtonHovered)
        {
            target.FillRectangle(close, closeButtonActive ? activeRowBrush : hoverRowBrush);
        }

        var closeBrush = closeButtonHovered ? accentBrush : quietBrush;
        target.DrawLine(
            new Vector2(close.Left + 6.0f, close.Top + 6.0f),
            new Vector2(close.Right - 6.0f, close.Bottom - 6.0f),
            closeBrush,
            closeButtonActive ? 2.0f : 1.25f);
        target.DrawLine(
            new Vector2(close.Right - 6.0f, close.Top + 6.0f),
            new Vector2(close.Left + 6.0f, close.Bottom - 6.0f),
            closeBrush,
            closeButtonActive ? 2.0f : 1.25f);

        DrawTabs(target, smallFormat, rowBrush, hoverRowBrush, activeRowBrush, outlineBrush, primaryBrush, quietBrush, accentBrush);

        foreach (var control in controls.Where(control => control.IsVisible))
        {
            control.IsFocused = control.Id == focusedTextId;
            control.Draw(
                target,
                smallFormat,
                rowBrush,
                hoverRowBrush,
                activeRowBrush,
                outlineBrush,
                primaryBrush,
                quietBrush,
                accentBrush,
                accentHoverBrush,
                accentActiveBrush,
                dimAccentBrush,
                trackHoverBrush,
                trackActiveBrush);
        }

        DrawTooltip(target, smallFormat, panelBrush, outlineBrush, primaryBrush, accentBrush, viewportWidth, viewportHeight);
    }

    private void Layout()
    {
        var y = panelTop + HeaderHeight + (tabs.Count > 0 ? TabRowHeight : 0.0f) + 10.0f;
        foreach (var control in controls.Where(control => control.IsVisible))
        {
            var height = control.LayoutHeight;
            control.Bounds = RectFromEdges(panelLeft + 10.0f, y, panelLeft + panelWidth - 10.0f, y + height);
            y += height + (control is SectionControl ? 2.0f : RowGap);
        }

        panelBounds = RectFromEdges(panelLeft, panelTop, panelLeft + panelWidth, y + 30.0f);
    }

    private Rect CloseButtonBounds()
    {
        return RectFromEdges(
            panelBounds.Right - CloseButtonSize - 10.0f,
            panelBounds.Top + 11.0f,
            panelBounds.Right - 10.0f,
            panelBounds.Top + 11.0f + CloseButtonSize);
    }

    private Rect TabBounds(int index)
    {
        if (tabs.Count == 0)
        {
            return RectFromEdges(0, 0, 0, 0);
        }

        var available = panelWidth - 20.0f - Math.Max(0, tabs.Count - 1) * TabButtonGap;
        var width = Math.Max(1.0f, available / tabs.Count);
        var left = panelLeft + 10.0f + index * (width + TabButtonGap);
        var top = panelTop + HeaderHeight + 4.0f;
        return RectFromEdges(left, top, left + width, top + TabRowHeight - 8.0f);
    }

    private int HitTestTab(Vector2 point)
    {
        for (var index = 0; index < tabs.Count; index++)
        {
            if (Contains(TabBounds(index), point))
            {
                return index;
            }
        }

        return -1;
    }

    private void DrawTabs(
        ID2D1RenderTarget target,
        IDWriteTextFormat format,
        ID2D1SolidColorBrush rowBrush,
        ID2D1SolidColorBrush hoverRowBrush,
        ID2D1SolidColorBrush activeRowBrush,
        ID2D1SolidColorBrush outlineBrush,
        ID2D1SolidColorBrush primaryBrush,
        ID2D1SolidColorBrush quietBrush,
        ID2D1SolidColorBrush accentBrush)
    {
        var activeTab = Math.Clamp(readActiveTab(), 0, Math.Max(0, tabs.Count - 1));
        for (var index = 0; index < tabs.Count; index++)
        {
            var bounds = TabBounds(index);
            var active = index == activeTab;
            var hovered = Contains(bounds, mousePosition);
            target.FillRectangle(bounds, active ? activeRowBrush : hovered ? hoverRowBrush : rowBrush);
            target.DrawRectangle(bounds, active ? accentBrush : outlineBrush, active ? 1.5f : 1.0f);
            if (active)
            {
                target.DrawLine(
                    new Vector2(bounds.Left + 5.0f, bounds.Bottom - 2.0f),
                    new Vector2(bounds.Right - 5.0f, bounds.Bottom - 2.0f),
                    accentBrush,
                    2.0f);
            }

            target.DrawText(
                tabs[index],
                format,
                RectFromEdges(bounds.Left + 7.0f, bounds.Top, bounds.Right - 7.0f, bounds.Bottom),
                active ? primaryBrush : quietBrush,
                DrawTextOptions.Clip);
        }
    }

    private void DrawTooltip(
        ID2D1RenderTarget target,
        IDWriteTextFormat format,
        ID2D1SolidColorBrush panelBrush,
        ID2D1SolidColorBrush outlineBrush,
        ID2D1SolidColorBrush primaryBrush,
        ID2D1SolidColorBrush accentBrush,
        int viewportWidth,
        int viewportHeight)
    {
        var tooltip = closeButtonHovered
            ? "Close panel. Press F2 to show it again."
            : controls.FirstOrDefault(control => control.IsVisible && control.IsHovered)?.Tooltip;
        if (string.IsNullOrWhiteSpace(tooltip))
        {
            return;
        }

        var left = mousePosition.X + 14.0f;
        var top = mousePosition.Y + 18.0f;
        if (left + TooltipWidth > viewportWidth - 10.0f)
        {
            left = mousePosition.X - TooltipWidth - 14.0f;
        }
        if (top + TooltipHeight > viewportHeight - 10.0f)
        {
            top = mousePosition.Y - TooltipHeight - 14.0f;
        }

        var bounds = RectFromEdges(left, top, left + TooltipWidth, top + TooltipHeight);
        target.FillRectangle(bounds, panelBrush);
        target.DrawRectangle(bounds, outlineBrush, 1.0f);
        target.DrawLine(
            new Vector2(bounds.Left, bounds.Top),
            new Vector2(bounds.Right, bounds.Top),
            accentBrush,
            1.0f);
        target.DrawText(
            tooltip,
            format,
            RectFromEdges(bounds.Left + 8.0f, bounds.Top, bounds.Right - 8.0f, bounds.Bottom),
            primaryBrush,
            DrawTextOptions.Clip);
    }

    private static bool Contains(Rect bounds, Vector2 point)
    {
        return point.X >= bounds.Left
            && point.X <= bounds.Right
            && point.Y >= bounds.Top
            && point.Y <= bounds.Bottom;
    }

    private static Rect RectFromEdges(float left, float top, float right, float bottom)
    {
        return new Rect(left, top, Math.Max(0.0f, right - left), Math.Max(0.0f, bottom - top));
    }

    public sealed class DebugUiPanel
    {
        private readonly List<DebugUiControl> controls;

        internal DebugUiPanel(List<DebugUiControl> controls)
        {
            this.controls = controls;
        }

        public DebugUiPanel Section(string title, Func<bool>? isVisible = null)
        {
            controls.Add(new SectionControl(title, isVisible));
            return this;
        }

        public DebugUiPanel Button(string label, Action action, string? tooltip = null, Func<bool>? isVisible = null)
        {
            controls.Add(new ButtonControl(label, action, tooltip, isVisible));
            return this;
        }

        public DebugUiPanel Toggle(string label, Func<bool> read, Action<bool> write, string? tooltip = null, Func<bool>? isVisible = null)
        {
            controls.Add(new ToggleControl(label, read, write, tooltip, isVisible));
            return this;
        }

        public DebugUiPanel Slider(string label, Func<float> read, Action<float> write, float min, float max, string format = "0.###", string? tooltip = null, Func<bool>? isVisible = null)
        {
            controls.Add(new FloatSliderControl(label, read, write, min, max, format, tooltip, isVisible));
            return this;
        }

        public DebugUiPanel Slider(string label, Func<int> read, Action<int> write, int min, int max, string? tooltip = null, Func<bool>? isVisible = null)
        {
            controls.Add(new IntSliderControl(label, read, write, min, max, tooltip, isVisible));
            return this;
        }

        public DebugUiPanel Options(string label, Func<int> read, Action<int> write, IReadOnlyList<DebugUiOption> options, string? tooltip = null, Func<bool>? isVisible = null)
        {
            controls.Add(new OptionControl(label, read, write, options, tooltip, isVisible));
            return this;
        }

        public DebugUiPanel Text(string label, Func<string> read, Action<string> write, string? tooltip = null, Func<bool>? isVisible = null)
        {
            controls.Add(new TextControl(label, read, write, tooltip, isVisible));
            return this;
        }

        public DebugUiPanel TextBox(string label, Func<string> read, Action<string> write, int lines = 3, bool acceptsReturn = true, Action? submit = null, string? tooltip = null, Func<bool>? isVisible = null)
        {
            controls.Add(new TextBoxControl(label, read, write, Math.Max(1, lines), acceptsReturn, submit, tooltip, isVisible));
            return this;
        }

        public DebugUiPanel Readout(string label, Func<string> read, string? tooltip = null, Func<bool>? isVisible = null)
        {
            controls.Add(new ReadoutControl(label, read, tooltip, isVisible));
            return this;
        }
    }

    public readonly record struct DebugUiOption(int Value, string Label);

    private interface ITextInputControl
    {
        void ApplyTextInput(InputState input);
    }

    internal abstract class DebugUiControl(string label, string? tooltip = null, Func<bool>? isVisible = null)
    {
        private static int nextId;

        public int Id { get; } = nextId++;

        protected string Label { get; } = label;

        public string? Tooltip { get; } = tooltip;

        public bool IsVisible => isVisible?.Invoke() ?? true;

        public Rect Bounds { get; set; }

        public bool IsHovered { get; set; }

        public bool IsActive { get; set; }

        public bool IsFocused { get; set; }

        public virtual bool IsInteractive => true;

        public virtual float LayoutHeight => this is SectionControl ? SectionHeight : RowHeight;

        public virtual bool HitTest(Vector2 mouse) => Contains(Bounds, mouse);

        public virtual void UpdateHover(Vector2 mouse)
        {
            IsHovered = IsInteractive && HitTest(mouse);
        }

        public virtual void Click(Vector2 mouse)
        {
        }

        public virtual void DismissTransientState()
        {
        }

        public abstract void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush activeRowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush accentHoverBrush,
            ID2D1SolidColorBrush accentActiveBrush,
            ID2D1SolidColorBrush dimAccentBrush,
            ID2D1SolidColorBrush trackHoverBrush,
            ID2D1SolidColorBrush trackActiveBrush);

        protected void DrawRow(
            ID2D1RenderTarget target,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush activeRowBrush,
            ID2D1SolidColorBrush outlineBrush)
        {
            target.FillRectangle(Bounds, IsActive ? activeRowBrush : IsHovered ? hoverRowBrush : rowBrush);
            target.DrawLine(
                new Vector2(Bounds.Left, Bounds.Bottom),
                new Vector2(Bounds.Right, Bounds.Bottom),
                outlineBrush,
                1.0f);
        }

        protected Rect LabelBounds()
        {
            return RectFromEdges(Bounds.Left + 8.0f, Bounds.Top, Bounds.Left + LabelWidth, Bounds.Bottom);
        }

        protected Rect ValueBounds()
        {
            return RectFromEdges(Bounds.Left + LabelWidth + 10.0f, Bounds.Top, Bounds.Right - TrackWidth - ValueTrackGap, Bounds.Bottom);
        }
    }

    private sealed class SectionControl(string label, Func<bool>? isVisible) : DebugUiControl(label, isVisible: isVisible)
    {
        public override bool IsInteractive => false;

        public override bool HitTest(Vector2 mouse) => false;

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush activeRowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush accentHoverBrush,
            ID2D1SolidColorBrush accentActiveBrush,
            ID2D1SolidColorBrush dimAccentBrush,
            ID2D1SolidColorBrush trackHoverBrush,
            ID2D1SolidColorBrush trackActiveBrush)
        {
            target.DrawText(Label.ToUpperInvariant(), format, Bounds, accentBrush, DrawTextOptions.Clip);
            target.DrawLine(
                new Vector2(Bounds.Left + 118.0f, Bounds.Top + 11.0f),
                new Vector2(Bounds.Right, Bounds.Top + 11.0f),
                outlineBrush,
                1.0f);
        }
    }

    private sealed class ButtonControl(string label, Action action, string? tooltip, Func<bool>? isVisible) : DebugUiControl(label, tooltip, isVisible)
    {
        public override void Click(Vector2 mouse) => action();

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush activeRowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush accentHoverBrush,
            ID2D1SolidColorBrush accentActiveBrush,
            ID2D1SolidColorBrush dimAccentBrush,
            ID2D1SolidColorBrush trackHoverBrush,
            ID2D1SolidColorBrush trackActiveBrush)
        {
            DrawRow(target, rowBrush, hoverRowBrush, activeRowBrush, outlineBrush);
            target.DrawText(Label, format, LabelBounds(), accentBrush, DrawTextOptions.Clip);
            target.DrawText("apply", format, ValueBounds(), primaryBrush, DrawTextOptions.Clip);
        }
    }

    private sealed class ToggleControl(string label, Func<bool> read, Action<bool> write, string? tooltip, Func<bool>? isVisible) : DebugUiControl(label, tooltip, isVisible)
    {
        public override void Click(Vector2 mouse) => write(!read());

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush activeRowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush accentHoverBrush,
            ID2D1SolidColorBrush accentActiveBrush,
            ID2D1SolidColorBrush dimAccentBrush,
            ID2D1SolidColorBrush trackHoverBrush,
            ID2D1SolidColorBrush trackActiveBrush)
        {
            DrawRow(target, rowBrush, hoverRowBrush, activeRowBrush, outlineBrush);
            target.DrawText(Label.ToUpperInvariant(), format, LabelBounds(), accentBrush, DrawTextOptions.Clip);

            var box = RectFromEdges(Bounds.Right - 28.0f, Bounds.Top + 8.0f, Bounds.Right - 13.0f, Bounds.Bottom - 8.0f);
            target.DrawRectangle(box, read() ? accentBrush : outlineBrush, 1.0f);
            if (read())
            {
                target.DrawLine(new Vector2(box.Left + 3.0f, box.Top + 8.0f), new Vector2(box.Left + 7.0f, box.Bottom - 3.0f), accentBrush, 2.0f);
                target.DrawLine(new Vector2(box.Left + 7.0f, box.Bottom - 3.0f), new Vector2(box.Right - 2.0f, box.Top + 3.0f), accentBrush, 2.0f);
            }
        }
    }

    private sealed class OptionControl(
        string label,
        Func<int> read,
        Action<int> write,
        IReadOnlyList<DebugUiOption> options,
        string? tooltip,
        Func<bool>? isVisible) : DebugUiControl(label, tooltip, isVisible)
    {
        private bool isOpen;
        private int hoveredOptionIndex = -1;

        public override float LayoutHeight => RowHeight + (isOpen ? options.Count * RowHeight : 0.0f);

        public override void UpdateHover(Vector2 mouse)
        {
            hoveredOptionIndex = -1;
            IsHovered = HitTest(mouse);
            if (!isOpen || !IsHovered || Contains(MainBounds(), mouse))
            {
                return;
            }

            for (var index = 0; index < options.Count; index++)
            {
                if (Contains(OptionBounds(index), mouse))
                {
                    hoveredOptionIndex = index;
                    return;
                }
            }
        }

        public override void Click(Vector2 mouse)
        {
            if (!isOpen)
            {
                isOpen = true;
                return;
            }

            if (Contains(MainBounds(), mouse))
            {
                isOpen = false;
                return;
            }

            if (hoveredOptionIndex >= 0 && hoveredOptionIndex < options.Count)
            {
                write(options[hoveredOptionIndex].Value);
                isOpen = false;
            }
        }

        public override void DismissTransientState()
        {
            isOpen = false;
        }

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush activeRowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush accentHoverBrush,
            ID2D1SolidColorBrush accentActiveBrush,
            ID2D1SolidColorBrush dimAccentBrush,
            ID2D1SolidColorBrush trackHoverBrush,
            ID2D1SolidColorBrush trackActiveBrush)
        {
            var main = MainBounds();
            target.FillRectangle(main, IsActive ? activeRowBrush : IsHovered ? hoverRowBrush : rowBrush);
            target.DrawLine(new Vector2(main.Left, main.Bottom), new Vector2(main.Right, main.Bottom), outlineBrush, 1.0f);
            target.DrawText(Label.ToUpperInvariant(), format, RectFromEdges(main.Left + 8.0f, main.Top, main.Left + LabelWidth, main.Bottom), accentBrush, DrawTextOptions.Clip);
            target.DrawText(SelectedLabel(), format, RectFromEdges(main.Left + LabelWidth + 10.0f, main.Top, main.Right - 32.0f, main.Bottom), primaryBrush, DrawTextOptions.Clip);

            var arrowBrush = IsActive ? accentActiveBrush : IsHovered ? accentHoverBrush : accentBrush;
            var cy = (main.Top + main.Bottom) * 0.5f;
            target.DrawLine(new Vector2(main.Right - 24.0f, cy - 3.0f), new Vector2(main.Right - 18.0f, cy + 3.0f), arrowBrush, 1.5f);
            target.DrawLine(new Vector2(main.Right - 18.0f, cy + 3.0f), new Vector2(main.Right - 12.0f, cy - 3.0f), arrowBrush, 1.5f);

            if (!isOpen)
            {
                return;
            }

            for (var index = 0; index < options.Count; index++)
            {
                var bounds = OptionBounds(index);
                var selected = options[index].Value == read();
                var hovered = index == hoveredOptionIndex;
                target.FillRectangle(bounds, hovered ? hoverRowBrush : rowBrush);
                target.DrawLine(new Vector2(bounds.Left, bounds.Bottom), new Vector2(bounds.Right, bounds.Bottom), outlineBrush, 1.0f);
                var labelBrush = selected ? accentBrush : primaryBrush;
                target.DrawText(options[index].Label, format, RectFromEdges(bounds.Left + 18.0f, bounds.Top, bounds.Right - 12.0f, bounds.Bottom), labelBrush, DrawTextOptions.Clip);
                if (selected)
                {
                    target.DrawLine(new Vector2(bounds.Left + 7.0f, bounds.Top + 16.0f), new Vector2(bounds.Left + 10.0f, bounds.Top + 20.0f), accentBrush, 1.75f);
                    target.DrawLine(new Vector2(bounds.Left + 10.0f, bounds.Top + 20.0f), new Vector2(bounds.Left + 15.0f, bounds.Top + 10.0f), accentBrush, 1.75f);
                }
            }
        }

        private string SelectedLabel()
        {
            var value = read();
            foreach (var option in options)
            {
                if (option.Value == value)
                {
                    return option.Label;
                }
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        private Rect MainBounds()
        {
            return RectFromEdges(Bounds.Left, Bounds.Top, Bounds.Right, Bounds.Top + RowHeight);
        }

        private Rect OptionBounds(int index)
        {
            var top = Bounds.Top + RowHeight + index * RowHeight;
            return RectFromEdges(Bounds.Left + 8.0f, top, Bounds.Right, top + RowHeight);
        }
    }

    private sealed class TextControl(string label, Func<string> read, Action<string> write, string? tooltip, Func<bool>? isVisible)
        : DebugUiControl(label, tooltip, isVisible), ITextInputControl
    {
        public override float LayoutHeight => RowHeight * 2.0f;

        public void ApplyTextInput(InputState input)
        {
            var value = read();
            foreach (var ch in input.TextInput)
            {
                switch (ch)
                {
                    case '\b':
                        if (value.Length > 0)
                        {
                            value = value[..^1];
                        }
                        break;
                    case '\r':
                    case '\n':
                        value += ";";
                        break;
                    default:
                        if (!char.IsControl(ch))
                        {
                            value += ch;
                        }
                        break;
                }
            }

            if (value != read())
            {
                write(value);
            }
        }

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush activeRowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush accentHoverBrush,
            ID2D1SolidColorBrush accentActiveBrush,
            ID2D1SolidColorBrush dimAccentBrush,
            ID2D1SolidColorBrush trackHoverBrush,
            ID2D1SolidColorBrush trackActiveBrush)
        {
            DrawRow(target, rowBrush, hoverRowBrush, activeRowBrush, outlineBrush);
            target.DrawText(Label.ToUpperInvariant(), format, RectFromEdges(Bounds.Left + 8.0f, Bounds.Top, Bounds.Right - 8.0f, Bounds.Top + RowHeight), accentBrush, DrawTextOptions.Clip);
            var display = read();
            if (display.Length > 92)
            {
                display = "..." + display[^89..];
            }

            target.DrawText(display, format, RectFromEdges(Bounds.Left + 8.0f, Bounds.Top + RowHeight - 4.0f, Bounds.Right - 8.0f, Bounds.Bottom), primaryBrush, DrawTextOptions.Clip);
        }
    }

    private sealed class TextBoxControl(string label, Func<string> read, Action<string> write, int lines, bool acceptsReturn, Action? submit, string? tooltip, Func<bool>? isVisible)
        : DebugUiControl(label, tooltip, isVisible), ITextInputControl
    {
        private const float LabelHeight = 20.0f;
        private const float TextLineHeight = 18.0f;
        private const float VerticalPadding = 9.0f;

        public override float LayoutHeight => LabelHeight + VerticalPadding * 2.0f + Math.Max(1, lines) * TextLineHeight;

        public void ApplyTextInput(InputState input)
        {
            var value = read();
            foreach (var ch in input.TextInput)
            {
                switch (ch)
                {
                    case '\b':
                        if (value.Length > 0)
                        {
                            value = value[..^1];
                        }
                        break;
                    case '\r':
                    case '\n':
                        if (acceptsReturn)
                        {
                            value += "\n";
                        }
                        else
                        {
                            submit?.Invoke();
                            value = read();
                        }
                        break;
                    default:
                        if (!char.IsControl(ch))
                        {
                            value += ch;
                        }
                        break;
                }
            }

            if (value != read())
            {
                write(value);
            }
        }

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush activeRowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush accentHoverBrush,
            ID2D1SolidColorBrush accentActiveBrush,
            ID2D1SolidColorBrush dimAccentBrush,
            ID2D1SolidColorBrush trackHoverBrush,
            ID2D1SolidColorBrush trackActiveBrush)
        {
            var labelBounds = RectFromEdges(Bounds.Left + 8.0f, Bounds.Top, Bounds.Right - 8.0f, Bounds.Top + LabelHeight);
            var box = RectFromEdges(Bounds.Left, Bounds.Top + LabelHeight, Bounds.Right, Bounds.Bottom);
            target.DrawText(Label.ToUpperInvariant(), format, labelBounds, accentBrush, DrawTextOptions.Clip);
            var focused = IsFocused || IsActive;
            target.FillRectangle(box, IsActive ? activeRowBrush : IsHovered ? hoverRowBrush : rowBrush);
            target.DrawRectangle(box, focused ? accentBrush : outlineBrush, focused ? 1.5f : 1.0f);

            target.DrawText(
                DisplayText(),
                format,
                RectFromEdges(box.Left + 8.0f, box.Top + 6.0f, box.Right - 8.0f, box.Bottom - 6.0f),
                primaryBrush,
                DrawTextOptions.Clip);

            if (focused)
            {
                var caretX = box.Right - 12.0f;
                var caretBottom = box.Bottom - 8.0f;
                target.DrawLine(new Vector2(caretX, caretBottom - TextLineHeight + 2.0f), new Vector2(caretX, caretBottom), accentBrush, 1.25f);
            }
        }

        private string DisplayText()
        {
            var value = read().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var split = value.Split('\n');
            if (split.Length <= lines)
            {
                return value;
            }

            return string.Join('\n', split.Skip(split.Length - lines));
        }
    }

    private sealed class ReadoutControl(string label, Func<string> read, string? tooltip, Func<bool>? isVisible)
        : DebugUiControl(label, tooltip, isVisible)
    {
        public override bool IsInteractive => !string.IsNullOrWhiteSpace(Tooltip);

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush activeRowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush accentHoverBrush,
            ID2D1SolidColorBrush accentActiveBrush,
            ID2D1SolidColorBrush dimAccentBrush,
            ID2D1SolidColorBrush trackHoverBrush,
            ID2D1SolidColorBrush trackActiveBrush)
        {
            DrawRow(target, rowBrush, hoverRowBrush, activeRowBrush, outlineBrush);
            target.DrawText(Label.ToUpperInvariant(), format, LabelBounds(), accentBrush, DrawTextOptions.Clip);
            target.DrawText(read(), format, RectFromEdges(Bounds.Left + LabelWidth + 10.0f, Bounds.Top, Bounds.Right - 8.0f, Bounds.Bottom), primaryBrush, DrawTextOptions.Clip);
        }
    }

    private abstract class SliderControl(string label, string? tooltip, Func<bool>? isVisible) : DebugUiControl(label, tooltip, isVisible)
    {
        private const float ThumbRadius = 4.0f;
        private const float ThumbHitRadius = 8.0f;

        private bool trackHovered;
        private bool thumbHovered;
        private bool trackActive;
        private bool thumbActive;

        public override bool HitTest(Vector2 mouse)
        {
            return Contains(SliderInteractionBounds(), mouse);
        }

        public override void UpdateHover(Vector2 mouse)
        {
            thumbHovered = Contains(ThumbInteractionBounds(), mouse);
            trackHovered = !thumbHovered && Contains(TrackInteractionBounds(), mouse);
            IsHovered = trackHovered || thumbHovered;
        }

        public void Drag(Vector2 mouse)
        {
            var track = TrackBounds();
            var normalized = Math.Clamp((mouse.X - track.Left) / Math.Max(1.0f, track.Right - track.Left), 0.0f, 1.0f);
            WriteNormalized(normalized);
        }

        public override void Click(Vector2 mouse)
        {
            thumbActive = Contains(ThumbInteractionBounds(), mouse);
            trackActive = !thumbActive && Contains(TrackInteractionBounds(), mouse);
            Drag(mouse);
        }

        protected abstract float ReadNormalized();

        protected abstract void WriteNormalized(float value);

        protected abstract string ReadDisplay();

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush activeRowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush accentHoverBrush,
            ID2D1SolidColorBrush accentActiveBrush,
            ID2D1SolidColorBrush dimAccentBrush,
            ID2D1SolidColorBrush trackHoverBrush,
            ID2D1SolidColorBrush trackActiveBrush)
        {
            DrawRow(target, rowBrush, rowBrush, rowBrush, outlineBrush);
            target.DrawText(Label.ToUpperInvariant(), format, LabelBounds(), accentBrush, DrawTextOptions.Clip);
            target.DrawText(ReadDisplay(), format, ValueBounds(), primaryBrush, DrawTextOptions.Clip);

            var track = TrackBounds();
            var t = ReadNormalized();
            var fill = RectFromEdges(track.Left, track.Top, track.Left + (track.Right - track.Left) * t, track.Bottom);
            var isTrackActive = IsActive && trackActive;
            var isThumbActive = IsActive && thumbActive;
            var isTrackLit = trackHovered || isTrackActive;
            var trackBrush = isTrackActive ? trackActiveBrush : isTrackLit ? trackHoverBrush : dimAccentBrush;
            var fillBrush = isTrackActive ? accentActiveBrush : isTrackLit ? accentHoverBrush : accentBrush;
            target.FillRectangle(track, trackBrush);
            target.FillRectangle(fill, fillBrush);
            var thumbX = fill.Right;
            var thumbBrush = isThumbActive ? accentActiveBrush : thumbHovered ? accentHoverBrush : accentBrush;
            target.FillRectangle(RectFromEdges(thumbX - ThumbRadius, track.Top - ThumbRadius, thumbX + ThumbRadius, track.Bottom + ThumbRadius), thumbBrush);
        }

        private Rect TrackBounds()
        {
            var right = Bounds.Right - 12.0f;
            var left = right - TrackWidth;
            var centerY = (Bounds.Top + Bounds.Bottom) * 0.5f;
            return RectFromEdges(left, centerY - TrackHeight * 0.5f, right, centerY + TrackHeight * 0.5f);
        }

        private Rect SliderInteractionBounds()
        {
            var track = TrackBounds();
            return RectFromEdges(track.Left - 8.0f, Bounds.Top, track.Right + 8.0f, Bounds.Bottom);
        }

        private Rect TrackInteractionBounds()
        {
            var track = TrackBounds();
            return RectFromEdges(track.Left - 4.0f, Bounds.Top, track.Right + 4.0f, Bounds.Bottom);
        }

        private Rect ThumbInteractionBounds()
        {
            var track = TrackBounds();
            var thumbX = track.Left + (track.Right - track.Left) * ReadNormalized();
            var centerY = (track.Top + track.Bottom) * 0.5f;
            return RectFromEdges(thumbX - ThumbHitRadius, centerY - ThumbHitRadius, thumbX + ThumbHitRadius, centerY + ThumbHitRadius);
        }
    }

    private sealed class FloatSliderControl(
        string label,
        Func<float> read,
        Action<float> write,
        float min,
        float max,
        string format,
        string? tooltip,
        Func<bool>? isVisible) : SliderControl(label, tooltip, isVisible)
    {
        protected override float ReadNormalized()
        {
            return Math.Clamp((read() - min) / Math.Max(0.000001f, max - min), 0.0f, 1.0f);
        }

        protected override void WriteNormalized(float value)
        {
            write(min + (max - min) * value);
        }

        protected override string ReadDisplay()
        {
            return read().ToString(format, CultureInfo.InvariantCulture);
        }
    }

    private sealed class IntSliderControl(
        string label,
        Func<int> read,
        Action<int> write,
        int min,
        int max,
        string? tooltip,
        Func<bool>? isVisible) : SliderControl(label, tooltip, isVisible)
    {
        protected override float ReadNormalized()
        {
            return Math.Clamp((read() - min) / Math.Max(1.0f, max - min), 0.0f, 1.0f);
        }

        protected override void WriteNormalized(float value)
        {
            write((int)MathF.Round(min + (max - min) * value));
        }

        protected override string ReadDisplay()
        {
            return read().ToString(CultureInfo.InvariantCulture);
        }
    }
}
