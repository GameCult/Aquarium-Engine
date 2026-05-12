using System.Numerics;

namespace Aquarium.Engine.Input;

public sealed class InputState
{
    private readonly HashSet<KeyCode> downKeys = [];
    private readonly HashSet<KeyCode> pressedKeys = [];
    private readonly HashSet<MouseButton> downMouseButtons = [];
    private readonly HashSet<MouseButton> pressedMouseButtons = [];
    private readonly List<char> textInput = [];

    public Vector2 MousePosition { get; private set; }

    public Vector2 MouseDelta { get; private set; }

    public float WheelDelta { get; private set; }

    public IReadOnlyList<char> TextInput => textInput;

    public bool LeftMouseDown => IsMouseDown(MouseButton.Left);

    public bool MiddleMouseDown => IsMouseDown(MouseButton.Middle);

    public bool RightMouseDown => IsMouseDown(MouseButton.Right);

    public bool IsKeyDown(KeyCode key) => downKeys.Contains(key);

    public bool IsKeyPressed(KeyCode key) => pressedKeys.Contains(key);

    public bool IsMouseDown(MouseButton button) => downMouseButtons.Contains(button);

    public bool IsMousePressed(MouseButton button) => pressedMouseButtons.Contains(button);

    public void BeginFrame()
    {
        MouseDelta = Vector2.Zero;
        WheelDelta = 0.0f;
        pressedKeys.Clear();
        pressedMouseButtons.Clear();
        textInput.Clear();
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
        if (isDown)
        {
            if (!downMouseButtons.Contains(button))
            {
                pressedMouseButtons.Add(button);
            }

            downMouseButtons.Add(button);
        }
        else
        {
            downMouseButtons.Remove(button);
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

    public void AddTextInput(char value)
    {
        textInput.Add(value);
    }
}

public enum MouseButton
{
    Left,
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
    Digit7,
    Digit8,
    Digit9,
    RenderDebugCycle,
    DebugUiToggle,
    Backspace,
    Enter,
}
