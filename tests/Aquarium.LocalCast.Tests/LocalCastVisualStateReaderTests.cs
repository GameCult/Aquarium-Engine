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

    private static byte[] WriteStore()
    {
        var payload = WriteRenderFramePayload();
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(1);
        writer.WriteMapHeader(4);
        writer.Write("key");
        writer.Write(LocalCastVisualStateReader.LiveRenderFrameKey);
        writer.Write("type");
        writer.Write(LocalCastVisualStateReader.RenderFrameType);
        writer.Write("storedAt");
        writer.Write("2026-05-18T18:35:15Z");
        writer.Write("payload");
        writer.Write(payload);
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
}
