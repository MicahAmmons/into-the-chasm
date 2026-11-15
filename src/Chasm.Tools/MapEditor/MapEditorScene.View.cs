using Chasm.Game.Input;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// Map view: zoom + pan (trackpad friendly), the zoom bar over the map, and Game view.
///
/// Trackpads: Windows sends a pinch as "Ctrl + scroll wheel". Ctrl can drop out for a single
/// event mid-pinch, which used to turn that event into a pan (the view lurched). Now each gesture
/// is locked: once a pinch starts, every scroll event is zoom until the fingers stop for a moment
/// (and the same for two-finger panning). Zoom eases toward its target instead of jumping.
/// </summary>
public sealed partial class MapEditorScene
{
    private bool _panning;

    private enum Gesture { None, Zoom, Pan }
    private Gesture _gesture;
    private float _gestureQuiet;              // seconds since the last scroll event
    private const float GestureEnd = 0.22f;   // a pause this long ends the gesture

    private float? _zoomTarget;
    private Point _zoomAnchor;
    private Vector2? _panTarget;

    // Game view
    private bool _gameView;
    private float _savedZoom = 1f;
    private Vector2 _savedPan;

    private void UpdateView(InputState input, bool ctrl, float dt)
    {
        var canvas = Services.Canvas;
        if (_gameView)
        {
            if (input.Pressed(InputAction.Cancel) && !input.KeyboardCapturedByUi) ToggleGameView();
            AnimateView(dt);
            return;
        }
        bool overCanvas = canvas.Area.Contains(input.MouseWindow) && !input.MouseCapturedByUi;
        int wheel = overCanvas ? input.ScrollDelta : 0, hwheel = overCanvas ? input.HorizontalScrollDelta : 0;
        // Shift + scroll zooms the texture whose handles are on the map (some trackpads send it sideways).
        if (SheetScrollZoom(input, wheel != 0 ? wheel : hwheel)) { wheel = 0; hwheel = 0; }

        _gestureQuiet += dt;
        if (wheel != 0 || hwheel != 0)
        {
            // Pinch (Ctrl) always wins; otherwise keep whatever this gesture started as.
            if (ctrl) _gesture = Gesture.Zoom;
            else if (_gesture == Gesture.None || _gestureQuiet > GestureEnd) _gesture = Gesture.Pan;
            _gestureQuiet = 0;

            if (_gesture == Gesture.Zoom)
            {
                if (wheel != 0)
                {
                    float from = _zoomTarget ?? canvas.Zoom;
                    _zoomTarget = Math.Clamp(from * MathF.Pow(1.0018f, wheel), 0.2f, 8f);
                    _zoomAnchor = input.MouseWindow;
                    _panTarget = null;
                }
            }
            else
            {
                _panTarget = null;
                canvas.Pan += new Vector2(-hwheel * 0.6f, wheel * 0.6f);
            }
        }
        else if (_gestureQuiet > GestureEnd) _gesture = Gesture.None;

        bool spaceDrag = input.KeyHeld(Keys.Space) && input.LeftHeld;
        _panning = (spaceDrag || input.MiddleHeld) && (overCanvas || _panning);
        if (_panning) { canvas.Pan += input.MouseDelta.ToVector2(); _panTarget = null; }
        if (!input.KeyboardCapturedByUi)
        {
            if (input.KeyPressed(Keys.OemPlus) || input.KeyPressed(Keys.Add)) ZoomBy(1.25f);
            if (input.KeyPressed(Keys.OemMinus) || input.KeyPressed(Keys.Subtract)) ZoomBy(1 / 1.25f);
            if (input.KeyPressed(Keys.F)) FitView();
            if (input.KeyPressed(Keys.G) && !ctrl) ToggleGameView();
        }
        AnimateView(dt);
    }

    /// <summary>Eases zoom (around its anchor) and pan toward their targets.</summary>
    private void AnimateView(float dt)
    {
        var canvas = Services.Canvas;
        float k = 1f - MathF.Exp(-dt * 18f);
        if (_zoomTarget is float zt)
        {
            float z = canvas.Zoom + (zt - canvas.Zoom) * k;
            if (MathF.Abs(zt - z) < 0.0015f) { z = zt; _zoomTarget = null; }
            canvas.ZoomAround(_zoomAnchor, z);
        }
        if (_panTarget is Vector2 pt)
        {
            var p = canvas.Pan + (pt - canvas.Pan) * k;
            if (Vector2.Distance(p, pt) < 0.5f) { p = pt; _panTarget = null; }
            canvas.Pan = p;
            canvas.UpdateDestination();
        }
    }

    private void ZoomBy(float factor)
    {
        var canvas = Services.Canvas;
        _zoomTarget = Math.Clamp((_zoomTarget ?? canvas.Zoom) * factor, 0.2f, 8f);
        _zoomAnchor = canvas.Area.Center;
    }

    /// <summary>Slides the tile back to the middle of the map area (keeps the zoom).</summary>
    private void CenterView()
    {
        _zoomTarget = null;
        _panTarget = Vector2.Zero;
    }

    /// <summary>100% and centred: the whole tile fits the map area.</summary>
    private void FitView()
    {
        _panTarget = null;
        _zoomTarget = null;
        Services.Canvas.ResetView();
    }

    /// <summary>Game view: art only, filling the window (no panels, overlays or grid). Toggle again to go back.</summary>
    private void ToggleGameView()
    {
        var canvas = Services.Canvas;
        _zoomTarget = null; _panTarget = null; _gesture = Gesture.None;
        if (!_gameView)
        {
            _savedZoom = canvas.Zoom;
            _savedPan = canvas.Pan;
            _gameView = true;
            _cutOutMaterial = null; _stencilRegionId = null; _sheetMaterial = null; _panning = false;
            canvas.ResetView();
            Status("Game view. Press G, Esc or the button to go back.");
        }
        else
        {
            _gameView = false;
            canvas.Zoom = _savedZoom;
            canvas.Pan = _savedPan;
            Status("Back to editing.");
        }
        canvas.UpdateDestination();
    }

    /// <summary>The small bar over the top of the map: zoom slider, centre, fit, game view.</summary>
    private void ZoomBar()
    {
        var io = ImGui.GetIO();
        var canvas = Services.Canvas;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Num.Vector2(8, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8);
        ImGui.SetNextWindowBgAlpha(_gameView ? 0.55f : 0.88f);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove
                    | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing;
        if (_gameView)
        {
            // Only the toggle stays, top-right, so you can always get back.
            ImGui.SetNextWindowPos(new Num.Vector2(io.DisplaySize.X - 12, 12), ImGuiCond.Always, new Num.Vector2(1, 0));
            ImGui.Begin("##zoombar", flags);
            if (Ui.ToggleButton("Game view", true, new Num.Vector2(120, 0), "Back to the editor at the zoom you had (G or Esc).")) ToggleGameView();
            ImGui.End();
            ImGui.PopStyleVar(2);
            return;
        }
        var area = canvas.Area;
        ImGui.SetNextWindowPos(new Num.Vector2(area.Center.X, area.Y + 10), ImGuiCond.Always, new Num.Vector2(0.5f, 0));
        ImGui.Begin("##zoombar", flags);
        if (ImGui.Button("-", new Num.Vector2(28, 0))) ZoomBy(1 / 1.25f);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Zoom out (-)");
        ImGui.SameLine(0, 4);
        float pct = (_zoomTarget ?? canvas.Zoom) * 100f;
        ImGui.SetNextItemWidth(230);
        if (ImGui.SliderFloat("##zoom", ref pct, 20, 800, "%.0f%%", ImGuiSliderFlags.Logarithmic | ImGuiSliderFlags.AlwaysClamp))
        {
            _zoomTarget = null;
            canvas.ZoomAround(area.Center, pct / 100f);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drag to zoom, or Ctrl+click to type a percentage.");
        ImGui.SameLine(0, 4);
        if (ImGui.Button("+", new Num.Vector2(28, 0))) ZoomBy(1.25f);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Zoom in (+)");
        ImGui.SameLine(0, 12);
        if (ImGui.Button("Center")) CenterView();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Slide the tile back to the middle (keeps the zoom).");
        ImGui.SameLine(0, 4);
        if (ImGui.Button("Fit")) FitView();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("100%, centred: the whole tile fits (F).");
        ImGui.SameLine(0, 12);
        if (Ui.ToggleButton("Game view", false, new Num.Vector2(110, 0), "See the tile as it looks in the game: art only, full screen, no grid or overlays (G).")) ToggleGameView();
        ImGui.End();
        ImGui.PopStyleVar(2);
    }
}
