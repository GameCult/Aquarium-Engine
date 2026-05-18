using System.Buffers;
using MessagePack;
using Aquarium.LocalCast;

namespace Aquarium.LocalCast.Tests;

public sealed class LocalCastVisualStateReaderTests
{
    [Fact]
    public void ParsesPythonCultCacheRenderFrameStore()
    {
        var store = WriteStore();

        Assert.True(LocalCastVisualStateReader.TryParseStore(store, out var frame));
        Assert.Equal(7, frame.FrameId);
        Assert.Equal("LocalCastBridge Point Cloud", frame.SpoutSenderName);
        var point = Assert.Single(frame.Points);
        Assert.Equal("rgb:1", point.StableKey);
        Assert.Equal(0.03f, point.RadiusMeters, 6);
    }

    [Fact]
    public void ParsesPythonCultCacheClapEventsStore()
    {
        var store = WriteStore(includeClapEvents: true);

        Assert.True(LocalCastVisualStateReader.TryParseClapEventsStore(store, out var frame));
        Assert.Equal(12, frame.FrameId);
        var clap = Assert.Single(frame.Events);
        Assert.Equal("clap:host:0001", clap.StableKey);
        Assert.Equal(123_456_789_000L, clap.AcousticOracleNs);
        Assert.Equal(1.8f, clap.TimingUncertaintyMicroseconds, 6);
        Assert.True(clap.AcousticConfidence > clap.VisualConfidence);
    }

    private static byte[] WriteStore(bool includeClapEvents = false)
    {
        var payload = WriteRenderFramePayload();
        var recordCount = includeClapEvents ? 2 : 1;
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(recordCount);
        writer.WriteMapHeader(4);
        writer.Write("key");
        writer.Write(LocalCastVisualStateReader.LiveRenderFrameKey);
        writer.Write("type");
        writer.Write(LocalCastVisualStateReader.RenderFrameType);
        writer.Write("storedAt");
        writer.Write("2026-05-18T18:35:15Z");
        writer.Write("payload");
        writer.Write(payload);
        if (includeClapEvents)
        {
            writer.WriteMapHeader(4);
            writer.Write("key");
            writer.Write(LocalCastVisualStateReader.LiveClapEventsKey);
            writer.Write("type");
            writer.Write(LocalCastVisualStateReader.ClapEventsType);
            writer.Write("storedAt");
            writer.Write("2026-05-18T18:35:15Z");
            writer.Write("payload");
            writer.Write(WriteClapEventsPayload());
        }
        writer.Flush();
        return buffer.WrittenMemory.ToArray();
    }

    private static byte[] WriteRenderFramePayload()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(11);
        writer.Write(LocalCastVisualStateReader.RenderFrameSchemaId);
        writer.Write(7L);
        writer.Write(1_000_000L);
        writer.Write(1_000_000L);
        writer.Write(1_010_000L);
        writer.Write(1_260_000L);
        writer.Write(1_260_000L);
        writer.Write("LocalCastBridge Point Cloud");
        writer.Write(1920);
        writer.Write(1080);
        writer.WriteArrayHeader(1);
        writer.WriteArrayHeader(6);
        writer.Write("rgb:1");
        writer.WriteArrayHeader(3);
        writer.Write(0.1f);
        writer.Write(0.2f);
        writer.Write(1.2f);
        writer.Write(0.03f);
        writer.WriteArrayHeader(4);
        writer.Write(0.7f);
        writer.Write(0.6f);
        writer.Write(0.5f);
        writer.Write(0.9f);
        writer.Write(0.8f);
        writer.Write(1_010_000L);
        writer.Flush();
        return buffer.WrittenMemory.ToArray();
    }

    private static byte[] WriteClapEventsPayload()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(4);
        writer.Write(LocalCastVisualStateReader.ClapEventsSchemaId);
        writer.Write(12L);
        writer.Write(1_000_000L);
        writer.WriteArrayHeader(1);
        writer.WriteArrayHeader(8);
        writer.Write("clap:host:0001");
        writer.WriteArrayHeader(3);
        writer.Write(0.12f);
        writer.Write(0.4f);
        writer.Write(1.35f);
        writer.Write(123_456_789_000L);
        writer.Write(123_456_805_000L);
        writer.Write(1.8f);
        writer.Write(0.82f);
        writer.Write(0.97f);
        writer.WriteArrayHeader(1);
        writer.WriteMapHeader(4);
        writer.Write("sensorId");
        writer.Write("kiyo-pro");
        writer.Write("timestampNs");
        writer.Write(123_456_805_000L);
        writer.Write("uv");
        writer.WriteArrayHeader(2);
        writer.Write(320.0f);
        writer.Write(240.0f);
        writer.Write("score");
        writer.Write(0.9f);
        writer.Flush();
        return buffer.WrittenMemory.ToArray();
    }
}
