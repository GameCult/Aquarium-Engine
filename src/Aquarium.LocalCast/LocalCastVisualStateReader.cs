using System.Buffers;
using System.Numerics;
using MessagePack;

namespace Aquarium.LocalCast;

public sealed class LocalCastVisualStateReader
{
    public const string LiveRenderFrameKey = "localcast.visual.render-frame.live";
    public const string RenderFrameType = "localcast.visual.render_frame";
    public const string RenderFrameSchemaId = "gamecult.localcast.visual.render_frame.v1";
    public const string LiveClapEventsKey = "localcast.calibration.clap-events.live";
    public const string ClapEventsType = "localcast.calibration.clap_events";
    public const string ClapEventsSchemaId = "gamecult.localcast.calibration.clap_events.v1";

    private readonly string path;

    public LocalCastVisualStateReader(string path)
    {
        this.path = System.IO.Path.GetFullPath(path);
    }

    public string Path => path;

    public bool TryReadLatest(out LocalCastVisualFrame frame)
    {
        frame = null!;

        if (!File.Exists(path))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bytes = new byte[stream.Length];
            var read = stream.Read(bytes, 0, bytes.Length);
            if (read != bytes.Length)
            {
                Array.Resize(ref bytes, read);
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        try
        {
            return TryParseStore(bytes, out frame);
        }
        catch (MessagePackSerializationException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    public bool TryReadLatestClapEvents(out LocalCastClapCalibrationFrame frame)
    {
        frame = null!;

        if (!File.Exists(path))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bytes = new byte[stream.Length];
            var read = stream.Read(bytes, 0, bytes.Length);
            if (read != bytes.Length)
            {
                Array.Resize(ref bytes, read);
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        try
        {
            return TryParseClapEventsStore(bytes, out frame);
        }
        catch (MessagePackSerializationException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    public static bool TryParseStore(ReadOnlyMemory<byte> payload, out LocalCastVisualFrame frame)
    {
        frame = null!;
        var reader = new MessagePackReader(payload);
        var recordCount = reader.ReadArrayHeader();

        for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
        {
            var mapCount = reader.ReadMapHeader();
            string? key = null;
            string? type = null;
            byte[]? documentPayload = null;

            for (var mapIndex = 0; mapIndex < mapCount; mapIndex++)
            {
                var field = reader.ReadString();
                switch (field)
                {
                    case "key":
                        key = reader.ReadString();
                        break;
                    case "type":
                        type = reader.ReadString();
                        break;
                    case "payload":
                        var bytes = reader.ReadBytes();
                        documentPayload = bytes.HasValue ? bytes.Value.ToArray() : null;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (string.Equals(key, LiveRenderFrameKey, StringComparison.Ordinal)
                && string.Equals(type, RenderFrameType, StringComparison.Ordinal)
                && documentPayload is not null)
            {
                frame = ParseRenderFrame(documentPayload);
                return string.Equals(frame.SchemaVersion, RenderFrameSchemaId, StringComparison.Ordinal);
            }
        }

        return false;
    }

    public static bool TryParseClapEventsStore(ReadOnlyMemory<byte> payload, out LocalCastClapCalibrationFrame frame)
    {
        frame = null!;
        var reader = new MessagePackReader(payload);
        var recordCount = reader.ReadArrayHeader();

        for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
        {
            var mapCount = reader.ReadMapHeader();
            string? key = null;
            string? type = null;
            byte[]? documentPayload = null;

            for (var mapIndex = 0; mapIndex < mapCount; mapIndex++)
            {
                var field = reader.ReadString();
                switch (field)
                {
                    case "key":
                        key = reader.ReadString();
                        break;
                    case "type":
                        type = reader.ReadString();
                        break;
                    case "payload":
                        var bytes = reader.ReadBytes();
                        documentPayload = bytes.HasValue ? bytes.Value.ToArray() : null;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (string.Equals(key, LiveClapEventsKey, StringComparison.Ordinal)
                && string.Equals(type, ClapEventsType, StringComparison.Ordinal)
                && documentPayload is not null)
            {
                frame = ParseClapEvents(documentPayload);
                return string.Equals(frame.SchemaVersion, ClapEventsSchemaId, StringComparison.Ordinal);
            }
        }

        return false;
    }

    public static LocalCastVisualFrame ParseRenderFrame(ReadOnlyMemory<byte> payload)
    {
        var reader = new MessagePackReader(payload);
        var fieldCount = reader.ReadArrayHeader();
        if (fieldCount < 11)
        {
            throw new InvalidOperationException($"Render frame payload has {fieldCount} fields; expected at least 11.");
        }

        var frame = new LocalCastVisualFrame
        {
            SchemaVersion = reader.ReadString() ?? string.Empty,
            FrameId = reader.ReadInt64(),
            CreatedMonotonicNs = reader.ReadInt64(),
            SourceTimeMinNs = reader.ReadInt64(),
            SourceTimeMaxNs = reader.ReadInt64(),
            PresentTimeNs = reader.ReadInt64(),
            AudioAlignmentTimeNs = reader.ReadInt64(),
            SpoutSenderName = reader.ReadString() ?? string.Empty,
            TargetWidth = reader.ReadInt32(),
            TargetHeight = reader.ReadInt32(),
            Points = ReadPoints(ref reader),
        };

        for (var fieldIndex = 11; fieldIndex < fieldCount; fieldIndex++)
        {
            reader.Skip();
        }

        return frame;
    }

    public static LocalCastClapCalibrationFrame ParseClapEvents(ReadOnlyMemory<byte> payload)
    {
        var reader = new MessagePackReader(payload);
        var fieldCount = reader.ReadArrayHeader();
        if (fieldCount < 4)
        {
            throw new InvalidOperationException($"Clap events payload has {fieldCount} fields; expected at least 4.");
        }

        var frame = new LocalCastClapCalibrationFrame
        {
            SchemaVersion = reader.ReadString() ?? string.Empty,
            FrameId = reader.ReadInt64(),
            CreatedMonotonicNs = reader.ReadInt64(),
            Events = ReadClapEvents(ref reader),
        };

        for (var fieldIndex = 4; fieldIndex < fieldCount; fieldIndex++)
        {
            reader.Skip();
        }

        return frame;
    }

    private static IReadOnlyList<LocalCastVisualPoint> ReadPoints(ref MessagePackReader reader)
    {
        var count = reader.ReadArrayHeader();
        var points = new LocalCastVisualPoint[count];
        for (var index = 0; index < count; index++)
        {
            var fieldCount = reader.ReadArrayHeader();
            if (fieldCount < 6)
            {
                throw new InvalidOperationException($"Render point payload has {fieldCount} fields; expected at least 6.");
            }

            var stableKey = reader.ReadString() ?? string.Empty;
            var position = ReadVector3(ref reader);
            var radius = reader.ReadSingle();
            var color = ReadVector4(ref reader);
            var confidence = reader.ReadSingle();
            var timestampNs = reader.ReadInt64();
            for (var fieldIndex = 6; fieldIndex < fieldCount; fieldIndex++)
            {
                reader.Skip();
            }

            points[index] = new LocalCastVisualPoint(
                stableKey,
                position,
                MathF.Max(0.001f, radius),
                color,
                Math.Clamp(confidence, 0.0f, 1.0f),
                timestampNs);
        }

        return points;
    }

    private static IReadOnlyList<LocalCastClapCalibrationEvent> ReadClapEvents(ref MessagePackReader reader)
    {
        var count = reader.ReadArrayHeader();
        var events = new LocalCastClapCalibrationEvent[count];
        for (var index = 0; index < count; index++)
        {
            var fieldCount = reader.ReadArrayHeader();
            if (fieldCount < 8)
            {
                throw new InvalidOperationException($"Clap event payload has {fieldCount} fields; expected at least 8.");
            }

            var stableKey = reader.ReadString() ?? string.Empty;
            var position = ReadVector3(ref reader);
            var acousticOracleNs = reader.ReadInt64();
            var visualObservedNs = reader.ReadInt64();
            var timingUncertaintyUs = reader.ReadSingle();
            var visualConfidence = reader.ReadSingle();
            var acousticConfidence = reader.ReadSingle();
            reader.Skip();
            for (var fieldIndex = 8; fieldIndex < fieldCount; fieldIndex++)
            {
                reader.Skip();
            }

            events[index] = new LocalCastClapCalibrationEvent(
                stableKey,
                position,
                acousticOracleNs,
                visualObservedNs,
                timingUncertaintyUs,
                Math.Clamp(visualConfidence, 0.0f, 1.0f),
                Math.Clamp(acousticConfidence, 0.0f, 1.0f));
        }

        return events;
    }

    private static Vector3 ReadVector3(ref MessagePackReader reader)
    {
        var count = reader.ReadArrayHeader();
        if (count < 3)
        {
            throw new InvalidOperationException($"Vector3 payload has {count} fields; expected at least 3.");
        }

        var value = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        for (var index = 3; index < count; index++)
        {
            reader.Skip();
        }

        return value;
    }

    private static Vector4 ReadVector4(ref MessagePackReader reader)
    {
        var count = reader.ReadArrayHeader();
        if (count < 4)
        {
            throw new InvalidOperationException($"Vector4 payload has {count} fields; expected at least 4.");
        }

        var value = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        for (var index = 4; index < count; index++)
        {
            reader.Skip();
        }

        return value;
    }
}
