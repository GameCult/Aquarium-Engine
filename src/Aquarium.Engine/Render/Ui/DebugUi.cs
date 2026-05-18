using System.Globalization;
using System.Numerics;
using Aquarium.Engine.Input;
using Aquarium.Engine.Ui;
using SharpGen.Runtime;
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
    private const float ScreenMargin = 10.0f;

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
    private int lastViewportHeight = 720;
    private float bodyScrollOffset;
    private float bodyContentHeight;
    private Rect bodyClipBounds;

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

    public bool WantsKeyboard { get; private set; }

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
                controls.Add(new TextBoxControl(textBox.Label, textBox.Read, textBox.Write, textBox.Lines, textBox.AcceptsReturn, textBox.Submit, textBox.Monospace, textBox.AlignBottom, textBox.Tooltip, () => textBox.Visible));
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
            WantsKeyboard = false;
            activeSliderId = null;
            focusedTextId = null;
            bodyScrollOffset = 0.0f;
            return;
        }

        Layout();
        mousePosition = input.MousePosition;
        if (focusedTextId is { } focusedId && controls.All(control => !control.IsVisible || control.Id != focusedId))
        {
            focusedTextId = null;
        }

        var openOption = OpenOptionControl();
        WantsKeyboard = focusedTextId is not null;
        WantsMouse = Contains(panelBounds, mousePosition)
            || (openOption is not null && Contains(openOption.PopupBounds(lastViewportHeight), mousePosition));
        closeButtonHovered = Contains(CloseButtonBounds(), mousePosition);
        closeButtonActive = closeButtonHovered && input.LeftMouseDown;
        foreach (var control in controls.Where(control => control.IsVisible))
        {
            control.UpdateHover(mousePosition, bodyClipBounds);
            control.IsActive = control.Id == activeControlId && input.LeftMouseDown;
        }

        openOption?.UpdatePopupHover(mousePosition, lastViewportHeight);

        if (!input.LeftMouseDown)
        {
            activeSliderId = null;
            activeControlId = null;
        }

        if (openOption is not null
            && Contains(openOption.PopupBounds(lastViewportHeight), mousePosition)
            && Math.Abs(input.WheelDelta) > 0.0f)
        {
            openOption.ScrollPopup(input.WheelDelta, mousePosition, lastViewportHeight);
            return;
        }

        if (Math.Abs(input.WheelDelta) > 0.0f)
        {
            for (var index = controls.Count - 1; index >= 0; index--)
            {
                var control = controls[index];
                if (control.IsVisible
                    && control.HitTest(mousePosition)
                    && control is ITextInputControl textControl
                    && textControl.Scroll(input.WheelDelta))
                {
                    return;
                }
            }

            if (Contains(panelBounds, mousePosition) && ScrollBody(input.WheelDelta))
            {
                Layout();
                return;
            }
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

        if (activeControlId is { } textDragId && input.LeftMouseDown)
        {
            if (controls.FirstOrDefault(control => control.IsVisible && control.Id == textDragId) is ITextInputControl textControl)
            {
                textControl.SelectToPoint(mousePosition);
                return;
            }
        }

        if (!input.IsMousePressed(MouseButton.Left))
        {
            ApplyTextInput(input);
            return;
        }

        if (openOption is not null)
        {
            if (Contains(openOption.PopupBounds(lastViewportHeight), mousePosition))
            {
                openOption.ClickPopup(mousePosition, lastViewportHeight);
                focusedTextId = null;
                activeSliderId = null;
                activeControlId = null;
                return;
            }

            if (!openOption.HitTest(mousePosition))
            {
                openOption.DismissTransientState();
                focusedTextId = null;
                activeSliderId = null;
                activeControlId = null;
                return;
            }
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

            if (!Contains(bodyClipBounds, mousePosition) || !control.HitTest(mousePosition))
            {
                continue;
            }

            control.Click(mousePosition);
            activeControlId = control.Id;
            control.IsActive = true;
            if (control is OptionControl optionControl && optionControl.IsOpen)
            {
                optionControl.ClampScroll(lastViewportHeight);
            }

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
        IDWriteFactory6 directWriteFactory,
        IDWriteTextFormat titleFormat,
        IDWriteTextFormat smallFormat,
        IDWriteTextFormat monospaceFormat,
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

        lastViewportHeight = viewportHeight;
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

        target.PushAxisAlignedClip(bodyClipBounds, AntialiasMode.Aliased);
        foreach (var control in controls.Where(control => control.IsVisible && Intersects(control.Bounds, bodyClipBounds)))
        {
            control.IsFocused = control.Id == focusedTextId;
            control.Draw(
                target,
                directWriteFactory,
                control.UseMonospace ? monospaceFormat : smallFormat,
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
        target.PopAxisAlignedClip();

        DrawBodyScrollbar(target, quietBrush, accentBrush);

        OpenOptionControl()?.DrawPopup(
            target,
            smallFormat,
            rowBrush,
            hoverRowBrush,
            outlineBrush,
            primaryBrush,
            accentBrush,
            viewportHeight);

        DrawTooltip(target, smallFormat, panelBrush, outlineBrush, primaryBrush, accentBrush, viewportWidth, viewportHeight);
    }

    private OptionControl? OpenOptionControl()
    {
        return controls.OfType<OptionControl>().FirstOrDefault(control => control.IsVisible && control.IsOpen);
    }

    private void Layout()
    {
        var bodyTop = panelTop + HeaderHeight + (tabs.Count > 0 ? TabRowHeight : 0.0f) + 10.0f;
        var maxPanelBottom = MathF.Max(bodyTop + RowHeight, lastViewportHeight - ScreenMargin);
        var visibleControls = controls.Where(control => control.IsVisible).ToArray();
        bodyContentHeight = ContentHeight(visibleControls);
        var desiredBottom = bodyTop + bodyContentHeight + 12.0f;
        var bottom = lastViewportHeight > 0 ? Math.Min(desiredBottom, maxPanelBottom) : desiredBottom;
        panelBounds = RectFromEdges(panelLeft, panelTop, panelLeft + panelWidth, bottom);
        bodyClipBounds = RectFromEdges(panelBounds.Left, bodyTop, panelBounds.Right, MathF.Max(bodyTop, panelBounds.Bottom - 8.0f));
        bodyScrollOffset = Math.Clamp(bodyScrollOffset, 0.0f, MaxBodyScrollOffset());

        var y = bodyTop - bodyScrollOffset;
        foreach (var control in visibleControls)
        {
            var height = control.LayoutHeight;
            control.Bounds = RectFromEdges(panelLeft + 10.0f, y, panelLeft + panelWidth - 10.0f, y + height);
            y += height + (control is SectionControl ? 2.0f : RowGap);
        }
    }

    private static float ContentHeight(IReadOnlyList<DebugUiControl> visibleControls)
    {
        var height = 0.0f;
        for (var index = 0; index < visibleControls.Count; index++)
        {
            var control = visibleControls[index];
            height += control.LayoutHeight;
            if (index < visibleControls.Count - 1)
            {
                height += control is SectionControl ? 2.0f : RowGap;
            }
        }

        return height;
    }

    private bool ScrollBody(float wheelDelta)
    {
        var maxOffset = MaxBodyScrollOffset();
        if (maxOffset <= 0.0f)
        {
            bodyScrollOffset = 0.0f;
            return false;
        }

        var previous = bodyScrollOffset;
        bodyScrollOffset = Math.Clamp(bodyScrollOffset + (wheelDelta > 0.0f ? -72.0f : 72.0f), 0.0f, maxOffset);
        return MathF.Abs(bodyScrollOffset - previous) > 0.001f;
    }

    private float MaxBodyScrollOffset()
    {
        return MathF.Max(0.0f, bodyContentHeight - bodyClipBounds.Height + 12.0f);
    }

    private void DrawBodyScrollbar(ID2D1RenderTarget target, ID2D1SolidColorBrush trackBrush, ID2D1SolidColorBrush thumbBrush)
    {
        var maxOffset = MaxBodyScrollOffset();
        if (maxOffset <= 0.0f || bodyClipBounds.Height <= 0.0f)
        {
            return;
        }

        var track = RectFromEdges(panelBounds.Right - 7.0f, bodyClipBounds.Top + 2.0f, panelBounds.Right - 4.0f, bodyClipBounds.Bottom - 2.0f);
        var thumbHeight = MathF.Max(24.0f, track.Height * bodyClipBounds.Height / MathF.Max(bodyContentHeight + 12.0f, bodyClipBounds.Height));
        var thumbTop = track.Top + (track.Height - thumbHeight) * bodyScrollOffset / maxOffset;
        target.FillRectangle(track, trackBrush);
        target.FillRectangle(RectFromEdges(track.Left, thumbTop, track.Right, thumbTop + thumbHeight), thumbBrush);
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

    private static bool Intersects(Rect a, Rect b)
    {
        return a.Left < b.Right
            && a.Right > b.Left
            && a.Top < b.Bottom
            && a.Bottom > b.Top;
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

        public DebugUiPanel TextBox(string label, Func<string> read, Action<string> write, int lines = 3, bool acceptsReturn = true, Action? submit = null, bool monospace = false, bool alignBottom = false, string? tooltip = null, Func<bool>? isVisible = null)
        {
            controls.Add(new TextBoxControl(label, read, write, Math.Max(1, lines), acceptsReturn, submit, monospace, alignBottom, tooltip, isVisible));
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

        void SelectToPoint(Vector2 point);

        bool Scroll(float wheelDelta);
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

        public virtual bool UseMonospace => false;

        public virtual float LayoutHeight => this is SectionControl ? SectionHeight : RowHeight;

        public virtual bool HitTest(Vector2 mouse) => Contains(Bounds, mouse);

        public virtual void UpdateHover(Vector2 mouse, Rect clipBounds)
        {
            IsHovered = IsInteractive && Contains(clipBounds, mouse) && HitTest(mouse);
        }

        public virtual void Click(Vector2 mouse)
        {
        }

        public virtual void DismissTransientState()
        {
        }

        public abstract void Draw(
            ID2D1RenderTarget target,
            IDWriteFactory6 directWriteFactory,
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
            IDWriteFactory6 directWriteFactory,
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
            IDWriteFactory6 directWriteFactory,
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
            IDWriteFactory6 directWriteFactory,
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
        private int scrollOffset;

        public bool IsOpen => isOpen;

        public override void UpdateHover(Vector2 mouse, Rect clipBounds)
        {
            hoveredOptionIndex = -1;
            IsHovered = Contains(clipBounds, mouse) && HitTest(mouse);
        }

        public void UpdatePopupHover(Vector2 mouse, int viewportHeight)
        {
            hoveredOptionIndex = -1;
            if (!isOpen || !Contains(PopupBounds(viewportHeight), mouse))
            {
                return;
            }

            var localIndex = Math.Clamp((int)MathF.Floor((mouse.Y - PopupBounds(viewportHeight).Top) / RowHeight), 0, VisibleOptionCount(viewportHeight) - 1);
            var optionIndex = scrollOffset + localIndex;
            if (optionIndex >= 0 && optionIndex < options.Count)
            {
                hoveredOptionIndex = optionIndex;
            }
        }

        public override void Click(Vector2 mouse)
        {
            if (!isOpen)
            {
                isOpen = true;
                scrollOffset = SelectedOptionIndex();
                return;
            }

            if (Contains(MainBounds(), mouse))
            {
                isOpen = false;
            }
        }

        public void ClickPopup(Vector2 mouse, int viewportHeight)
        {
            UpdatePopupHover(mouse, viewportHeight);
            if (hoveredOptionIndex >= 0 && hoveredOptionIndex < options.Count)
            {
                write(options[hoveredOptionIndex].Value);
            }

            isOpen = false;
        }

        public void ScrollPopup(float wheelDelta, Vector2 mouse, int viewportHeight)
        {
            if (!isOpen || options.Count <= VisibleOptionCount(viewportHeight))
            {
                return;
            }

            scrollOffset += wheelDelta > 0.0f ? -3 : 3;
            ClampScroll(viewportHeight);
            UpdatePopupHover(mouse, viewportHeight);
        }

        public void ClampScroll(int viewportHeight)
        {
            var visibleCount = VisibleOptionCount(viewportHeight);
            var maxOffset = Math.Max(0, options.Count - visibleCount);
            scrollOffset = Math.Clamp(scrollOffset, 0, maxOffset);
        }

        public override void DismissTransientState()
        {
            isOpen = false;
        }

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteFactory6 directWriteFactory,
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
            var valueBounds = ValueFieldBounds();
            target.DrawText(SelectedLabel(), format, RectFromEdges(valueBounds.Left, main.Top, main.Right - 32.0f, main.Bottom), primaryBrush, DrawTextOptions.Clip);

            var arrowBrush = IsActive ? accentActiveBrush : IsHovered ? accentHoverBrush : accentBrush;
            var cy = (main.Top + main.Bottom) * 0.5f;
            target.DrawLine(new Vector2(main.Right - 24.0f, cy - 3.0f), new Vector2(main.Right - 18.0f, cy + 3.0f), arrowBrush, 1.5f);
            target.DrawLine(new Vector2(main.Right - 18.0f, cy + 3.0f), new Vector2(main.Right - 12.0f, cy - 3.0f), arrowBrush, 1.5f);
        }

        public void DrawPopup(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush hoverRowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush accentBrush,
            int viewportHeight)
        {
            if (!isOpen)
            {
                return;
            }

            ClampScroll(viewportHeight);
            var popup = PopupBounds(viewportHeight);
            target.FillRectangle(popup, rowBrush);
            target.DrawRectangle(popup, outlineBrush, 1.0f);
            var visibleCount = VisibleOptionCount(viewportHeight);
            for (var localIndex = 0; localIndex < visibleCount; localIndex++)
            {
                var optionIndex = scrollOffset + localIndex;
                if (optionIndex >= options.Count)
                {
                    break;
                }

                var bounds = OptionBounds(localIndex, viewportHeight);
                var selected = options[optionIndex].Value == read();
                var hovered = optionIndex == hoveredOptionIndex;
                target.FillRectangle(bounds, hovered ? hoverRowBrush : rowBrush);
                target.DrawLine(new Vector2(bounds.Left, bounds.Bottom), new Vector2(bounds.Right, bounds.Bottom), outlineBrush, 1.0f);
                var labelBrush = selected ? accentBrush : primaryBrush;
                target.DrawText(options[optionIndex].Label, format, RectFromEdges(bounds.Left + 18.0f, bounds.Top, bounds.Right - 12.0f, bounds.Bottom), labelBrush, DrawTextOptions.Clip);
                if (selected)
                {
                    target.DrawLine(new Vector2(bounds.Left + 7.0f, bounds.Top + 16.0f), new Vector2(bounds.Left + 10.0f, bounds.Top + 20.0f), accentBrush, 1.75f);
                    target.DrawLine(new Vector2(bounds.Left + 10.0f, bounds.Top + 20.0f), new Vector2(bounds.Left + 15.0f, bounds.Top + 10.0f), accentBrush, 1.75f);
                }
            }

            if (options.Count > visibleCount)
            {
                var track = RectFromEdges(popup.Right - 4.0f, popup.Top + 3.0f, popup.Right - 2.0f, popup.Bottom - 3.0f);
                var thumbHeight = Math.Max(12.0f, track.Height * visibleCount / options.Count);
                var maxOffset = Math.Max(1, options.Count - visibleCount);
                var thumbTop = track.Top + (track.Height - thumbHeight) * scrollOffset / maxOffset;
                target.FillRectangle(RectFromEdges(track.Left, thumbTop, track.Right, thumbTop + thumbHeight), accentBrush);
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

        public Rect PopupBounds(int viewportHeight)
        {
            var field = ValueFieldBounds();
            var desiredHeight = options.Count * RowHeight;
            var belowAvailable = Math.Max(RowHeight, viewportHeight - ScreenMargin - MainBounds().Bottom);
            var aboveAvailable = Math.Max(RowHeight, MainBounds().Top - ScreenMargin);
            var opensAbove = desiredHeight > belowAvailable && aboveAvailable > belowAvailable;
            var available = opensAbove ? aboveAvailable : belowAvailable;
            var height = VisibleOptionCount(viewportHeight) * RowHeight;
            var top = opensAbove ? MainBounds().Top - Math.Min(height, available) : MainBounds().Bottom;
            return RectFromEdges(field.Left, top, field.Right, top + Math.Min(height, available));
        }

        private Rect ValueFieldBounds()
        {
            var main = MainBounds();
            return RectFromEdges(main.Left + LabelWidth + 10.0f, main.Top, main.Right, main.Bottom);
        }

        private Rect OptionBounds(int localIndex, int viewportHeight)
        {
            var popup = PopupBounds(viewportHeight);
            var top = popup.Top + localIndex * RowHeight;
            return RectFromEdges(popup.Left, top, popup.Right, top + RowHeight);
        }

        private int VisibleOptionCount(int viewportHeight)
        {
            var desiredCount = options.Count;
            var belowAvailable = Math.Max(RowHeight, viewportHeight - ScreenMargin - MainBounds().Bottom);
            var aboveAvailable = Math.Max(RowHeight, MainBounds().Top - ScreenMargin);
            var desiredHeight = desiredCount * RowHeight;
            var opensAbove = desiredHeight > belowAvailable && aboveAvailable > belowAvailable;
            var available = opensAbove ? aboveAvailable : belowAvailable;
            return Math.Clamp((int)MathF.Floor(available / RowHeight), 1, Math.Max(1, desiredCount));
        }

        private int SelectedOptionIndex()
        {
            var value = read();
            for (var index = 0; index < options.Count; index++)
            {
                if (options[index].Value == value)
                {
                    return index;
                }
            }

            return 0;
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

        public void SelectToPoint(Vector2 point)
        {
        }

        public bool Scroll(float wheelDelta) => false;

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteFactory6 directWriteFactory,
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

    private sealed class TextBoxControl(string label, Func<string> read, Action<string> write, int lines, bool acceptsReturn, Action? submit, bool monospace, bool alignBottom, string? tooltip, Func<bool>? isVisible)
        : DebugUiControl(label, tooltip, isVisible), ITextInputControl
    {
        private const float LabelHeight = 20.0f;
        private const float TextLineHeight = 18.0f;
        private const float VerticalPadding = 9.0f;
        private const float FallbackCharacterWidth = 7.0f;
        private const float TextInsetX = 8.0f;
        private const float TextInsetY = 6.0f;
        private readonly List<LineHitCache> lineHitCache = [];
        private int caretIndex = -1;
        private int selectionAnchor = -1;
        private int firstVisibleLine = -1;
        private string lastValue = string.Empty;

        public override float LayoutHeight => LabelLineHeight + VerticalPadding * 2.0f + Math.Max(1, lines) * TextLineHeight;

        public override bool UseMonospace => monospace;

        public void ApplyTextInput(InputState input)
        {
            SyncEditingState();
            var value = NormalizedValue;
            var selecting = input.IsKeyDown(KeyCode.Shift);
            if (input.IsKeyPressed(KeyCode.LeftArrow))
            {
                MoveCaret(caretIndex - 1, selecting, value);
            }

            if (input.IsKeyPressed(KeyCode.RightArrow))
            {
                MoveCaret(caretIndex + 1, selecting, value);
            }

            if (input.IsKeyPressed(KeyCode.Home))
            {
                MoveCaret(HomeIndex(value, caretIndex), selecting, value);
            }

            if (input.IsKeyPressed(KeyCode.End))
            {
                MoveCaret(LineEnd(value, caretIndex), selecting, value);
            }

            if (input.IsKeyPressed(KeyCode.Backspace))
            {
                if (HasSelection)
                {
                    value = DeleteSelection(value);
                }
                else if (caretIndex > EditableStartIndex(value))
                {
                    value = value.Remove(caretIndex - 1, 1);
                    MoveCaret(caretIndex - 1, selecting: false, value);
                }
            }

            if (input.IsKeyPressed(KeyCode.Delete))
            {
                if (HasSelection)
                {
                    value = DeleteSelection(value);
                }
                else if (caretIndex < value.Length)
                {
                    value = value.Remove(caretIndex, 1);
                }
            }

            foreach (var ch in input.TextInput)
            {
                switch (ch)
                {
                    case '\b':
                        break;
                    case '\r':
                    case '\n':
                        if (acceptsReturn)
                        {
                            value = InsertText(value, "\n");
                        }
                        else
                        {
                            submit?.Invoke();
                            caretIndex = -1;
                            selectionAnchor = -1;
                            value = NormalizedValue;
                            SyncEditingState(force: true);
                        }
                        break;
                    default:
                        if (!char.IsControl(ch))
                        {
                            value = InsertText(value, ch.ToString());
                        }
                        break;
                }
            }

            if (value != NormalizedValue)
            {
                write(value);
                SyncCommittedValue(value);
            }
        }

        public void SelectToPoint(Vector2 point)
        {
            SyncEditingState();
            MoveCaret(IndexFromPoint(point), selecting: true, NormalizedValue);
        }

        public bool Scroll(float wheelDelta)
        {
            var value = NormalizedValue;
            var totalLines = LineCount(value);
            var maxFirstLine = Math.Max(0, totalLines - lines);
            if (maxFirstLine == 0)
            {
                firstVisibleLine = alignBottom ? 0 : -1;
                return false;
            }

            var step = wheelDelta > 0.0f ? -3 : 3;
            var current = VisibleStartLine(totalLines);
            firstVisibleLine = Math.Clamp(current + step, 0, maxFirstLine);
            return firstVisibleLine != current;
        }

        public override void Click(Vector2 mouse)
        {
            SyncEditingState();
            MoveCaret(IndexFromPoint(mouse), selecting: false, NormalizedValue);
        }

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteFactory6 directWriteFactory,
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
            if (HasLabel)
            {
                var labelBounds = RectFromEdges(Bounds.Left + 8.0f, Bounds.Top, Bounds.Right - 8.0f, Bounds.Top + LabelHeight);
                target.DrawText(Label.ToUpperInvariant(), format, labelBounds, accentBrush, DrawTextOptions.Clip);
            }

            var box = RectFromEdges(Bounds.Left, Bounds.Top + LabelLineHeight, Bounds.Right, Bounds.Bottom);
            var focused = IsFocused || IsActive;
            target.FillRectangle(box, rowBrush);
            target.DrawRectangle(box, focused ? accentBrush : outlineBrush, focused ? 1.5f : 1.0f);

            CacheLineHitGeometry(directWriteFactory, format, box);
            DrawSelection(target, directWriteFactory, format, activeRowBrush, box);
            DrawBoxText(target, directWriteFactory, format, primaryBrush, box);
            DrawCaret(target, directWriteFactory, format, accentBrush, box);
        }

        private string DisplayText()
        {
            return string.Join('\n', DisplayLines());
        }

        private Rect TextBounds(Rect box)
        {
            return RectFromEdges(box.Left + TextInsetX, box.Top + TextInsetY, box.Right - TextInsetX, box.Bottom - TextInsetY);
        }

        private void DrawBoxText(
            ID2D1RenderTarget target,
            IDWriteFactory6 directWriteFactory,
            IDWriteTextFormat format,
            ID2D1Brush brush,
            Rect box)
        {
            var bounds = TextBounds(box);
            var displayLines = DisplayLines();
            var top = TextTop(bounds, displayLines.Length);
            for (var index = 0; index < displayLines.Length; index++)
            {
                using var layout = directWriteFactory.CreateTextLayout(displayLines[index], format, bounds.Width, TextLineHeight);
                target.DrawTextLayout(new Vector2(bounds.Left, top + index * TextLineHeight), layout, brush, DrawTextOptions.Clip);
            }
        }

        private string[] DisplayLines()
        {
            var split = NormalizedValue.Split('\n');
            var startLine = VisibleStartLine(split.Length);
            return split.Skip(startLine).Take(lines).ToArray();
        }

        private int DisplayStartIndex()
        {
            var split = NormalizedValue.Split('\n');
            var skippedLines = VisibleStartLine(split.Length);
            var index = 0;
            for (var line = 0; line < skippedLines; line++)
            {
                index += split[line].Length + 1;
            }

            return index;
        }

        private void SyncEditingState(bool force = false)
        {
            var value = NormalizedValue;
            if (!force && caretIndex >= 0 && string.Equals(value, lastValue, StringComparison.Ordinal))
            {
                return;
            }

            lastValue = value;
            caretIndex = ClampEditableIndex(value.Length, value);
            selectionAnchor = caretIndex;
            firstVisibleLine = alignBottom ? Math.Max(0, LineCount(value) - lines) : 0;
        }

        private void SyncCommittedValue(string value)
        {
            lastValue = value;
            caretIndex = ClampEditableIndex(caretIndex, value);
            selectionAnchor = ClampEditableIndex(selectionAnchor, value);
            firstVisibleLine = Math.Clamp(firstVisibleLine, 0, Math.Max(0, LineCount(value) - lines));
            EnsureCaretVisible(value);
        }

        private void MoveCaret(int index, bool selecting, string value)
        {
            caretIndex = ClampEditableIndex(index, value);
            if (!selecting)
            {
                selectionAnchor = caretIndex;
            }
            else
            {
                selectionAnchor = ClampEditableIndex(selectionAnchor, value);
            }

            EnsureCaretVisible(value);
        }

        private string InsertText(string value, string text)
        {
            if (HasSelection)
            {
                value = DeleteSelection(value);
            }

            var insertionIndex = ClampEditableIndex(caretIndex, value);
            value = value.Insert(insertionIndex, text);
            MoveCaret(insertionIndex + text.Length, selecting: false, value);
            return value;
        }

        private string DeleteSelection(string value)
        {
            var editableStart = EditableStartIndex(value);
            var start = Math.Max(SelectionStart, editableStart);
            var end = Math.Max(start, Math.Min(SelectionEnd, value.Length));
            var length = end - start;
            if (length == 0)
            {
                MoveCaret(start, selecting: false, value);
                return value;
            }

            value = value.Remove(start, length);
            MoveCaret(start, selecting: false, value);
            return value;
        }

        private int IndexFromPoint(Vector2 point)
        {
            var bounds = TextBounds(BoxBounds);
            var displayLines = DisplayLines();
            var top = TextTop(bounds, displayLines.Length);
            var line = Math.Clamp((int)MathF.Floor((point.Y - top) / TextLineHeight), 0, Math.Max(0, displayLines.Length - 1));
            var cache = line < lineHitCache.Count ? lineHitCache[line] : default;
            if (cache.Edges.Length > 0)
            {
                var localX = point.X - cache.OriginX;
                var nearest = 0;
                var nearestDistance = float.MaxValue;
                for (var edgeIndex = 0; edgeIndex < cache.Edges.Length; edgeIndex++)
                {
                    var distance = MathF.Abs(cache.Edges[edgeIndex] - localX);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = edgeIndex;
                    }
                }

                return ClampEditableIndex(cache.GlobalStart + nearest, NormalizedValue);
            }

            var column = Math.Max(0, (int)MathF.Round((point.X - bounds.Left) / FallbackCharacterWidth));
            var index = DisplayStartIndex();
            for (var lineIndex = 0; lineIndex < line; lineIndex++)
            {
                index += displayLines[lineIndex].Length + 1;
            }

            return ClampEditableIndex(index + Math.Min(column, displayLines[line].Length), NormalizedValue);
        }

        private void DrawSelection(
            ID2D1RenderTarget target,
            IDWriteFactory6 directWriteFactory,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush activeRowBrush,
            Rect box)
        {
            if (!HasSelection)
            {
                return;
            }

            var bounds = TextBounds(box);
            var displayLines = DisplayLines();
            var displayStart = DisplayStartIndex();
            var displayEnd = displayStart + DisplayText().Length;
            var start = Math.Max(SelectionStart, displayStart);
            var end = Math.Min(SelectionEnd, displayEnd);
            if (start >= end)
            {
                return;
            }

            var top = TextTop(bounds, displayLines.Length);
            var lineStart = displayStart;
            for (var line = 0; line < displayLines.Length; line++)
            {
                var lineEnd = lineStart + displayLines[line].Length;
                var startColumn = Math.Max(0, start - lineStart);
                var endColumn = Math.Min(displayLines[line].Length, end - lineStart);
                if (endColumn > startColumn)
                {
                    using var layout = directWriteFactory.CreateTextLayout(displayLines[line], format, bounds.Width, TextLineHeight);
                    var startHit = TextPositionHit(layout, startColumn, displayLines[line].Length);
                    var endHit = TextPositionHit(layout, endColumn, displayLines[line].Length);
                    var vertical = TextVerticalHit(layout, displayLines[line].Length);
                    var selectionTop = top + line * TextLineHeight + vertical.Top;
                    var selectionBottom = selectionTop + vertical.Height;
                    target.FillRectangle(
                        RectFromEdges(
                            bounds.Left + startHit.X,
                            selectionTop,
                            bounds.Left + endHit.X,
                            selectionBottom),
                        activeRowBrush);
                }

                lineStart = lineEnd + 1;
            }
        }

        private void DrawCaret(
            ID2D1RenderTarget target,
            IDWriteFactory6 directWriteFactory,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush accentBrush,
            Rect box)
        {
            if (!IsFocused)
            {
                return;
            }

            var bounds = TextBounds(box);
            var displayLines = DisplayLines();
            var displayStart = DisplayStartIndex();
            var displayEnd = displayStart + DisplayText().Length;
            if (caretIndex < displayStart || caretIndex > displayEnd)
            {
                return;
            }

            var top = TextTop(bounds, displayLines.Length);
            var lineStart = displayStart;
            for (var line = 0; line < displayLines.Length; line++)
            {
                var lineEnd = lineStart + displayLines[line].Length;
                if (caretIndex <= lineEnd || line == displayLines.Length - 1)
                {
                    var column = Math.Clamp(caretIndex - lineStart, 0, displayLines[line].Length);
                    using var layout = directWriteFactory.CreateTextLayout(displayLines[line], format, bounds.Width, TextLineHeight);
                    var hit = TextPositionHit(layout, column, displayLines[line].Length);
                    var x = bounds.Left + hit.X;
                    var y = top + line * TextLineHeight + hit.Top;
                    target.DrawLine(new Vector2(x, y), new Vector2(x, y + hit.Height), accentBrush, 1.25f);
                    return;
                }

                lineStart = lineEnd + 1;
            }
        }

        private static int LineStart(string value, int index)
        {
            var clamped = Math.Clamp(index, 0, value.Length);
            if (clamped == 0)
            {
                return 0;
            }

            var previousNewline = value.LastIndexOf('\n', Math.Max(0, clamped - 1));
            return previousNewline < 0 ? 0 : previousNewline + 1;
        }

        private int HomeIndex(string value, int index)
        {
            return LocksToPrompt ? EditableStartIndex(value) : LineStart(value, index);
        }

        private static int LineEnd(string value, int index)
        {
            var nextNewline = value.IndexOf('\n', Math.Clamp(index, 0, value.Length));
            return nextNewline < 0 ? value.Length : nextNewline;
        }

        private float TextTop(Rect bounds, int lineCount)
        {
            return alignBottom
                ? Math.Max(bounds.Top, bounds.Bottom - lineCount * TextLineHeight)
                : bounds.Top;
        }

        private void CacheLineHitGeometry(IDWriteFactory6 directWriteFactory, IDWriteTextFormat format, Rect box)
        {
            lineHitCache.Clear();
            var bounds = TextBounds(box);
            var displayLines = DisplayLines();
            var top = TextTop(bounds, displayLines.Length);
            var globalStart = DisplayStartIndex();
            for (var line = 0; line < displayLines.Length; line++)
            {
                using var layout = directWriteFactory.CreateTextLayout(displayLines[line], format, bounds.Width, TextLineHeight);
                var edges = new float[displayLines[line].Length + 1];
                for (var index = 0; index < edges.Length; index++)
                {
                    edges[index] = TextPositionHit(layout, index, displayLines[line].Length).X;
                }

                lineHitCache.Add(new LineHitCache(globalStart, bounds.Left, top + line * TextLineHeight, edges));
                globalStart += displayLines[line].Length + 1;
            }
        }

        private static TextHit TextPositionHit(IDWriteTextLayout layout, int position, int length)
        {
            if (length == 0)
            {
                return new TextHit(0.0f, 2.0f, TextLineHeight - 2.0f);
            }

            var clamped = Math.Clamp(position, 0, length);
            uint textPosition;
            RawBool trailing;
            if (clamped == length)
            {
                textPosition = (uint)Math.Max(0, length - 1);
                trailing = new RawBool(true);
            }
            else
            {
                textPosition = (uint)clamped;
                trailing = new RawBool(false);
            }

            layout.HitTestTextPosition(textPosition, trailing, out var x, out _, out var metrics);
            return new TextHit(x, metrics.Top, metrics.Height);
        }

        private static TextHit TextVerticalHit(IDWriteTextLayout layout, int length)
        {
            return TextPositionHit(layout, Math.Min(1, length), length);
        }

        private bool HasLabel => !string.IsNullOrWhiteSpace(Label);

        private float LabelLineHeight => HasLabel ? LabelHeight : 0.0f;

        private Rect BoxBounds => RectFromEdges(Bounds.Left, Bounds.Top + LabelLineHeight, Bounds.Right, Bounds.Bottom);

        private string NormalizedValue => read().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        private bool HasSelection => caretIndex != selectionAnchor;

        private int SelectionStart => Math.Min(caretIndex, selectionAnchor);

        private int SelectionEnd => Math.Max(caretIndex, selectionAnchor);

        private bool LocksToPrompt => submit is not null && !acceptsReturn;

        private int ClampEditableIndex(int index, string value)
        {
            return Math.Clamp(index, EditableStartIndex(value), value.Length);
        }

        private int EditableStartIndex(string value)
        {
            if (!LocksToPrompt)
            {
                return 0;
            }

            var promptIndex = value.LastIndexOf("\n> ", StringComparison.Ordinal);
            if (promptIndex >= 0)
            {
                return Math.Min(value.Length, promptIndex + 3);
            }

            if (value.StartsWith("> ", StringComparison.Ordinal))
            {
                return Math.Min(value.Length, 2);
            }

            if (value.EndsWith("\n>", StringComparison.Ordinal) || string.Equals(value, ">", StringComparison.Ordinal))
            {
                return value.Length;
            }

            return value.Length;
        }

        private int VisibleStartLine(int totalLines)
        {
            var maxFirstLine = Math.Max(0, totalLines - lines);
            if (firstVisibleLine < 0)
            {
                return alignBottom ? maxFirstLine : 0;
            }

            return Math.Clamp(firstVisibleLine, 0, maxFirstLine);
        }

        private void EnsureCaretVisible(string value)
        {
            var totalLines = LineCount(value);
            var maxFirstLine = Math.Max(0, totalLines - lines);
            var caretLine = LineNumber(value, caretIndex);
            var first = VisibleStartLine(totalLines);
            if (caretLine < first)
            {
                firstVisibleLine = caretLine;
            }
            else if (caretLine >= first + lines)
            {
                firstVisibleLine = caretLine - lines + 1;
            }
            else if (firstVisibleLine < 0)
            {
                firstVisibleLine = first;
            }

            firstVisibleLine = Math.Clamp(firstVisibleLine, 0, maxFirstLine);
        }

        private static int LineCount(string value)
        {
            return value.Count(ch => ch == '\n') + 1;
        }

        private static int LineNumber(string value, int index)
        {
            var clamped = Math.Clamp(index, 0, value.Length);
            var line = 0;
            for (var i = 0; i < clamped; i++)
            {
                if (value[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private readonly record struct LineHitCache(int GlobalStart, float OriginX, float OriginY, float[] Edges);

        private readonly record struct TextHit(float X, float Top, float Height);
    }

    private sealed class ReadoutControl(string label, Func<string> read, string? tooltip, Func<bool>? isVisible)
        : DebugUiControl(label, tooltip, isVisible)
    {
        public override bool IsInteractive => !string.IsNullOrWhiteSpace(Tooltip);

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteFactory6 directWriteFactory,
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

        public override void UpdateHover(Vector2 mouse, Rect clipBounds)
        {
            thumbHovered = Contains(clipBounds, mouse) && Contains(ThumbInteractionBounds(), mouse);
            trackHovered = Contains(clipBounds, mouse) && !thumbHovered && Contains(TrackInteractionBounds(), mouse);
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
            IDWriteFactory6 directWriteFactory,
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
