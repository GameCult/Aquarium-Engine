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
                    10_000_000_000,
                    SharedHandleName: "LocalCastBridge/KiyoProLeft/Bgra")
            ],
        };

        var scene = new AquariumSceneState { GpuSensorFrame = frame };

        Assert.True(scene.GpuSensorFrame.HasInput);
        Assert.Equal("kiyo-pro-left", scene.GpuSensorFrame.Cameras[0].SensorId);
        Assert.Equal(AquariumGpuSensorPixelFormat.Bgra8Unorm, scene.GpuSensorFrame.ExternalTextures[0].PixelFormat);
        Assert.Equal("LocalCastBridge/KiyoProLeft/Bgra", scene.GpuSensorFrame.ExternalTextures[0].SharedHandleName);
        Assert.Equal(18.0f, scene.GpuSensorFrame.AccumulationWindowSeconds, 6);
    }

    [Fact]
    public void SceneStateCarriesAcousticTimingOracleWithRoomConstraints()
    {
        var acoustic = new AquariumAcousticFieldFrame
        {
            TimingOracleNs = 99_000_000_123,
            TimingConfidence = 0.98f,
            TimingUncertaintyMicroseconds = 2.5f,
            AccumulationWindowSeconds = 6.0f,
            PresentationDelaySeconds = 2.0f,
            Constraints =
            [
                new AquariumAcousticConstraint(
                    "ultrasonic-wall-return:front",
                    AquariumAcousticConstraintKind.UltrasonicReflector,
                    new Vector3(0.0f, 1.2f, 1.1f),
                    Vector3.Zero,
                    0.18f,
                    0.87f,
                    99_000_000_123)
            ],
        };

        var scene = new AquariumSceneState { AcousticFieldFrame = acoustic };

        Assert.True(scene.AcousticFieldFrame.HasInput);
        Assert.Equal(99_000_000_123, scene.AcousticFieldFrame.TimingOracleNs);
        Assert.Equal(2.5f, scene.AcousticFieldFrame.TimingUncertaintyMicroseconds, 6);
        Assert.Equal(AquariumAcousticConstraintKind.UltrasonicReflector, scene.AcousticFieldFrame.Constraints[0].Kind);
    }

    [Fact]
    public void SceneStateCarriesClapCalibrationEventAlignedToAcousticOracle()
    {
        var events = new AquariumCalibrationEventFrame
        {
            ClapEvents =
            [
                new AquariumClapCalibrationEvent(
                    "clap:host:0001",
                    new Vector3(0.12f, 0.4f, 1.35f),
                    AcousticOracleNs: 123_456_789_000,
                    VisualObservedNs: 123_456_805_000,
                    TimingUncertaintyMicroseconds: 1.8f,
                    VisualConfidence: 0.82f,
                    AcousticConfidence: 0.97f)
            ],
        };

        var scene = new AquariumSceneState { CalibrationEventFrame = events };

        Assert.True(scene.CalibrationEventFrame.HasInput);
        Assert.Equal(123_456_789_000, scene.CalibrationEventFrame.ClapEvents[0].AcousticOracleNs);
        Assert.True(scene.CalibrationEventFrame.ClapEvents[0].AcousticConfidence > scene.CalibrationEventFrame.ClapEvents[0].VisualConfidence);
    }
}
