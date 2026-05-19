namespace Aquarium.Engine.Fractal.Lod;

public sealed class FractalSurfacePagePayload
{
    public FractalSurfacePagePayload(AquariumFractalSurfacePage page, float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length != page.Width * page.Height)
        {
            throw new ArgumentException("Surface page sample count must match page dimensions.", nameof(samples));
        }

        Page = page;
        Samples = samples;
    }

    public AquariumFractalSurfacePage Page { get; }

    public float[] Samples { get; }
}
