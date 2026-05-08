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
    private readonly IDWriteFactory6 directWriteFactory;
    private readonly IDWriteFontCollection fontCollection;
    private readonly IDWriteTypography smallCapsTypography;
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
        titleFormat = CreateTextFormat("Montserrat", 18.0f, FontWeight.Thin);
        smallFormat = CreateTextFormat("Ubuntu Sans", 11.0f, FontWeight.Regular);
    }

    public void Render(AquariumFrame frame)
    {
        renderTarget.BeginDraw();
        DrawHeader(
            "directwrite overlay",
            titleFormat,
            new Rect(18, 14, Math.Min(width - 18, 340), 42),
            primaryTextBrush);
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
        smallCapsTypography.Dispose();
        fontCollection.Dispose();
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
}
