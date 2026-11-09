using Chasm.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Chasm.Game.Input;

/// <summary>Named player actions. Gameplay asks for these, never for raw keys, so keys can be rebound.</summary>
public enum InputAction
{
    Interact, Cancel, Confirm, Rest, DebugOverlay, DebugCells,
    Quick1, Quick2, Quick3, Quick4, Quick5, Quick6, Quick7, Quick8, Quick9, Quick0,
}

/// <summary>
/// Keyboard and mouse state for the current frame, with the mouse already converted to canvas
/// coordinates. When an editor panel (ImGui) is using the mouse or keyboard, the game side sees
/// "nothing pressed" so clicking a panel doesn't also walk the player.
/// </summary>
public sealed class InputState
{
    private KeyboardState _kb, _prevKb;
    private MouseState _mouse, _prevMouse;

    public Dictionary<InputAction, Keys[]> Bindings { get; } = new()
    {
        [InputAction.Interact] = new[] { Keys.E },
        [InputAction.Cancel] = new[] { Keys.Escape },
        [InputAction.Confirm] = new[] { Keys.Enter, Keys.Space },
        [InputAction.Rest] = new[] { Keys.R },
        [InputAction.DebugOverlay] = new[] { Keys.F3 },
        [InputAction.DebugCells] = new[] { Keys.F4 },
        [InputAction.Quick1] = new[] { Keys.D1 }, [InputAction.Quick2] = new[] { Keys.D2 },
        [InputAction.Quick3] = new[] { Keys.D3 }, [InputAction.Quick4] = new[] { Keys.D4 },
        [InputAction.Quick5] = new[] { Keys.D5 }, [InputAction.Quick6] = new[] { Keys.D6 },
        [InputAction.Quick7] = new[] { Keys.D7 }, [InputAction.Quick8] = new[] { Keys.D8 },
        [InputAction.Quick9] = new[] { Keys.D9 }, [InputAction.Quick0] = new[] { Keys.D0 },
    };

    /// <summary>Set by the tools host when ImGui wants the mouse / keyboard this frame.</summary>
    public bool MouseCapturedByUi { get; set; }
    public bool KeyboardCapturedByUi { get; set; }

    public Vector2 MouseCanvas { get; private set; }
    public Point MouseWindow => _mouse.Position;
    public int ScrollDelta => MouseCapturedByUi ? 0 : _mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

    public void Update(Canvas canvas, bool windowActive)
    {
        _prevKb = _kb;
        _prevMouse = _mouse;
        _kb = windowActive ? Keyboard.GetState() : default;
        _mouse = windowActive ? Mouse.GetState() : default;
        MouseCanvas = canvas.WindowToCanvas(_mouse.Position);
    }

    public bool MouseInCanvas => MouseCanvas.X >= 0 && MouseCanvas.Y >= 0 && MouseCanvas.X < 1920 && MouseCanvas.Y < 1080;

    public bool Pressed(InputAction a) => !KeyboardCapturedByUi && Bindings[a].Any(KeyPressed);
    public bool Held(InputAction a) => !KeyboardCapturedByUi && Bindings[a].Any(k => _kb.IsKeyDown(k));

    public bool KeyPressed(Keys k) => !KeyboardCapturedByUi && _kb.IsKeyDown(k) && _prevKb.IsKeyUp(k);
    public bool KeyHeld(Keys k) => !KeyboardCapturedByUi && _kb.IsKeyDown(k);

    public bool LeftClicked => !MouseCapturedByUi && _mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
    public bool RightClicked => !MouseCapturedByUi && _mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
    public bool LeftHeld => !MouseCapturedByUi && _mouse.LeftButton == ButtonState.Pressed;
    public bool RightHeld => !MouseCapturedByUi && _mouse.RightButton == ButtonState.Pressed;
    public bool LeftReleased => _mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;

    /// <summary>Raw mouse state, for the ImGui bridge.</summary>
    public MouseState RawMouse => _mouse;
    public KeyboardState RawKeyboard => _kb;
}
