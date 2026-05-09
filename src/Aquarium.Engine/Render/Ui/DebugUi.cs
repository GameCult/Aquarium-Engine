using System.Globalization;
using System.Numerics;
using Aquarium.Engine.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Aquarium.Engine.Render.Ui;

internal sealed class DebugUi
{
    private const float PanelLeft = 18.0f;
    private const float PanelTop = 82.0f;
    private const float PanelWidth = 360.0f;
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

    private readonly List<DebugUiControl> controls = [];
    private int? activeSliderId;
    private int? activeControlId;
    private bool closeButtonHovered;
    private bool closeButtonActive;
    private Rect panelBounds;
    private Vector2 mousePosition;

    public DebugUi(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public bool IsVisible { get; set; } = true;

    public bool WantsMouse { get; private set; }

    public DebugUi Panel(Action<DebugUiPanel> compose)
    {
        var panel = new DebugUiPanel(controls);
        compose(panel);
        return this;
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
            return;
        }

        Layout();
        mousePosition = input.MousePosition;
        WantsMouse = Contains(panelBounds, mousePosition);
        closeButtonHovered = Contains(CloseButtonBounds(), mousePosition);
        closeButtonActive = closeButtonHovered && input.LeftMouseDown;
        foreach (var control in controls)
        {
            control.IsHovered = control.IsInteractive && control.HitTest(mousePosition);
            control.IsActive = control.Id == activeControlId && input.LeftMouseDown;
        }

        if (!input.LeftMouseDown)
        {
            activeSliderId = null;
            activeControlId = null;
        }

        if (activeSliderId is { } sliderId)
        {
            if (controls.FirstOrDefault(control => control.Id == sliderId) is SliderControl activeSlider)
            {
                activeSlider.IsActive = true;
                activeSlider.Drag(mousePosition);
            }

            return;
        }

        if (!input.IsMousePressed(MouseButton.Left))
        {
            return;
        }

        if (closeButtonHovered)
        {
            IsVisible = false;
            return;
        }

        for (var index = controls.Count - 1; index >= 0; index--)
        {
            var control = controls[index];
            if (!control.HitTest(mousePosition))
            {
                continue;
            }

            control.Click(mousePosition);
            activeControlId = control.Id;
            if (control is SliderControl slider)
            {
                activeSliderId = slider.Id;
                slider.Drag(mousePosition);
            }

            break;
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
        ID2D1SolidColorBrush dimAccentBrush,
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

        foreach (var control in controls)
        {
            control.Draw(target, smallFormat, rowBrush, hoverRowBrush, activeRowBrush, outlineBrush, primaryBrush, quietBrush, accentBrush, dimAccentBrush);
        }

        DrawTooltip(target, smallFormat, panelBrush, outlineBrush, primaryBrush, accentBrush, viewportWidth, viewportHeight);
    }

    private void Layout()
    {
        var y = PanelTop + HeaderHeight + 10.0f;
        foreach (var control in controls)
        {
            var height = control is SectionControl ? SectionHeight : RowHeight;
            control.Bounds = RectFromEdges(PanelLeft + 10.0f, y, PanelLeft + PanelWidth - 10.0f, y + height);
            y += height + (control is SectionControl ? 2.0f : RowGap);
        }

        panelBounds = RectFromEdges(PanelLeft, PanelTop, PanelLeft + PanelWidth, y + 30.0f);
    }

    private Rect CloseButtonBounds()
    {
        return RectFromEdges(
            panelBounds.Right - CloseButtonSize - 10.0f,
            panelBounds.Top + 11.0f,
            panelBounds.Right - 10.0f,
            panelBounds.Top + 11.0f + CloseButtonSize);
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
            : controls.FirstOrDefault(control => control.IsHovered)?.Tooltip;
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

        public DebugUiPanel Section(string title)
        {
            controls.Add(new SectionControl(title));
            return this;
        }

        public DebugUiPanel Button(string label, Action action, string? tooltip = null)
        {
            controls.Add(new ButtonControl(label, action, tooltip));
            return this;
        }

        public DebugUiPanel Toggle(string label, Func<bool> read, Action<bool> write, string? tooltip = null)
        {
            controls.Add(new ToggleControl(label, read, write, tooltip));
            return this;
        }

        public DebugUiPanel Slider(string label, Func<float> read, Action<float> write, float min, float max, string format = "0.###", string? tooltip = null)
        {
            controls.Add(new FloatSliderControl(label, read, write, min, max, format, tooltip));
            return this;
        }

        public DebugUiPanel Slider(string label, Func<int> read, Action<int> write, int min, int max, string? tooltip = null)
        {
            controls.Add(new IntSliderControl(label, read, write, min, max, tooltip));
            return this;
        }
    }

    internal abstract class DebugUiControl(string label, string? tooltip = null)
    {
        private static int nextId;

        public int Id { get; } = nextId++;

        protected string Label { get; } = label;

        public string? Tooltip { get; } = tooltip;

        public Rect Bounds { get; set; }

        public bool IsHovered { get; set; }

        public bool IsActive { get; set; }

        public virtual bool IsInteractive => true;

        public virtual bool HitTest(Vector2 mouse) => Contains(Bounds, mouse);

        public virtual void Click(Vector2 mouse)
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
            ID2D1SolidColorBrush dimAccentBrush);

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

    private sealed class SectionControl(string label) : DebugUiControl(label)
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
            ID2D1SolidColorBrush dimAccentBrush)
        {
            target.DrawText(Label.ToUpperInvariant(), format, Bounds, accentBrush, DrawTextOptions.Clip);
            target.DrawLine(
                new Vector2(Bounds.Left + 118.0f, Bounds.Top + 11.0f),
                new Vector2(Bounds.Right, Bounds.Top + 11.0f),
                outlineBrush,
                1.0f);
        }
    }

    private sealed class ButtonControl(string label, Action action, string? tooltip) : DebugUiControl(label, tooltip)
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
            ID2D1SolidColorBrush dimAccentBrush)
        {
            DrawRow(target, rowBrush, hoverRowBrush, activeRowBrush, outlineBrush);
            target.DrawText(Label, format, LabelBounds(), accentBrush, DrawTextOptions.Clip);
            target.DrawText("apply", format, ValueBounds(), primaryBrush, DrawTextOptions.Clip);
        }
    }

    private sealed class ToggleControl(string label, Func<bool> read, Action<bool> write, string? tooltip) : DebugUiControl(label, tooltip)
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
            ID2D1SolidColorBrush dimAccentBrush)
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

    private abstract class SliderControl(string label, string? tooltip) : DebugUiControl(label, tooltip)
    {
        public override bool HitTest(Vector2 mouse)
        {
            return Contains(SliderInteractionBounds(), mouse);
        }

        public void Drag(Vector2 mouse)
        {
            var track = TrackBounds();
            var normalized = Math.Clamp((mouse.X - track.Left) / Math.Max(1.0f, track.Right - track.Left), 0.0f, 1.0f);
            WriteNormalized(normalized);
        }

        public override void Click(Vector2 mouse)
        {
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
            ID2D1SolidColorBrush dimAccentBrush)
        {
            DrawRow(target, rowBrush, rowBrush, rowBrush, outlineBrush);
            target.DrawText(Label.ToUpperInvariant(), format, LabelBounds(), accentBrush, DrawTextOptions.Clip);
            target.DrawText(ReadDisplay(), format, ValueBounds(), primaryBrush, DrawTextOptions.Clip);

            var track = TrackBounds();
            var t = ReadNormalized();
            var fill = RectFromEdges(track.Left, track.Top, track.Left + (track.Right - track.Left) * t, track.Bottom);
            target.FillRectangle(track, IsActive ? activeRowBrush : IsHovered ? hoverRowBrush : dimAccentBrush);
            target.FillRectangle(fill, accentBrush);
            var thumbX = fill.Right;
            var thumbRadius = IsActive ? 5.5f : IsHovered ? 5.0f : 4.0f;
            target.FillRectangle(RectFromEdges(thumbX - thumbRadius, track.Top - thumbRadius, thumbX + thumbRadius, track.Bottom + thumbRadius), accentBrush);
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
    }

    private sealed class FloatSliderControl(
        string label,
        Func<float> read,
        Action<float> write,
        float min,
        float max,
        string format,
        string? tooltip) : SliderControl(label, tooltip)
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
        string? tooltip) : SliderControl(label, tooltip)
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
