using Chasm.Core.World;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// Auto cliffs (Walls page): the floor's kit (which prefab goes on left / right faces and corners)
/// and Generate / Remove for this tile. Plus the delete flows for prefabs and floors.
/// </summary>
public sealed partial class MapEditorScene
{
    private void AutoCliffsSection(MaterialKind kind)
    {
        var floor = _floor!;
        bool up = kind == MaterialKind.WallUp;
        var kit = up ? floor.CliffKitUp : floor.CliffKitDown;
        Ui.Section(up ? "Auto walls (kit)" : "Auto cliffs (kit)");
        Ui.Hint(up
            ? "Puts walls along every upper edge of the walkable cells. Pick prefabs for the floor's kit, then Generate on each tile."
            : "Hangs cliffs under every edge of the walkable cells that drops into the void. Pick prefabs for the floor's kit, then Generate on each tile.");

        var options = floor.Prefabs.Select(id => Services.Defs.TryGet<PrefabDef>(id, out var p) ? p : null)
            .Where(p => p is not null && p.Kind == kind).Cast<PrefabDef>().ToList();
        bool changed = false;
        changed |= KitSlot("Left face", kit.LeftFace, v => kit.LeftFace = v, options, required: true,
            up ? "Walls along the upper-left edges." : "Cliffs under the lower-left edges (facing down-left).");
        changed |= KitSlot("Right face", kit.RightFace, v => kit.RightFace = v, options, required: false,
            "Empty = the left face, mirrored.");
        changed |= KitSlot("Outer corner", kit.OuterCorner, v => kit.OuterCorner = v, options, required: false,
            "Where a left and right face meet at a point sticking out. Empty = faces just meet.");
        changed |= KitSlot("Inner corner", kit.InnerCorner, v => kit.InnerCorner = v, options, required: false,
            "Where faces meet in a notch going in. Empty = faces just overlap there.");

        const float label = 120;
        int depth = kit.Depth, run = kit.MaxRun;
        ImGui.TextUnformatted("Depth"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderInt("##kdepth", ref depth, 1, 10, "%d cells")) { kit.Depth = depth; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("How many cells each face reaches down (up). The fade usually hides the end.");
        ImGui.TextUnformatted("Piece length"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderInt("##krun", ref run, 1, 24, "up to %d cells")) { kit.MaxRun = run; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A straight edge longer than this is split into several pieces (so the art never runs out).");
        bool skew = kit.SkewFaces;
        if (ImGui.Checkbox("Slant faces along the cell edges", ref skew)) { kit.SkewFaces = skew; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Isometric look: the face follows the diagonal edge while its vertical lines stay vertical.\nTurn off if your prefab art is already drawn on the diagonal.");
        if (changed) Edited();

        var t = _tile!;
        string tag = up ? AutoCliffs.TagUp : AutoCliffs.TagDown;
        int existing = t.PaintRegions.Count(r => r.Auto == tag);
        bool ready = Services.Defs.Exists(typeof(PrefabDef), kit.LeftFace);
        ImGui.BeginDisabled(!ready);
        if (Ui.AccentButton(existing > 0 ? "Regenerate on this tile" : "Generate on this tile")) GenerateAutoCliffs(kit, up);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(ready ? "Replaces this side's auto pieces on this tile (pieces you've kept are left alone)." : "Pick a Left face prefab first.");
        ImGui.SameLine();
        ImGui.BeginDisabled(existing == 0);
        if (ImGui.Button("Remove auto pieces")) { t.PaintRegions.RemoveAll(r => r.Auto == tag); Edited(); Status("Removed the auto pieces."); }
        ImGui.EndDisabled();
        Ui.Colored(Ui.Muted, existing == 0 ? "No auto pieces on this tile." : $"{existing} auto piece(s) on this tile (listed below, tagged [auto]).");
    }

    /// <summary>A kit slot: dropdown of this floor's prefabs (of the right kind), with a clear button.</summary>
    private bool KitSlot(string name, string value, Action<string> set, List<PrefabDef> options, bool required, string tip)
    {
        bool changed = false;
        ImGui.PushID("kit_" + name);
        ImGui.TextUnformatted(name); ImGui.SameLine(120);
        var current = options.FirstOrDefault(p => p.Id == value);
        string preview = current?.DisplayName ?? (string.IsNullOrEmpty(value) ? (required ? "(pick one)" : "(none)") : $"missing '{value}'");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 32);
        if (ImGui.BeginCombo("##slot", preview))
        {
            if (!required && ImGui.Selectable("(none)", string.IsNullOrEmpty(value))) { set(""); changed = true; }
            foreach (var p in options)
            {
                if (ImGui.Selectable($"{p.DisplayName}  [{p.Id}]", p.Id == value)) { set(p.Id); changed = true; }
                if (ImGui.IsItemHovered()) PrefabTooltip(p);
            }
            if (options.Count == 0) ImGui.TextDisabled("Add prefabs to this floor first (above).");
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
        ImGui.SameLine(0, 4);
        ImGui.BeginDisabled(string.IsNullOrEmpty(value));
        if (ImGui.Button("x", new Num.Vector2(28, 0))) { set(""); changed = true; }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Clear this slot");
        ImGui.PopID();
        return changed;
    }

    private void GenerateAutoCliffs(CliffKit kit, bool up)
    {
        var t = _tile!;
        string tag = up ? AutoCliffs.TagUp : AutoCliffs.TagDown;
        t.PaintRegions.RemoveAll(r => r.Auto == tag);
        var taken = t.PaintRegions.Select(r => r.Id).ToList();
        var pieces = AutoCliffs.Generate(t, kit, up,
            id => Services.Defs.TryGet<PrefabDef>(id, out var p) ? p : null,
            made => NextId("r", taken.Concat(made)));
        // Behind everything else painted on the tile (walkways draw over the cliff tops).
        t.PaintRegions.InsertRange(0, pieces);
        ClearMapModes();
        Edited();
        Status(pieces.Count == 0
            ? "Nothing to generate: no walkable edges drop into empty cells here."
            : $"Generated {pieces.Count} piece(s). Adjust any of them below, or press Keep to stop it being replaced.");
    }

    // ------------------------------------------------------------------ deleting prefabs

    private PrefabDef? _deletePrefabTarget;

    private bool _deleteWholeSet = true;

    private void AskDeletePrefab(PrefabDef p) { _deletePrefabTarget = p; _openDeletePrefab = true; _deleteWholeSet = p.IsSetLeader; }

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
            Ui.Hint("It's taken off every floor's list and kit. Placements already on tiles stay but show as missing until removed.");
        }
        var set = p.InSet ? Services.Defs.All<PrefabDef>().Where(x => x.SetId == p.SetId).ToList() : new List<PrefabDef>();
        if (set.Count > 1)
            ImGui.Checkbox($"Delete the whole set ({set.Count} prefabs: {string.Join(", ", set.Select(x => x.DisplayName))})", ref _deleteWholeSet);
        ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.55f, 0.16f, 0.16f, 1));
        bool del = ImGui.Button("Delete prefab", new Num.Vector2(150, 0));
        ImGui.PopStyleColor();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(120, 0))) ImGui.CloseCurrentPopup();
        if (del)
        {
            if (set.Count > 1 && _deleteWholeSet) foreach (var m in set) DeletePrefab(m.Id);
            else
            {
                DeletePrefab(p.Id);
                // The rest of the set loses its leader: they become ordinary prefabs.
                if (p.IsSetLeader)
                    foreach (var m in set.Where(x => x.Id != p.Id))
                    {
                        var copy = Chasm.Core.Serialization.ChasmJson.Clone(m);
                        copy.SetId = ""; copy.Role = PrefabRole.Single; copy.Shade = 1;
                        Services.Defs.Save(copy);
                    }
            }
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    /// <summary>Deletes a prefab file and takes it off every floor's shortlist and kits.</summary>
    private void DeletePrefab(string id)
    {
        static bool Strip(FloorDef f, string id)
        {
            bool c = f.Prefabs.Remove(id);
            foreach (var kit in new[] { f.CliffKitDown, f.CliffKitUp })
            {
                if (kit.LeftFace == id) { kit.LeftFace = ""; c = true; }
                if (kit.RightFace == id) { kit.RightFace = ""; c = true; }
                if (kit.OuterCorner == id) { kit.OuterCorner = ""; c = true; }
                if (kit.InnerCorner == id) { kit.InnerCorner = ""; c = true; }
            }
            return c;
        }
        foreach (var f in Services.Defs.All<FloorDef>().ToList())
        {
            if (_floor is not null && f.Id == _floor.Id) continue; // the open floor is edited below (saved with the tile)
            if (Strip(f, id)) Services.Defs.Save(f);
        }
        if (_floor is not null && Strip(_floor, id)) Edited();
        Services.Defs.Delete(typeof(PrefabDef), id);
        if (_prefab?.Id == id) { _prefab = null; if (_panel == Panel.Prefab) _panel = Panel.Walls; }
        _deletePrefabTarget = null;
        Status($"Deleted prefab {id}.");
    }

    // ------------------------------------------------------------------ deleting a floor

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
        Ui.Colored(Ui.Muted, $"content/defs/floors/{f.Id}.json  -  its textures, walls, clouds and kits go with it.");
        if (others.Count > 0)
        {
            Ui.Colored(Ui.Warn, $"{others.Count} other tile(s) are on this floor. Move them to another floor or delete them first:");
            foreach (var t in others.Take(12)) ImGui.BulletText($"{t.DisplayName} [{t.Id}]");
        }
        else Ui.Hint("This tile becomes a sketch (no floor). Prefabs stay in the library.");
        ImGui.BeginDisabled(others.Count > 0);
        ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.55f, 0.16f, 0.16f, 1));
        bool del = ImGui.Button("Delete floor", new Num.Vector2(150, 0));
        ImGui.PopStyleColor();
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
            if (_panel is Panel.FloorProperties or Panel.FloorTextures or Panel.Walls or Panel.Clouds) _panel = Panel.Selection;
            Edited();
            Status($"Deleted floor {number}. Save the tile to keep it as a sketch.");
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
