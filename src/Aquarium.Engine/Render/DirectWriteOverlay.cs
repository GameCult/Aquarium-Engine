using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2DFactoryType = Vortice.Direct2D1.FactoryType;
using DWriteFactoryType = Vortice.DirectWrite.FactoryType;

namespace Aquarium.Engine.Render;

internal sealed class DirectWriteOverlay : IDisposable
{
    private readonly ID2D1Factory direct2DFactory;
    private readonly IDWriteFactory directWriteFactory;
    private readonly ID2D1RenderTarget renderTarget;
    private readonly ID2D1SolidColorBrush primaryTextBrush;
    private readonly ID2D1SolidColorBrush quietTextBrush;
    private readonly IDWriteTextFormat titleFormat;
    private readonly IDWriteTextFormat smallFormat;
    private readonly int width;
    private readonly int height;

    public DirectWriteOverlay(IDXGISurface backBufferSurface, int width, int height)
    {
        this.width = width;
        this.height = height;

        direct2DFactory = D2D1.D2D1CreateFactory<ID2D1Factory>(D2DFactoryType.SingleThreaded);
        directWriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>(DWriteFactoryType.Shared);

        var renderTargetProperties = new RenderTargetProperties(
            RenderTargetType.Default,
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore),
            96.0f,
            96.0f,
            RenderTargetUsage.None,
            FeatureLevel.Default);
        renderTarget = direct2DFactory.CreateDxgiSurfaceRenderTarget(backBufferSurface, renderTargetProperties);
        renderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
        renderTarget.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;

        primaryTextBrush = renderTarget.CreateSolidColorBrush(new Color4(0.88f, 0.96f, 1.0f, 0.92f));
        quietTextBrush = renderTarget.CreateSolidColorBrush(new Color4(0.54f, 0.68f, 0.72f, 0.72f));
        titleFormat = CreateTextFormat(16.0f, FontWeight.SemiBold);
        smallFormat = CreateTextFormat(11.0f, FontWeight.Medium);
    }

    public void Render(AquariumFrame frame)
    {
        renderTarget.BeginDraw();
        renderTarget.DrawText(
            "DIRECTWRITE OVERLAY",
            titleFormat,
            new Rect(18, 14, Math.Min(width - 18, 340), 42),
            primaryTextBrush,
            DrawTextOptions.Clip);
        renderTarget.DrawText(
            $"grid r {frame.Grid.Radius:0.00}  cam z {frame.CameraPosition.Z:0.00}",
            smallFormat,
            new Rect(18, 38, Math.Min(width - 18, 420), 62),
            quietTextBrush,
            DrawTextOptions.Clip);
        renderTarget.DrawText(
            "crisp text belongs after tonemapping",
            smallFormat,
            new Rect(18, Math.Max(64, height - 36), Math.Min(width - 18, 420), height - 12),
            quietTextBrush,
            DrawTextOptions.Clip);
        renderTarget.EndDraw();
    }

    public void Dispose()
    {
        smallFormat.Dispose();
        titleFormat.Dispose();
        quietTextBrush.Dispose();
        primaryTextBrush.Dispose();
        renderTarget.Dispose();
        directWriteFactory.Dispose();
        direct2DFactory.Dispose();
    }

    private IDWriteTextFormat CreateTextFormat(float size, FontWeight weight)
    {
        var format = directWriteFactory.CreateTextFormat(
            "Segoe UI",
            null,
            weight,
            FontStyle.Normal,
            FontStretch.Normal,
            size,
            "en-us");
        format.WordWrapping = WordWrapping.NoWrap;
        return format;
    }
}
