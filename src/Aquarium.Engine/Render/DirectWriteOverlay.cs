using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Aquarium.Engine.Render.Ui;
using D2DFactoryType = Vortice.Direct2D1.FactoryType;
using DWriteFactoryType = Vortice.DirectWrite.FactoryType;

namespace Aquarium.Engine.Render;

internal sealed class DirectWriteOverlay : IDisposable
{
    private readonly ID2D1Factory direct2DFactory;
    private readonly IDWriteFactory6 directWriteFactory;
    private readonly IDWriteFontCollection fontCollection;
    private readonly IDWriteTypography smallCapsTypography;
    private readonly ID2D1RenderTarget renderTarget;
    private readonly ID2D1SolidColorBrush primaryTextBrush;
    private readonly ID2D1SolidColorBrush quietTextBrush;
    private readonly ID2D1SolidColorBrush panelBrush;
    private readonly ID2D1SolidColorBrush rowBrush;
    private readonly ID2D1SolidColorBrush hoverRowBrush;
    private readonly ID2D1SolidColorBrush activeRowBrush;
    private readonly ID2D1SolidColorBrush outlineBrush;
    private readonly ID2D1SolidColorBrush accentBrush;
    private readonly ID2D1SolidColorBrush accentHoverBrush;
    private readonly ID2D1SolidColorBrush accentActiveBrush;
    private readonly ID2D1SolidColorBrush dimAccentBrush;
    private readonly ID2D1SolidColorBrush trackHoverBrush;
    private readonly ID2D1SolidColorBrush trackActiveBrush;
    private readonly IDWriteTextFormat titleFormat;
    private readonly IDWriteTextFormat smallFormat;
    private readonly int width;
    private readonly int height;

    public DirectWriteOverlay(IDXGISurface backBufferSurface, int width, int height)
    {
        this.width = width;
        this.height = height;

        direct2DFactory = D2D1.D2D1CreateFactory<ID2D1Factory>(D2DFactoryType.SingleThreaded);
        directWriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory6>(DWriteFactoryType.Shared);
        fontCollection = CreateBrandFontCollection();
        smallCapsTypography = directWriteFactory.CreateTypography();
        smallCapsTypography.AddFontFeature(new FontFeature
        {
            NameTag = FontFeatureTag.SmallCapitalsFromCapitals,
            Parameter = 1
        });
        smallCapsTypography.AddFontFeature(new FontFeature
        {
            NameTag = FontFeatureTag.CapitalSpacing,
            Parameter = 1
        });

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
        panelBrush = renderTarget.CreateSolidColorBrush(new Color4(0.018f, 0.022f, 0.032f, 0.96f));
        rowBrush = renderTarget.CreateSolidColorBrush(new Color4(0.085f, 0.084f, 0.12f, 0.96f));
        hoverRowBrush = renderTarget.CreateSolidColorBrush(new Color4(0.13f, 0.13f, 0.18f, 0.98f));
        activeRowBrush = renderTarget.CreateSolidColorBrush(new Color4(0.19f, 0.18f, 0.24f, 0.99f));
        outlineBrush = renderTarget.CreateSolidColorBrush(new Color4(0.28f, 0.34f, 0.36f, 0.72f));
        accentBrush = renderTarget.CreateSolidColorBrush(new Color4(1.0f, 0.38f, 0.055f, 0.96f));
        accentHoverBrush = renderTarget.CreateSolidColorBrush(new Color4(1.0f, 0.58f, 0.30f, 0.98f));
        accentActiveBrush = renderTarget.CreateSolidColorBrush(new Color4(1.0f, 0.78f, 0.58f, 1.0f));
        dimAccentBrush = renderTarget.CreateSolidColorBrush(new Color4(0.20f, 0.20f, 0.22f, 0.95f));
        trackHoverBrush = renderTarget.CreateSolidColorBrush(new Color4(0.32f, 0.29f, 0.29f, 0.98f));
        trackActiveBrush = renderTarget.CreateSolidColorBrush(new Color4(0.45f, 0.38f, 0.34f, 1.0f));
        titleFormat = CreateTextFormat("Montserrat", 18.0f, FontWeight.Thin);
        smallFormat = CreateTextFormat("Ubuntu Sans", 11.0f, FontWeight.Regular);
    }

    public void Render(AquariumFrame frame, int renderDebugMode, DebugUi? debugUi)
    {
        renderTarget.BeginDraw();
        DrawHeader(
            "directwrite overlay",
            titleFormat,
            RectFromEdges(18, 14, Math.Min(width - 18, 340), 42),
            primaryTextBrush);
        renderTarget.DrawText(
            $"view r {frame.View.Radius:0.00}  cam z {frame.CameraPosition.Z:0.00}",
            smallFormat,
            RectFromEdges(18, 38, Math.Min(width - 18, 420), 62),
            quietTextBrush,
            DrawTextOptions.Clip);
        if (renderDebugMode > 0)
        {
            renderTarget.DrawText(
                $"render debug {renderDebugMode}  F1 cycle  debug panel selects",
                smallFormat,
                RectFromEdges(18, 56, Math.Min(width - 18, 420), 80),
                quietTextBrush,
                DrawTextOptions.Clip);
        }
        renderTarget.DrawText(
            "crisp text belongs after tonemapping",
            smallFormat,
            RectFromEdges(18, Math.Max(64, height - 36), Math.Min(width - 18, 420), height - 12),
            quietTextBrush,
            DrawTextOptions.Clip);
        debugUi?.Draw(
            renderTarget,
            titleFormat,
            smallFormat,
            panelBrush,
            rowBrush,
            hoverRowBrush,
            activeRowBrush,
            outlineBrush,
            primaryTextBrush,
            quietTextBrush,
            accentBrush,
            accentHoverBrush,
            accentActiveBrush,
            dimAccentBrush,
            trackHoverBrush,
            trackActiveBrush,
            width,
            height);
        renderTarget.EndDraw();
    }

    public void Dispose()
    {
        smallFormat.Dispose();
        titleFormat.Dispose();
        smallCapsTypography.Dispose();
        fontCollection.Dispose();
        dimAccentBrush.Dispose();
        trackActiveBrush.Dispose();
        trackHoverBrush.Dispose();
        accentActiveBrush.Dispose();
        accentHoverBrush.Dispose();
        accentBrush.Dispose();
        outlineBrush.Dispose();
        activeRowBrush.Dispose();
        hoverRowBrush.Dispose();
        rowBrush.Dispose();
        panelBrush.Dispose();
        quietTextBrush.Dispose();
        primaryTextBrush.Dispose();
        renderTarget.Dispose();
        directWriteFactory.Dispose();
        direct2DFactory.Dispose();
    }

    private IDWriteTextFormat CreateTextFormat(string familyName, float size, FontWeight weight)
    {
        var format = directWriteFactory.CreateTextFormat(
            familyName,
            fontCollection,
            weight,
            FontStyle.Normal,
            FontStretch.Normal,
            size,
            "en-us");
        format.WordWrapping = WordWrapping.NoWrap;
        format.ParagraphAlignment = ParagraphAlignment.Center;
        return format;
    }

    private IDWriteFontCollection CreateBrandFontCollection()
    {
        using var builder = directWriteFactory.CreateFontSetBuilder();
        builder.AddFontFile(FontAssetPath("Montserrat[wght].ttf"));
        builder.AddFontFile(FontAssetPath("UbuntuSans[wdth,wght].ttf"));
        using var fontSet = builder.CreateFontSet();
        return directWriteFactory.CreateFontCollectionFromFontSet(fontSet, FontFamilyModel.Typographic);
    }

    private void DrawHeader(string text, IDWriteTextFormat format, Rect bounds, ID2D1Brush brush)
    {
        var displayText = text.ToUpperInvariant();
        using var layout = directWriteFactory.CreateTextLayout(displayText, format, bounds.Width, bounds.Height);
        layout.SetTypography(smallCapsTypography, new TextRange(0, (uint)displayText.Length));
        renderTarget.DrawTextLayout(new System.Numerics.Vector2(bounds.Left, bounds.Top), layout, brush, DrawTextOptions.Clip);
    }

    private static string FontAssetPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", fileName);
    }

    private static Rect RectFromEdges(float left, float top, float right, float bottom)
    {
        return new Rect(left, top, Math.Max(0.0f, right - left), Math.Max(0.0f, bottom - top));
    }
}
