using GameCult.Caching;
using MessagePack;

namespace Aquarium.Engine.State;

[CultDocument("epiphany.aquarium.live_state", "epiphany.aquarium.live_state.v0")]
[MessagePackObject]
public sealed class AquariumLiveState
{
    public const string PrimaryName = "aquarium-client";

    [Key(0)]
    [CultName]
    public string Name { get; set; } = PrimaryName;

    [Key(1)]
    public float TimeSeconds { get; set; }

    [Key(2)]
    public float CameraTargetX { get; set; }

    [Key(3)]
    public float CameraTargetY { get; set; }

    [Key(4)]
    public float CameraYawRadians { get; set; } = Angle.DegreesToRadians(35.0f);

    [Key(5)]
    public float CameraPitchRadians { get; set; } = Angle.DegreesToRadians(42.0f);

    [Key(6)]
    public float CameraDistance { get; set; } = 24.0f;

    [Key(7)]
    public long SaveGeneration { get; set; }
}
