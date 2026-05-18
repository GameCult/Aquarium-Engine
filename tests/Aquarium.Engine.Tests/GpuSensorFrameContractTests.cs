using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Engine.Tests;

public sealed class GpuSensorFrameContractTests
{
    [Fact]
    public void SceneStateCarriesSharedGpuSensorFrameAsRendererInput()
    {
        var frame = new AquariumGpuSensorFrame
        {
            AccumulationWindowSeconds = 18.0f,
            PresentationDelaySeconds = 0.35f,
            Cameras =
            [
                new AquariumGpuSensorCamera(
                    "kiyo-pro-left",
                    AquariumGpuSensorKind.RgbCamera,
                    Matrix4x4.Identity,
                    Matrix4x4.Identity,
                    new Vector4(840.0f, 840.0f, 960.0f, 540.0f),
                    Vector4.Zero,
                    Vector4.Zero,
                    1920,
                    1080,
                    0,
                    1,
                    10_000_000_000)
            ],
            ExternalTextures =
            [
                new AquariumExternalGpuTexture(
                    new TextureHandle("kiyo-pro-left-bgra"),
                    new IntPtr(1234),
                    1920,
                    1080,
                    AquariumGpuSensorPixelFormat.Bgra8Unorm,
                    10_000_000_000)
            ],
        };

        var scene = new AquariumSceneState { GpuSensorFrame = frame };

        Assert.True(scene.GpuSensorFrame.HasInput);
        Assert.Equal("kiyo-pro-left", scene.GpuSensorFrame.Cameras[0].SensorId);
        Assert.Equal(AquariumGpuSensorPixelFormat.Bgra8Unorm, scene.GpuSensorFrame.ExternalTextures[0].PixelFormat);
        Assert.Equal(18.0f, scene.GpuSensorFrame.AccumulationWindowSeconds, 6);
    }
}
