using System.Numerics;

namespace Aquarium.Engine.Input;

public sealed class InputState
{
    private readonly HashSet<KeyCode> downKeys = [];
    private readonly HashSet<KeyCode> pressedKeys = [];

    public Vector2 MousePosition { get; private set; }

    public Vector2 MouseDelta { get; private set; }

    public float WheelDelta { get; private set; }

    public bool MiddleMouseDown { get; private set; }

    public bool RightMouseDown { get; private set; }

    public bool IsKeyDown(KeyCode key) => downKeys.Contains(key);

    public bool IsKeyPressed(KeyCode key) => pressedKeys.Contains(key);

    public void BeginFrame()
    {
        MouseDelta = Vector2.Zero;
        WheelDelta = 0.0f;
        pressedKeys.Clear();
    }

    public void SetMousePosition(Vector2 position)
    {
        MouseDelta += position - MousePosition;
        MousePosition = position;
    }

    public void AddWheelDelta(float delta)
    {
        WheelDelta += delta;
    }

    public void SetMouseButton(MouseButton button, bool isDown)
    {
        switch (button)
        {
            case MouseButton.Middle:
                MiddleMouseDown = isDown;
                break;
            case MouseButton.Right:
                RightMouseDown = isDown;
                break;
        }
    }

    public void SetKey(KeyCode key, bool isDown)
    {
        if (isDown)
        {
            if (!downKeys.Contains(key))
            {
                pressedKeys.Add(key);
            }

            downKeys.Add(key);
        }
        else
        {
            downKeys.Remove(key);
        }
    }
}

public enum MouseButton
{
    Middle,
    Right,
}

public enum KeyCode
{
    W,
    A,
    S,
    D,
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    RenderDebugCycle,
}
