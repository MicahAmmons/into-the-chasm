using Chasm.Core.Serialization;
using Chasm.Tools.Scenes;
using Chasm.Core.World;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

public sealed partial class MapEditorScene
{
    private TileDef? _reference;
    private float _referenceOpacity = 0.5f;
    private bool _referenceOverlay = true;
    private string _refSearch = "";

    private void ReferencePickerPopup()
    {
        if (!ImGui.BeginPopup("pickref")) return;
        ImGui.Text("Show which tile as a reference?");
        ImGui.SetNextItemWidth(300);
        ImGui.InputTextWithHint("##rs", "search...", ref _refSearch, 60);
        ImGui.BeginChild("reflist", new Num.Vector2(320, 360));
        TileList(t => { _reference = t; _panel = Panel.Reference; ImGui.CloseCurrentPopup(); }, _refSearch, exclude: _isNewTile ? null : _tile?.Id);
        ImGui.EndChild();
        ImGui.EndPopup();
    }

    /// <summary>Read-only view of another tile, with "copy to my tile" buttons.</summary>
    private void ReferencePanel()
    {
        if (ImGui.Button(_reference is null ? "Pick a reference tile..." : "Change reference tile...")) ImGui.OpenPopup("pickref");
        ReferencePickerPopup();
        if (_reference is null) { ImGui.TextDisabled("Shows another tile on top of yours (read-only), to match edges or find a texture."); return; }
        if (!Services.Defs.TryGet<TileDef>(_reference.Id, out var r)) { _reference = null; return; }
        _reference = r;
        var refFloor = TileLinks.FloorOf(Services.Defs, r);
        ImGui.SameLine();
        if (ImGui.Button("Close")) { _reference = null; _panel = Panel.Selection; return; }
        Ui.Section($"{r.DisplayName}  [{r.Id}]  (read-only)");
        ImGui.TextUnformatted($"Floor {(r.Floor?.ToString() ?? "-")}   X {(r.X?.ToString() ?? "-")}   Y {(r.Y?.ToString() ?? "-")}");
        ImGui.Checkbox("Show on top of my tile", ref _referenceOverlay);
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderFloat("##op", ref _referenceOpacity, 0f, 1f, "Opacity %.2f");

        var t = _tile!;
        ImGui.BeginChild("ref");
        if (ImGui.CollapsingHeader($"Materials ({(refFloor?.Materials.Count ?? 0) + r.LocalMaterials.Count})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var m in (refFloor?.Materials ?? new()).Select(m => (m, "floor")).Concat(r.LocalMaterials.Select(m => (m, "local"))))
            {
                ImGui.PushID(m.Item2 + m.m.Id);
                if (ImGui.SmallButton("Copy as local")) { CopyMaterialLocal(m.m); }
                if (_floor is not null) { ImGui.SameLine(); if (ImGui.SmallButton("Copy to my floor")) CopyMaterialToFloor(m.m); }
                ImGui.SameLine();
                ImGui.TextUnformatted($"{m.m.Name} ({m.Item2})  {m.m.Texture}");
                if (ImGui.IsItemHovered()) MaterialTooltip(m.m);
                ImGui.PopID();
            }
        }
        if (ImGui.CollapsingHeader($"Paint regions ({r.PaintRegions.Count})"))
        {
            foreach (var reg in r.PaintRegions)
            {
                ImGui.PushID(reg.Id);
                if (ImGui.SmallButton("Copy")) CopyRegion(reg, r, refFloor);
                ImGui.SameLine();
                ImGui.TextUnformatted($"{reg.Id}: {r.FindMaterial(reg.Material, refFloor)?.Name ?? reg.Material}  {reg.Cells.Count} cells");
                ImGui.PopID();
            }
        }
        if (ImGui.CollapsingHeader($"Props ({r.Props.Count})"))
        {
            foreach (var p in r.Props)
            {
                ImGui.PushID(p.Id);
                if (ImGui.SmallButton("Copy")) { var c = ChasmJson.Clone(p); c.Id = NextId("prop", t.Props.Select(x => x.Id)); t.Props.Add(c); Edited(); }
                ImGui.SameLine();
                ImGui.TextUnformatted($"{p.Id}: {p.Texture}  ({p.X:0},{p.Y:0})");
                if (ImGui.IsItemHovered()) ImageTooltip(p.Texture);
                ImGui.PopID();
            }
        }
        if (ImGui.CollapsingHeader($"Objects / interactions ({r.Placements.Count})"))
        {
            foreach (var p in r.Placements)
            {
                ImGui.PushID(p.Id);
                if (ImGui.SmallButton("Copy")) { var c = ChasmJson.Clone(p); c.Id = NextId(p.Id.Split('_')[0], t.Placements.Select(x => x.Id)); t.Placements.Add(c); Edited(); }
                ImGui.SameLine();
                ImGui.TextUnformatted($"{p.Id} {p.Cell}: {p.Describe(Services.Defs)}");
                ImGui.PopID();
            }
        }
        if (ImGui.CollapsingHeader("Everything (read-only)"))
        {
            ImGui.BeginDisabled();
            var clone = ChasmJson.Clone(r); // inspector may create defaults; never touch the real one
            ToolContext.Wizards.Inspector.Edit(clone);
            ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void CopyMaterialLocal(MaterialDef m)
    {
        var c = ChasmJson.Clone(m);
        c.Id = UniqueMaterialId(_tile!.LocalMaterials, m.Name);
        _tile.LocalMaterials.Add(c);
        Edited();
        Status($"Copied '{m.Name}' into this tile's local materials.");
    }

    private void CopyMaterialToFloor(MaterialDef m)
    {
        var c = ChasmJson.Clone(m);
        c.Id = UniqueMaterialId(_floor!.Materials, m.Name);
        _floor.Materials.Add(c);
        Edited();
        Status($"Copied '{m.Name}' into the floor library as '{c.Id}'.");
    }

    /// <summary>Copies a region; its material comes along as a local material when this tile can't already see it.</summary>
    private void CopyRegion(PaintRegion reg, TileDef from, FloorDef? fromFloor)
    {
        var t = _tile!;
        var copy = ChasmJson.Clone(reg);
        copy.Id = NextId("r", t.PaintRegions.Select(x => x.Id));
        var mat = from.FindMaterial(reg.Material, fromFloor);
        bool sameFloorMaterial = !reg.Material.StartsWith("local:") && fromFloor is not null && _floor is not null && fromFloor.Number == _floor.Number;
        if (mat is not null && !sameFloorMaterial)
        {
            var local = ChasmJson.Clone(mat);
            local.Id = UniqueMaterialId(t.LocalMaterials, mat.Name);
            t.LocalMaterials.Add(local);
            copy.Material = "local:" + local.Id;
        }
        t.PaintRegions.Add(copy);
        _selectedRegion = copy;
        Edited();
        Status($"Copied region as {copy.Id}.");
    }
}
