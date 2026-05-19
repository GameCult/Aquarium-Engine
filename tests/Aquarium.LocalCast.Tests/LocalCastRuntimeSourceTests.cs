using System.Numerics;
using Aquarium.Engine;
using Aquarium.Engine.Render;
using Aquarium.Engine.Input;
using Aquarium.LocalCast;

namespace Aquarium.LocalCast.Tests;

public sealed class LocalCastRuntimeSourceTests
{
    [Fact]
    public void RuntimeConsumesInjectedFrameSource()
    {
        var source = new FakeFrameSource();
        var runtime = new LocalCastRuntime(new AquariumRuntimeOptions(true, null), source);

        runtime.Update(0.016f, new InputState());
        var frame = runtime.Frame;

        Assert.Equal(1, source.ReadCount);
        Assert.True(frame.Scene.GpuFusionField.Seeds.Count > 0);
    }

    [Fact]
    public void RuntimePublishesNativePointBufferWithoutManagedSeedMapping()
    {
        var source = new FakeFrameSource(
            new AquariumGpuFusionPointBuffer((IntPtr)0x1000, 3, 56));
        var runtime = new LocalCastRuntime(new AquariumRuntimeOptions(true, null), source);

        runtime.Update(0.016f, new InputState());
        var frame = runtime.Frame;

        Assert.Empty(frame.Scene.GpuFusionField.Seeds);
        Assert.True(frame.Scene.GpuFusionField.PointBuffer.HasInput);
        Assert.Equal(3, frame.Scene.GpuFusionField.PointBuffer.Count);
        Assert.Equal(56, frame.Scene.GpuFusionField.PointBuffer.StrideBytes);
        Assert.Empty(frame.Scene.TemporalGaussianField.Gaussians);
    }

    private sealed class FakeFrameSource : ILocalCastVisualFrameSource
    {
        private readonly AquariumGpuFusionPointBuffer pointBuffer;

        public FakeFrameSource(AquariumGpuFusionPointBuffer pointBuffer = default)
        {
            this.pointBuffer = pointBuffer;
        }

        public int ReadCount { get; private set; }

        public string Description => "fake-native-source";

        public bool TryReadLatest(out LocalCastVisualFrame frame)
        {
            ReadCount++;
            frame = new LocalCastVisualFrame
            {
                SchemaVersion = LocalCastVisualStateReader.RenderFrameSchemaId,
                FrameId = 10,
                CreatedMonotonicNs = 100,
                SourceTimeMinNs = 100,
                SourceTimeMaxNs = 120,
                PresentTimeNs = 160,
                AudioAlignmentTimeNs = 160,
                SpoutSenderName = "LocalCastBridge Point Cloud",
                TargetWidth = 1920,
                TargetHeight = 1080,
                Points =
                [
                    new LocalCastVisualPoint(
                        "fake:point",
                        new Vector3(0.1f, 0.2f, 1.2f),
                        0.03f,
                        new Vector4(0.7f, 0.6f, 0.5f, 0.9f),
                        0.8f,
                        120),
                ],
                NativeGpuFusionPointBuffer = pointBuffer,
            };
            return true;
        }

        public bool TryReadLatestClapEvents(out LocalCastClapCalibrationFrame frame)
        {
            frame = new LocalCastClapCalibrationFrame
            {
                SchemaVersion = LocalCastVisualStateReader.ClapEventsSchemaId,
                FrameId = -1,
                CreatedMonotonicNs = 0,
                Events = [],
            };
            return false;
        }
    }
}
