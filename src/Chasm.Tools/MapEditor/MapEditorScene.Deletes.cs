using Chasm.Core.World;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>Delete flows for things made in the editor: prefabs, walls and floors (each asks first).</summary>
public sealed partial class MapEditorScene
{
    // ------------------------------------------------------------------ prefabs

    private PrefabDef? _deletePrefabTarget;

    private void AskDeletePrefab(PrefabDef p) { _deletePrefabTarget = p; _openDeletePrefab = true; }

    private void DeletePrefabPopup()
    {
        if (_openDeletePrefab) { ImGui.OpenPopup("Delete prefab?"); _openDeletePrefab = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Delete prefab?", ImGuiWindowFlags.AlwaysAutoResize)) return;
        var p = _deletePrefabTarget!;
        var uses = FindReferences(p.Id);
        Ui.Title($"Delete \"{p.DisplayName}\"?");
        Ui.Colored(Ui.Muted, $"content/defs/prefabs/{p.Id}.json");
        if (uses.Count == 0) Ui.Colored(Ui.Good, "Nothing uses it.");
        else
        {
            Ui.Colored(Ui.Warn, $"Used by {uses.Count} thing(s):");
            foreach (var u in uses.Take(10)) ImGui.BulletText(u);
            Ui.Hint("It's taken off every floor's list. Pieces already placed on tiles show as missing until removed.");
        }
        if (DangerButton("Delete prefab"))
        {
            foreach (var f in Services.Defs.All<FloorDef>().ToList())
            {
                if (_floor is not null && f.Id == _floor.Id) continue;
                if (f.Prefabs.Remove(p.Id)) Services.Defs.Save(f);
            }
            if (_floor is not null && _floor.Prefabs.Remove(p.Id)) Edited();
            Services.Defs.Delete(typeof(PrefabDef), p.Id);
            if (_prefab?.Id == p.Id) { _prefab = null; if (_panel == Panel.Prefab) _panel = Panel.Walls; }
            Status($"Deleted prefab {p.Id}.");
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(120, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    // ------------------------------------------------------------------ walls

    private WallDef? _deleteWallTarget;
    private bool _openDeleteWall;

    private void AskDeleteWall(WallDef w) { _deleteWallTarget = w; _openDeleteWall = true; }

    private void DeleteWallPopup()
    {
        if (_openDeleteWall) { ImGui.OpenPopup("Delete wall?"); _openDeleteWall = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Delete wall?", ImGuiWindowFlags.AlwaysAutoResize)) return;
        var w = _deleteWallTarget!;
        var uses = FindReferences(w.Id);
        Ui.Title($"Delete \"{w.DisplayName}\"?");
        Ui.Colored(Ui.Muted, $"content/defs/walls/{w.Id}.json  (its pictures stay in the art library)");
        if (uses.Count == 0) Ui.Colored(Ui.Good, "Nothing uses it.");
        else
        {
            Ui.Colored(Ui.Warn, $"Used by {uses.Count} thing(s); it's taken off every floor's list and cells painted with it go back to the default:");
            foreach (var u in uses.Take(10)) ImGui.BulletText(u);
        }
        if (DangerButton("Delete wall"))
        {
            static bool Strip(FloorDef f, string id) => f.WallsDown.RemoveAll(x => x.Wall == id) + f.WallsUp.RemoveAll(x => x.Wall == id) > 0;
            foreach (var f in Services.Defs.All<FloorDef>().ToList())
            {
                if (_floor is not null && f.Id == _floor.Id) continue;
                if (Strip(f, w.Id)) Services.Defs.Save(f);
            }
            if (_floor is not null && Strip(_floor, w.Id)) Edited();
            if (_tile is not null && _tile.WallPaint.RemoveAll(x => x.Wall == w.Id) > 0) Edited();
            Services.Defs.Delete(typeof(WallDef), w.Id);
            if (_wall?.Id == w.Id) { _wall = null; if (_panel == Panel.WallEdit) _panel = Panel.Walls; }
            if (_wallBrush == w.Id) _wallBrush = null;
            Services.TileVisuals.InvalidateAll();
            Status($"Deleted wall {w.Id}.");
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(120, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    // ------------------------------------------------------------------ floors

    private bool _openDeleteFloor;

    private void DeleteFloorPopup()
    {
        if (_openDeleteFloor) { ImGui.OpenPopup("Delete floor?"); _openDeleteFloor = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Delete floor?", ImGuiWindowFlags.AlwaysAutoResize)) return;
        var f = _floor;
        if (f is null) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        var others = Services.Defs.All<TileDef>().Where(t => t.Floor == f.Number && t.Id != _tile?.Id).ToList();
        Ui.Title($"Delete floor {f.Number} ({f.DisplayName})?");
        Ui.Colored(Ui.Muted, $"content/defs/floors/{f.Id}.json  -  its textures, wall lists and clouds go with it.");
        if (others.Count > 0)
        {
            Ui.Colored(Ui.Warn, $"{others.Count} other tile(s) are on this floor. Move them to another floor or delete them first:");
            foreach (var t in others.Take(12)) ImGui.BulletText($"{t.DisplayName} [{t.Id}]");
        }
        else Ui.Hint("This tile becomes a sketch (no floor). Walls and prefabs stay in the library.");
        ImGui.BeginDisabled(others.Count > 0);
        bool del = DangerButton("Delete floor");
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(120, 0))) ImGui.CloseCurrentPopup();
        if (del)
        {
            string id = f.Id;
            int number = f.Number;
            SetFloor(null);
            Services.Defs.Delete(typeof(FloorDef), id);
            _savedFloorJson = "";
            if (_panel is Panel.FloorProperties or Panel.FloorTextures or Panel.Walls or Panel.Clouds or Panel.WallEdit) _panel = Panel.Selection;
            Edited();
            Status($"Deleted floor {number}. Save the tile to keep it as a sketch.");
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private static bool DangerButton(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.55f, 0.16f, 0.16f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Num.Vector4(0.72f, 0.2f, 0.2f, 1));
        bool b = ImGui.Button(label, new Num.Vector2(150, 0));
        ImGui.PopStyleColor(2);
        return b;
    }
}
