namespace Aquarium.LocalCast;

public interface ILocalCastVisualFrameSource
{
    string Description { get; }

    bool TryReadLatest(out LocalCastVisualFrame frame);

    bool TryReadLatestClapEvents(out LocalCastClapCalibrationFrame frame);
}

public sealed class LocalCastVisualStateFileSource : ILocalCastVisualFrameSource
{
    private readonly LocalCastVisualStateReader reader;

    public LocalCastVisualStateFileSource(string path)
    {
        reader = new LocalCastVisualStateReader(path);
    }

    public string Description => reader.Path;

    public bool TryReadLatest(out LocalCastVisualFrame frame)
    {
        return reader.TryReadLatest(out frame);
    }

    public bool TryReadLatestClapEvents(out LocalCastClapCalibrationFrame frame)
    {
        return reader.TryReadLatestClapEvents(out frame);
    }
}
