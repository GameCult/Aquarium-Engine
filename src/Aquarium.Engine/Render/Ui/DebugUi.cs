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
    private const float TrackWidth = 116.0f;
    private const float TrackHeight = 5.0f;

    private readonly List<DebugUiControl> controls = [];
    private int? activeSliderId;
    private Rect panelBounds;

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
        var mouse = input.MousePosition;
        WantsMouse = Contains(panelBounds, mouse);

        if (!input.LeftMouseDown)
        {
            activeSliderId = null;
        }

        if (activeSliderId is { } sliderId)
        {
            if (controls.FirstOrDefault(control => control.Id == sliderId) is SliderControl activeSlider)
            {
                activeSlider.Drag(mouse);
            }

            return;
        }

        if (!input.IsMousePressed(MouseButton.Left))
        {
            return;
        }

        for (var index = controls.Count - 1; index >= 0; index--)
        {
            var control = controls[index];
            if (!control.HitTest(mouse))
            {
                continue;
            }

            control.Click(mouse);
            if (control is SliderControl slider)
            {
                activeSliderId = slider.Id;
                slider.Drag(mouse);
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
        ID2D1SolidColorBrush outlineBrush,
        ID2D1SolidColorBrush primaryBrush,
        ID2D1SolidColorBrush quietBrush,
        ID2D1SolidColorBrush accentBrush,
        ID2D1SolidColorBrush dimAccentBrush)
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
            RectFromEdges(panelBounds.Left + 12.0f, panelBounds.Top + 8.0f, panelBounds.Right - 12.0f, panelBounds.Top + HeaderHeight - 4.0f),
            primaryBrush,
            DrawTextOptions.Clip);

        foreach (var control in controls)
        {
            control.Draw(target, smallFormat, rowBrush, outlineBrush, primaryBrush, quietBrush, accentBrush, dimAccentBrush);
        }

        target.DrawText(
            "F2 hide",
            smallFormat,
            RectFromEdges(panelBounds.Left + 12.0f, panelBounds.Bottom - 26.0f, panelBounds.Right - 12.0f, panelBounds.Bottom - 8.0f),
            quietBrush,
            DrawTextOptions.Clip);
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

        public DebugUiPanel Button(string label, Action action)
        {
            controls.Add(new ButtonControl(label, action));
            return this;
        }

        public DebugUiPanel Toggle(string label, Func<bool> read, Action<bool> write)
        {
            controls.Add(new ToggleControl(label, read, write));
            return this;
        }

        public DebugUiPanel Slider(string label, Func<float> read, Action<float> write, float min, float max, string format = "0.###")
        {
            controls.Add(new FloatSliderControl(label, read, write, min, max, format));
            return this;
        }

        public DebugUiPanel Slider(string label, Func<int> read, Action<int> write, int min, int max)
        {
            controls.Add(new IntSliderControl(label, read, write, min, max));
            return this;
        }
    }

    internal abstract class DebugUiControl(string label)
    {
        private static int nextId;

        public int Id { get; } = nextId++;

        protected string Label { get; } = label;

        public Rect Bounds { get; set; }

        public virtual bool HitTest(Vector2 mouse) => Contains(Bounds, mouse);

        public virtual void Click(Vector2 mouse)
        {
        }

        public abstract void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush dimAccentBrush);

        protected void DrawRow(ID2D1RenderTarget target, ID2D1SolidColorBrush rowBrush, ID2D1SolidColorBrush outlineBrush)
        {
            target.FillRectangle(Bounds, rowBrush);
            target.DrawLine(
                new Vector2(Bounds.Left, Bounds.Bottom),
                new Vector2(Bounds.Right, Bounds.Bottom),
                outlineBrush,
                1.0f);
        }

        protected Rect LabelBounds()
        {
            return RectFromEdges(Bounds.Left + 8.0f, Bounds.Top + 6.0f, Bounds.Left + LabelWidth, Bounds.Bottom);
        }

        protected Rect ValueBounds()
        {
            return RectFromEdges(Bounds.Left + LabelWidth + 10.0f, Bounds.Top + 6.0f, Bounds.Right - TrackWidth - 20.0f, Bounds.Bottom);
        }
    }

    private sealed class SectionControl(string label) : DebugUiControl(label)
    {
        public override bool HitTest(Vector2 mouse) => false;

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
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

    private sealed class ButtonControl(string label, Action action) : DebugUiControl(label)
    {
        public override void Click(Vector2 mouse) => action();

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush dimAccentBrush)
        {
            DrawRow(target, rowBrush, outlineBrush);
            target.DrawText(Label, format, LabelBounds(), accentBrush, DrawTextOptions.Clip);
            target.DrawText("apply", format, ValueBounds(), primaryBrush, DrawTextOptions.Clip);
        }
    }

    private sealed class ToggleControl(string label, Func<bool> read, Action<bool> write) : DebugUiControl(label)
    {
        public override void Click(Vector2 mouse) => write(!read());

        public override void Draw(
            ID2D1RenderTarget target,
            IDWriteTextFormat format,
            ID2D1SolidColorBrush rowBrush,
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush dimAccentBrush)
        {
            DrawRow(target, rowBrush, outlineBrush);
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

    private abstract class SliderControl(string label) : DebugUiControl(label)
    {
        public override bool HitTest(Vector2 mouse)
        {
            return Contains(Bounds, mouse);
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
            ID2D1SolidColorBrush outlineBrush,
            ID2D1SolidColorBrush primaryBrush,
            ID2D1SolidColorBrush quietBrush,
            ID2D1SolidColorBrush accentBrush,
            ID2D1SolidColorBrush dimAccentBrush)
        {
            DrawRow(target, rowBrush, outlineBrush);
            target.DrawText(Label.ToUpperInvariant(), format, LabelBounds(), accentBrush, DrawTextOptions.Clip);
            target.DrawText(ReadDisplay(), format, ValueBounds(), primaryBrush, DrawTextOptions.Clip);

            var track = TrackBounds();
            var t = ReadNormalized();
            var fill = RectFromEdges(track.Left, track.Top, track.Left + (track.Right - track.Left) * t, track.Bottom);
            target.FillRectangle(track, dimAccentBrush);
            target.FillRectangle(fill, accentBrush);
            var thumbX = fill.Right;
            target.FillRectangle(RectFromEdges(thumbX - 4.0f, track.Top - 4.0f, thumbX + 4.0f, track.Bottom + 4.0f), accentBrush);
        }

        private Rect TrackBounds()
        {
            var right = Bounds.Right - 12.0f;
            var left = right - TrackWidth;
            var centerY = (Bounds.Top + Bounds.Bottom) * 0.5f;
            return RectFromEdges(left, centerY - TrackHeight * 0.5f, right, centerY + TrackHeight * 0.5f);
        }
    }

    private sealed class FloatSliderControl(
        string label,
        Func<float> read,
        Action<float> write,
        float min,
        float max,
        string format) : SliderControl(label)
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
        int max) : SliderControl(label)
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
