using System.Numerics;

namespace Aquarium.Engine;

public readonly record struct AquariumFrame(GridFrame Grid, Vector3 CameraPosition, float TimeSeconds);

