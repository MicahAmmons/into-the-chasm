using Chasm.Core.Grid;
using Chasm.Core.World;
using Chasm.Game.Input;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// Ground page: the floor's grounds in two lists. Walkable: the first covers every walkable cell
/// on every tile automatically; others are painted onto cells with Assign. Unwalkable: only where
/// painted (no automatic ground). Each ground can move to the other list.
/// </summary>
public sealed partial class MapEditorScene
{
    private bool _groundVoid;          // false = Walkable tab, true = Unwalkable tab
    private string? _groundBrush;      // ground prefab id being painted, or "-" (no ground)
    private bool _openAddGround;
    private string _groundSearch = "";

    private string GroundSide => _groundVoid ? "void" : "walk";
    private List<FloorGround> GroundList => _groundVoid ? _floor!.GroundUnwalkable : _floor!.GroundWalkable;
    private GroundPrefabDef? GroundOf(string id) => Services.Defs.TryGet<GroundPrefabDef>(id, out var g) ? g : null;

    private void GroundPanel()
    {
        if (_floor is null) { ImGui.TextDisabled("This tile has no floor, so it has no ground lists."); return; }
        SideSwitch("Walkable", "Unwalkable", ref _groundVoid,
            "Ground on walkable cells. The first covers every walkable cell automatically; others are painted onto cells.",
            "Ground on unwalkable cells (ledges, rubble...). Only where you paint it: nothing automatic.");
        if (_groundBrush is not null && (_groundBrush == TileGroundPaint.NoGround ? _groundVoid : GroundList.All(g => g.Prefab != _groundBrush))) _groundBrush = null;
        ImGui.BeginChild("ground");

        Ui.Section(_groundVoid ? "Unwalkable ground on this floor" : "Walkable ground on this floor");
        Ui.Hint(_groundVoid
            ? "Painted onto unwalkable cells only (Assign). Drawn under the cliffs."
            : "The first is drawn automatically on every walkable cell, on every tile. Others are painted onto cells with Assign. Painted textures (Paint texture tool) still draw on top.");
        var list = GroundList;
        int up = -1, down = -1, remove = -1, move = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            var g = GroundOf(e.Prefab);
            ImGui.PushID("fg" + i);
            float rowRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
            if (ImGui.ArrowButton("up", ImGuiDir.Up)) up = i;
            ImGui.SameLine(0, 2);
            if (ImGui.ArrowButton("down", ImGuiDir.Down)) down = i;
            ImGui.SameLine();
            if (g?.Layers.FirstOrDefault(l => l.Enabled) is { } first) { Thumb(first.Texture, 26); ImGui.SameLine(); }
            bool auto = !_groundVoid && i == 0;
            string label = (g?.DisplayName ?? $"missing '{e.Prefab}'") + (auto ? "  (auto)" : "");
            ImGui.Selectable(label + "##sel", false, ImGuiSelectableFlags.None, new Num.Vector2(rowRight - ImGui.GetCursorPosX() - 222, 26));
            if (g is not null && ImGui.IsItemHovered()) ImGui.SetTooltip($"{g.DisplayName}  [{g.Id}]\n{g.Layers.Count} layer(s)");
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), rowRight - 216));
            if (!auto)
            {
                bool painting = _groundBrush == e.Prefab;
                if (Ui.ToggleButton(painting ? "Done" : "Assign", painting, new Num.Vector2(64, 0), "Paint this ground onto cells (click / drag on the map; again to un-paint). Esc when done."))
                    ToggleGroundBrush(painting ? null : e.Prefab);
            }
            else ImGui.Dummy(new Num.Vector2(64, 0));
            ImGui.SameLine(0, 4);
            if (ImGui.Button("Edit", new Num.Vector2(46, 0)) && g is not null) OpenGroundEditor(g, isNew: false);
            ImGui.SameLine(0, 4);
            if (ImGui.Button(_groundVoid ? "To walk" : "To void", new Num.Vector2(62, 0))) move = i;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(_groundVoid ? "Move to the Walkable list." : "Move to the Unwalkable list.");
            ImGui.SameLine(0, 4);
            if (ImGui.Button("x", new Num.Vector2(26, 0))) remove = i;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Take off this floor (the ground stays in the library).");
            ImGui.PopID();
        }
        if (list.Count == 0) ImGui.TextDisabled(_groundVoid ? "No unwalkable ground yet." : "No walkable ground yet: add one below (the first fills every walkable cell).");
        if (up > 0) { (list[up - 1], list[up]) = (list[up], list[up - 1]); Edited(); }
        if (down >= 0 && down < list.Count - 1) { (list[down + 1], list[down]) = (list[down], list[down + 1]); Edited(); }
        if (remove >= 0)
        {
            var id = list[remove].Prefab;
            list.RemoveAt(remove);
            _tile?.GroundPaint.RemoveAll(p => p.Ground == id && p.Side == GroundSide);
            if (_groundBrush == id) _groundBrush = null;
            Edited();
        }
        if (move >= 0)
        {
            var e = list[move];
            list.RemoveAt(move);
            var other = _groundVoid ? _floor.GroundWalkable : _floor.GroundUnwalkable;
            if (other.All(x => x.Prefab != e.Prefab)) other.Add(e);
            if (_groundBrush == e.Prefab) _groundBrush = null;
            Edited();
            Status($"Moved {GroundOf(e.Prefab)?.DisplayName ?? e.Prefab} to the {(_groundVoid ? "Walkable" : "Unwalkable")} list.");
        }

        if (Ui.AccentButton("+ New ground")) StartNewGround();
        ImGui.SameLine();
        if (ImGui.Button("+ Add from library...")) { _openAddGround = true; _groundSearch = ""; }
        AddGroundPopup();

        // ---- this tile
        Ui.Section("This tile");
        var t = _tile!;
        int painted = t.GroundPaint.Where(p => p.Side == GroundSide).Sum(p => p.Cells.Count);
        if (!_groundVoid)
        {
            bool erasing = _groundBrush == TileGroundPaint.NoGround;
            if (Ui.ToggleButton(erasing ? "Done (Esc)" : "No ground here...", erasing, new Num.Vector2(150, 0), "Paint walkable cells that should have no ground drawn (e.g. a painted texture covers them)."))
                ToggleGroundBrush(erasing ? null : TileGroundPaint.NoGround);
            ImGui.SameLine();
        }
        ImGui.BeginDisabled(painted == 0);
        if (ImGui.Button("Clear painted cells")) { t.GroundPaint.RemoveAll(p => p.Side == GroundSide); Edited(); }
        ImGui.EndDisabled();
        Ui.Colored(Ui.Muted, painted == 0
            ? (_groundVoid ? "No unwalkable cells painted on this tile." : "Every walkable cell uses the automatic ground.")
            : $"{painted} cell(s) painted on this tile.");
        if (!_groundVoid && t.PaintRegions.Count > 0)
            Ui.Hint($"This tile also has {t.PaintRegions.Count} painted texture region(s) (Paint texture tool); they draw over the ground.");
        ImGui.EndChild();
    }

    private void ToggleGroundBrush(string? brush)
    {
        ClearMapModes();
        _groundBrush = brush;
        if (brush is not null)
            Status(brush == TileGroundPaint.NoGround ? "Painting 'no ground' cells. Esc when done." : $"Painting {GroundOf(brush)?.DisplayName ?? brush} onto {(_groundVoid ? "unwalkable" : "walkable")} cells. Esc when done.");
    }

    /// <summary>Map input while painting ground onto cells. True = used.</summary>
    private bool UpdateGroundBrush(InputState input)
    {
        if (_groundBrush is not { } brush) return false;
        if (_floor is null || _panel != Panel.Ground) { _groundBrush = null; return false; }
        if (input.Pressed(InputAction.Cancel) && !input.KeyboardCapturedByUi) { _groundBrush = null; Status("Done painting ground."); return true; }
        if (input.LeftReleased || input.RightReleased) _dragMode = null;
        var t = _tile!;
        string side = GroundSide;
        bool wantWalkable = !_groundVoid;
        TileGroundPaint Entry(string id)
        {
            var e = t.GroundPaint.FirstOrDefault(p => p.Side == side && p.Ground == id);
            if (e is null) { e = new TileGroundPaint { Side = side, Ground = id }; t.GroundPaint.Add(e); }
            return e;
        }
        PaintCells(input, c => t.GroundPaint.Any(p => p.Side == side && p.Ground == brush && p.Cells.Contains(c)), (c, v) =>
        {
            if (t.Walkable[c] != wantWalkable) return;   // walkable ground on walkable cells only, and vice versa
            foreach (var p in t.GroundPaint.Where(p => p.Side == side)) p.Cells.Remove(c);
            if (v) Entry(brush).Cells.Add(c);
            t.GroundPaint.RemoveAll(p => p.Cells.Count == 0);
        }, rightPaintsFalse: true);
        return true;
    }

    /// <summary>Painted ground cells on the map (this list), coloured per ground.</summary>
    private void DrawGroundPaint(SpriteBatch b)
    {
        if (_panel != Panel.Ground || _tile is null || _floor is null) return;
        var d = Services.Draw;
        foreach (var p in _tile.GroundPaint.Where(p => p.Side == GroundSide))
        {
            var col = p.Ground == TileGroundPaint.NoGround ? new Color(230, 60, 60) : ColorFor(p.Ground);
            foreach (var c in p.Cells)
            {
                d.DiamondFill(b, IsoGrid.CellCenter(c), 100, 50, col * (_groundBrush is null ? 0.18f : 0.32f));
                d.DiamondOutline(b, IsoGrid.CellCenter(c), 112, 56, col, 2);
            }
        }
    }

    private void AddGroundPopup()
    {
        if (_openAddGround) { ImGui.OpenPopup("Add ground to floor"); _openAddGround = false; }
        CenterNextWindow();
        ImGui.SetNextWindowSize(new Num.Vector2(540, 520), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("Add ground to floor")) return;
        Ui.Colored(Ui.Muted, $"The ground library (adding to the {(_groundVoid ? "Unwalkable" : "Walkable")} list).");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##gs", "search...", ref _groundSearch, 60);
        ImGui.BeginChild("glib", new Num.Vector2(0, -36), ImGuiChildFlags.Borders);
        var have = GroundList.Select(x => x.Prefab).ToHashSet();
        var all = Services.Defs.All<GroundPrefabDef>().Where(g => !have.Contains(g.Id))
            .Where(g => _groundSearch.Length == 0 || g.DisplayName.Contains(_groundSearch, StringComparison.OrdinalIgnoreCase) || g.Category.Contains(_groundSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        if (all.Count == 0) ImGui.TextDisabled("No other grounds. Use + New ground.");
        foreach (var grp in all.GroupBy(g => string.IsNullOrWhiteSpace(g.Category) ? "(no category)" : g.Category).OrderBy(x => x.Key))
        {
            Ui.Section(grp.Key);
            foreach (var g in grp.OrderBy(g => g.DisplayName))
            {
                ImGui.PushID(g.Id);
                if (g.Layers.FirstOrDefault() is { } l) { Thumb(l.Texture, 40); ImGui.SameLine(); }
                if (ImGui.Selectable($"{g.DisplayName}  ({g.Layers.Count} layers)  [{g.Id}]", false, ImGuiSelectableFlags.None, new Num.Vector2(0, 40)))
                {
                    GroundList.Add(new FloorGround { Prefab = g.Id });
                    Edited();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("Close", new Num.Vector2(120, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
