namespace Aquarium.Engine;

public static class Angle
{
    public static float DegreesToRadians(float degrees)
    {
        return degrees * (MathF.PI / 180.0f);
    }
}
