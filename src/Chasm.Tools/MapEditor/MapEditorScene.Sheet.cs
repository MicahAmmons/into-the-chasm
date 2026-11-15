using Chasm.Core.World;
using Chasm.Game.Input;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// A material's texture sheet: position, zoom and perspective warp (corner pin), with handles on
/// the map. Kept in one place on purpose - this is expected to be reworked.
/// </summary>
public sealed partial class MapEditorScene
{
    /// <summary>Material reference ("id" / "local:id") whose sheet handles are on the map.</summary>
    private string? _sheetMaterial;
    private int _sheetHandle = -1;              // 0-3 corners, 4 = move (centre)
    private Num.Vector2 _sheetDragStart, _sheetStartValue;
    private readonly Dictionary<string, (float taper, float squash)> _taperUi = new();

    private void SheetFields(MaterialDef m, string reference)
    {
        const float label = 120;
        bool changed = false;
        var tex = Services.Textures.Get(m.Texture);
        Ui.Section("Position, zoom and perspective");

        ImGui.TextUnformatted("Zoom"); ImGui.SameLine(label);
        float zoom = m.Scale;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 50);
        if (ImGui.SliderFloat("##mzoom", ref zoom, 0.1f, 4f, "%.2fx", ImGuiSliderFlags.Logarithmic)) { m.Scale = zoom; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Size of the texture. With handles on the map, Shift + scroll also zooms.");
        ImGui.SameLine(0, 4);
        if (ImGui.Button("1:1##mz")) { m.Scale = 1; changed = true; }

        ImGui.TextUnformatted("Position"); ImGui.SameLine(label);
        var off = new Num.Vector2(m.OffsetX, m.OffsetY);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 64);
        if (ImGui.DragFloat2("##moff", ref off, 1f, -4000, 4000, "%.0f px")) { m.OffsetX = off.X; m.OffsetY = off.Y; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pixels from the middle of the screen. Drag, or use the move handle on the map.");
        ImGui.SameLine(0, 4);
        if (ImGui.Button("Center##mo")) { m.OffsetX = m.OffsetY = 0; changed = true; }

        bool warp = m.Warp is not null;
        if (ImGui.Checkbox("Perspective warp (corner pin)", ref warp))
        {
            m.Warp = warp ? new MaterialWarp() : null;
            if (warp) { _sheetMaterial = reference; _cutOutMaterial = null; _stencilRegionId = null; }
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pull the corners to pinch the texture toward a vanishing point (like Procreate's Distort).\nWith this on, one copy is drawn instead of repeating - position and zoom it to cover your cells.");
        if (m.Warp is { } w)
        {
            var t = _taperUi.TryGetValue(reference, out var v) ? v : (0f, 0f);
            ImGui.TextUnformatted("Taper"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
            bool tc = ImGui.SliderFloat("##mtaper", ref t.Item1, -0.9f, 0.9f, t.Item1 >= 0 ? "bottom %.2f" : "top %.2f");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pinch the bottom (right) or top (left) edge toward the middle.\nOverwrites the corners; fine-tune with the handles after.");
            ImGui.TextUnformatted("Squash"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
            tc |= ImGui.SliderFloat("##msquash", ref t.Item2, 0f, 0.8f, "%.2f");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pull the pinched edge in, so the far end looks further away.");
            if (tc)
            {
                _taperUi[reference] = t;
                m.Warp = MaterialWarp.Taper(t.Item1, t.Item2, tex.Width * m.Scale, tex.Height * m.Scale);
                changed = true;
            }
            if (ImGui.Button("Reset corners")) { m.Warp = new MaterialWarp(); _taperUi.Remove(reference); changed = true; }
            ImGui.SameLine();
            Ui.Colored(Ui.Muted, "Empty corners show nothing: move / zoom to cover.");
        }
        bool editing = _sheetMaterial == reference;
        if (Ui.ToggleButton(editing ? "Done with handles (Esc)" : "Edit on map (handles)", editing, new Num.Vector2(220, 0),
                m.Warp is null ? "Drag the middle handle to move the texture. Shift + scroll zooms."
                               : "Drag a corner to pin it, the middle handle to move. Shift + scroll zooms."))
        {
            _sheetMaterial = editing ? null : reference;
            if (!editing) { _cutOutMaterial = null; _stencilRegionId = null; }
        }

        if (changed)
        {
            Edited();
            if (!reference.StartsWith("local:")) Services.TileVisuals.InvalidateAll();
        }
    }

    /// <summary>Handle dragging while a sheet is being edited. Returns true when it used the input.</summary>
    private bool UpdateSheetHandles(InputState input)
    {
        if (_sheetMaterial is not { } reference) return false;
        var m = _tile!.FindMaterial(reference, _floor);
        if (m is null) { _sheetMaterial = null; return false; }
        if (input.Pressed(InputAction.Cancel) && !input.KeyboardCapturedByUi) { _sheetMaterial = null; _sheetHandle = -1; Status("Done editing the texture."); return true; }
        var tex = Services.Textures.Get(m.Texture);
        var corners = MaterialSheet.Corners(m, tex.Width, tex.Height);
        var centre = MaterialSheet.Centre(m);
        float pick = 16f / CanvasScale();

        if (input.LeftClicked && !input.MouseCapturedByUi)
        {
            _sheetHandle = -1;
            if (Num.Vector2.Distance(_hoverPx, centre) <= pick * 1.3f) _sheetHandle = 4;
            else if (m.Warp is not null)
                for (int i = 0; i < 4; i++) if (Num.Vector2.Distance(_hoverPx, corners[i]) <= pick) { _sheetHandle = i; break; }
            if (_sheetHandle >= 0)
            {
                _sheetDragStart = _hoverPx;
                _sheetStartValue = _sheetHandle == 4 ? new Num.Vector2(m.OffsetX, m.OffsetY) : m.Warp!.Get(_sheetHandle);
            }
        }
        else if (input.LeftHeld && _sheetHandle >= 0)
        {
            var delta = _hoverPx - _sheetDragStart;
            if (_sheetHandle == 4) { m.OffsetX = MathF.Round(_sheetStartValue.X + delta.X); m.OffsetY = MathF.Round(_sheetStartValue.Y + delta.Y); }
            else m.Warp!.Set(_sheetHandle, _sheetStartValue + MaterialSheet.ToLocalDelta(m, delta));
            Edited();
            if (!reference.StartsWith("local:")) Services.TileVisuals.InvalidateAll();
        }
        if (input.LeftReleased) _sheetHandle = -1;
        if (_sheetHandle < 0 && !input.MouseCapturedByUi)
        {
            bool overHandle = Num.Vector2.Distance(_hoverPx, centre) <= pick * 1.3f
                || (m.Warp is not null && corners.Any(c => Num.Vector2.Distance(_hoverPx, c) <= pick));
            if (overHandle) ImGui.SetTooltip("Drag");
        }
        return true;
    }

    /// <summary>Shift + scroll while a sheet is being edited: zoom the texture. Returns true when it did.</summary>
    private bool SheetScrollZoom(InputState input, int wheel)
    {
        if (_sheetMaterial is not { } reference || wheel == 0) return false;
        if (!(input.KeyHeld(Keys.LeftShift) || input.KeyHeld(Keys.RightShift))) return false;
        var m = _tile!.FindMaterial(reference, _floor);
        if (m is null) return false;
        m.Scale = Math.Clamp(m.Scale * MathF.Pow(1.0015f, wheel), 0.1f, 4f);
        Edited();
        if (!reference.StartsWith("local:")) Services.TileVisuals.InvalidateAll();
        return true;
    }

    private float CanvasScale() => Services.Canvas.Destination.Width / 1920f;

    private void DrawSheetHandles(SpriteBatch b)
    {
        if (_sheetMaterial is not { } reference || _tile is null) return;
        var m = _tile.FindMaterial(reference, _floor);
        if (m is null) return;
        var d = Services.Draw;
        var tex = Services.Textures.Get(m.Texture);
        var c = MaterialSheet.Corners(m, tex.Width, tex.Height);
        float px = 1f / CanvasScale();
        var col = new Color(255, 200, 80);
        for (int i = 0; i < 4; i++)
            d.Line(b, ToXna(c[i]), ToXna(c[(i + 1) % 4]), m.Warp is null ? col * 0.5f : col, 2 * px);
        if (m.Warp is not null)
            foreach (var p in c)
            {
                float s = 9 * px;
                d.Rect(b, new Rectangle((int)(p.X - s), (int)(p.Y - s), (int)(s * 2), (int)(s * 2)), Color.Black * 0.6f);
                d.RectOutline(b, new Rectangle((int)(p.X - s), (int)(p.Y - s), (int)(s * 2), (int)(s * 2)), col, (int)MathF.Max(1, 2 * px));
            }
        var ctr = ToXna(MaterialSheet.Centre(m));
        float r = 14 * px;
        d.Line(b, ctr - new Vector2(r, 0), ctr + new Vector2(r, 0), col, 3 * px);
        d.Line(b, ctr - new Vector2(0, r), ctr + new Vector2(0, r), col, 3 * px);
        d.DiamondOutline(b, ctr, r * 2.2f, r * 2.2f, col, 2 * px);
    }

    private static Vector2 ToXna(Num.Vector2 v) => new(v.X, v.Y);
}
