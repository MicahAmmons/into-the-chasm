using System.Reflection;
using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Tools.ImGuiHost;
using Chasm.Tools.Scenes;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

public sealed partial class MapEditorScene
{
    private static readonly Num.Vector4 Warn = new(1f, 0.65f, 0.35f, 1f);
    private static readonly Num.Vector4 Good = new(0.5f, 1f, 0.6f, 1f);

    private void RightPanelGui()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Num.Vector2(io.DisplaySize.X - RightW, MenuH));
        ImGui.SetNextWindowSize(new Num.Vector2(RightW, io.DisplaySize.Y - MenuH - StatusH));
        ImGui.Begin("##right", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);
        PageHeader();
        switch (_panel)
        {
            case Panel.FloorProperties: FloorPanel(); break;
            case Panel.FloorTextures: FloorTexturesPanel(); break;
            case Panel.LowerClouds: CloudPanel(lower: true); break;
            case Panel.UpperClouds: CloudPanel(lower: false); break;
            case Panel.Reference: ReferencePanel(); break;
            case Panel.TileSettings: TileSettingsPanel(); break;
            default:
                switch (_tool)
                {
                    case Tool.PaintTexture: PaintPanel(); break;
                    case Tool.PlaceProp: PropsPanel(); break;
                    case Tool.Walkable or Tool.PlayerStarts or Tool.EnemySpawns: CellToolPanel(); break;
                    default: SelectionPanel(); break;
                }
                break;
        }
        ImGui.End();
    }

    /// <summary>Title of the right panel's current page, with a close button for pages opened from the toolbar.</summary>
    private void PageHeader()
    {
        string title = _panel switch
        {
            Panel.FloorProperties => $"Floor {_floor?.Number} properties",
            Panel.FloorTextures => $"Floor {_floor?.Number} textures",
            Panel.LowerClouds => "Lower clouds",
            Panel.UpperClouds => "Upper clouds",
            Panel.Reference => "Reference tile",
            Panel.TileSettings => "Tile settings",
            _ => _tool switch
            {
                Tool.PaintTexture => "Paint texture",
                Tool.PlaceProp => "Place art",
                Tool.Walkable => "Walkable cells",
                Tool.PlayerStarts => "Player start cells",
                Tool.EnemySpawns => "Enemy spawn cells",
                Tool.Interaction => "Interaction",
                _ => "Selection",
            },
        };
        Ui.Title(title);
        if (_panel != Panel.Selection)
        {
            ImGui.SameLine(ImGui.GetWindowWidth() - 26 - ImGui.GetStyle().WindowPadding.X);
            if (ImGui.Button("x", new Num.Vector2(26, 26))) _panel = Panel.Selection;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Close this page");
        }
        ImGui.Separator();
    }

    private bool Edit(object obj, Func<PropertyInfo, bool>? filter = null)
    {
        bool changed = ToolContext.Wizards.Inspector.Edit(obj, filter);
        if (changed) Edited();
        return changed;
    }

    // ------------------------------------------------------------------ cell tools

    private void CellToolPanel()
    {
        var t = _tile!;
        int walk = IsoGrid.AllCells.Count(c => t.Walkable[c]);
        ImGui.TextWrapped(_tool switch
        {
            Tool.Walkable => "Green = walkable, red = blocked. Click or drag; the first cell you press decides. Right-drag always blocks.",
            Tool.PlayerStarts => "Blue cells: where the player may choose to start a fight. Must be walkable.",
            _ => "Orange cells: where enemies are placed at random when a fight starts. Must be walkable.",
        });
        ImGui.TextUnformatted($"Walkable: {walk}   Player starts: {t.PlayerCombatStarts.Count}   Enemy spawns: {t.EnemyCombatStarts.Count}");
        if (_tool == Tool.Walkable)
        {
            if (ImGui.Button("Make every cell walkable")) { foreach (var c in IsoGrid.AllCells) t.Walkable[c] = true; Edited(); }
            if (ImGui.Button("Block every cell")) { foreach (var c in IsoGrid.AllCells) t.Walkable[c] = false; t.PlayerCombatStarts.Clear(); t.EnemyCombatStarts.Clear(); Edited(); }
            if (ImGui.Button("Pick spawn point...")) ToolContext.Wizards.Inspector.CellPicker?.Invoke(c => { t.Spawn = c; });
            Ui.Colored(Ui.Muted, $"Spawn point (where teleports land): {t.Spawn}");
        }
        else
        {
            var list = _tool == Tool.PlayerStarts ? t.PlayerCombatStarts : t.EnemyCombatStarts;
            if (ImGui.Button("Clear all")) { list.Clear(); Edited(); }
        }
    }

    // ------------------------------------------------------------------ paint texture

    private string? _selectedMaterialRef;

    private void PaintPanel()
    {
        var t = _tile!;
        Ui.Section($"Selected cells: {_selection.Count}");
        if (ImGui.Button("Clear")) _selection.Clear();
        ImGui.SameLine();
        if (ImGui.Button("All walkable")) foreach (var c in IsoGrid.AllCells.Where(c => t.Walkable[c])) _selection.Add(c);
        ImGui.SameLine();
        if (ImGui.Button("All cells")) foreach (var c in IsoGrid.AllCells) _selection.Add(c);
        ImGui.SameLine();
        if (ImGui.Button("Invert")) { var all = IsoGrid.AllCells.Where(c => !_selection.Contains(c)).ToList(); _selection.Clear(); foreach (var c in all) _selection.Add(c); }

        Ui.Section("Paint the selection with...");
        var mats = AllMaterials().ToList();
        if (mats.Count == 0) ImGui.TextColored(Warn, "No materials yet. Add one to the floor library (Floor properties) or as a local material below.");
        foreach (var (reference, mat, scope) in mats)
        {
            ImGui.PushID(reference);
            ImGui.BeginDisabled(_selection.Count == 0);
            if (ImGui.Button("Paint")) PaintSelection(reference);
            ImGui.EndDisabled();
            ImGui.SameLine();
            Thumb(mat.Texture, 24);
            ImGui.SameLine();
            if (ImGui.Selectable($"{mat.Name}  ({scope})", _selectedMaterialRef == reference)) _selectedMaterialRef = reference;
            if (ImGui.IsItemHovered()) MaterialTooltip(mat);
            ImGui.PopID();
        }
        if (ImGui.Button("+ Local material (this tile only)")) AddMaterial(t.LocalMaterials, "local");
        if (_floor is not null) { ImGui.SameLine(); if (ImGui.Button("Edit floor library")) _panel = Panel.FloorTextures; }

        Ui.Section("Regions  (lower in the list = drawn on top)");
        int moveUp = -1, moveDown = -1;
        for (int i = 0; i < t.PaintRegions.Count; i++)
        {
            var r = t.PaintRegions[i];
            ImGui.PushID(r.Id);
            if (ImGui.ArrowButton("up", ImGuiDir.Up)) moveUp = i;
            ImGui.SameLine();
            if (ImGui.ArrowButton("down", ImGuiDir.Down)) moveDown = i;
            ImGui.SameLine();
            var mat = t.FindMaterial(r.Material, _floor);
            string label = $"{r.Id}: {mat?.Name ?? $"missing '{r.Material}'"}  {r.Cells.Count} cells{(r.Layer == PaintLayer.InFrontOfCharacters ? "  [front]" : "")}";
            if (ImGui.Selectable(label, _selectedRegion == r)) _selectedRegion = _selectedRegion == r ? null : r;
            ImGui.PopID();
        }
        if (moveUp > 0) { (t.PaintRegions[moveUp - 1], t.PaintRegions[moveUp]) = (t.PaintRegions[moveUp], t.PaintRegions[moveUp - 1]); Edited(); }
        if (moveDown >= 0 && moveDown < t.PaintRegions.Count - 1) { (t.PaintRegions[moveDown + 1], t.PaintRegions[moveDown]) = (t.PaintRegions[moveDown], t.PaintRegions[moveDown + 1]); Edited(); }

        if (_selectedRegion is { } sel && t.PaintRegions.Contains(sel))
        {
            Ui.Section($"Region {sel.Id}");
            string preview = t.FindMaterial(sel.Material, _floor)?.Name ?? sel.Material;
            ImGui.Text("Material"); ImGui.SameLine(170); ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##mat", preview))
            {
                foreach (var (reference, mat, scope) in mats)
                    if (ImGui.Selectable($"{mat.Name} ({scope})", reference == sel.Material)) { sel.Material = reference; Edited(); }
                ImGui.EndCombo();
            }
            Edit(sel, p => p.Name is nameof(PaintRegion.Layer) or nameof(PaintRegion.Feather) or nameof(PaintRegion.Opacity));
            ImGui.BeginDisabled(_selection.Count == 0);
            if (ImGui.Button("Add selection")) { foreach (var c in _selection) if (!sel.Cells.Contains(c)) sel.Cells.Add(c); Edited(); }
            ImGui.SameLine();
            if (ImGui.Button("Remove selection")) { sel.Cells.RemoveAll(_selection.Contains); Edited(); }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Select its cells")) { _selection.Clear(); foreach (var c in sel.Cells) _selection.Add(c); }
            if (ImGui.Button("Duplicate region")) { var copy = ChasmJson.Clone(sel); copy.Id = NextId("r", t.PaintRegions.Select(x => x.Id)); t.PaintRegions.Add(copy); _selectedRegion = copy; Edited(); }
            ImGui.SameLine();
            if (ImGui.Button("Delete region")) { t.PaintRegions.Remove(sel); _selectedRegion = null; Edited(); }
        }

        if (t.LocalMaterials.Count > 0 || _selectedMaterialRef?.StartsWith("local:") == true)
        {
            Ui.Section("Local materials");
            MaterialsEditor(t.LocalMaterials, isFloor: false);
        }
    }

    private void PaintSelection(string materialRef)
    {
        var t = _tile!;
        var region = new PaintRegion { Id = NextId("r", t.PaintRegions.Select(x => x.Id)), Material = materialRef, Cells = _selection.ToList() };
        t.PaintRegions.Add(region);
        _selectedRegion = region;
        _selection.Clear();
        Edited();
        Status($"Painted {region.Cells.Count} cells as {region.Id}.");
    }

    /// <summary>Every material this tile can paint with: (reference, material, "floor"/"local").</summary>
    private IEnumerable<(string reference, MaterialDef mat, string scope)> AllMaterials()
    {
        if (_floor is not null) foreach (var m in _floor.Materials) yield return (m.Id, m, "floor");
        foreach (var m in _tile!.LocalMaterials) yield return ("local:" + m.Id, m, "local");
    }

    private void MaterialTooltip(MaterialDef m)
    {
        var tex = Services.Textures.Get(m.Texture);
        var id = ImGuiExtension.Instance!.Renderer.BindTexture(tex);
        float scale = Math.Min(1f, 220f / Math.Max(tex.Width, tex.Height));
        ImGui.BeginTooltip();
        ImGui.Image(id, new Num.Vector2(tex.Width * scale, tex.Height * scale));
        ImGui.TextUnformatted($"{m.Texture}\nrotation {m.Rotation:0}  tint {m.Tint}  flip {(m.FlipX ? "X" : "")}{(m.FlipY ? "Y" : "")}  {m.Fade}");
        ImGui.EndTooltip();
    }

    // ------------------------------------------------------------------ materials library

    private string? _editingMaterial;
    private string _newMaterialName = "";
    private List<MaterialDef>? _addingTo;

    private void AddMaterial(List<MaterialDef> list, string scope)
    {
        _addingTo = list;
        _newMaterialName = "";
        ImGui.OpenPopup("newmat");
    }

    private void MaterialsEditor(List<MaterialDef> list, bool isFloor)
    {
        ImGui.PushID(isFloor ? "floormats" : "localmats");
        int remove = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            ImGui.PushID(m.Id);
            bool open = _editingMaterial == (isFloor ? "" : "local:") + m.Id;
            Thumb(m.Texture, 28);
            ImGui.SameLine();
            if (ImGui.Selectable($"{m.Name}  [{m.Id}]", open, ImGuiSelectableFlags.None, new Num.Vector2(0, 28))) _editingMaterial = open ? null : (isFloor ? "" : "local:") + m.Id;
            if (ImGui.IsItemHovered()) MaterialTooltip(m);
            if (open)
            {
                ImGui.Indent();
                MaterialFields(m, isFloor);
                int uses = _tile!.PaintRegions.Count(r => r.Material == (isFloor ? m.Id : "local:" + m.Id));
                Ui.Hint($"Used by {uses} region(s) on this tile{(isFloor ? " (other tiles on the floor may use it too)" : "")}.");
                if (ImGui.Button("Duplicate (e.g. Left -> Right)"))
                {
                    var copy = ChasmJson.Clone(m);
                    copy.Name = m.Name + " copy";
                    copy.Id = UniqueMaterialId(list, copy.Name);
                    list.Insert(i + 1, copy);
                    _editingMaterial = (isFloor ? "" : "local:") + copy.Id;
                    _focusMaterialName = true;
                    _cutOutMaterial = null;
                    Edited();
                    Status($"Duplicated as '{copy.Name}'. Rename it, change what you need, then Save (Ctrl+S).");
                }
                ImGui.SameLine();
                if (ImGui.Button("Delete")) remove = i;
                if (!isFloor && _floor is not null)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Move to floor library"))
                    {
                        var copy = ChasmJson.Clone(m);
                        copy.Id = UniqueMaterialId(_floor.Materials, m.Name);
                        _floor.Materials.Add(copy);
                        foreach (var r in _tile.PaintRegions.Where(r => r.Material == "local:" + m.Id)) r.Material = copy.Id;
                        list.Remove(m);
                        _editingMaterial = null;
                        Edited();
                        Status($"'{m.Name}' is now in the floor library as '{copy.Id}'.");
                        ImGui.PopID(); ImGui.Unindent(); break;
                    }
                }
                ImGui.Unindent();
            }
            ImGui.PopID();
        }
        if (remove >= 0) { list.RemoveAt(remove); _editingMaterial = null; Edited(); }
        if (ImGui.Button(isFloor ? "+ Add material to floor library" : "+ Add local material")) AddMaterial(list, isFloor ? "floor" : "local");
        NewMaterialPopup();
        ImGui.PopID();
    }

    private void NewMaterialPopup()
    {
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("newmat", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar)) return;
        ImGui.Text("New material name (e.g. 'Cliff Left'):");
        ImGui.SetNextItemWidth(260);
        bool enter = ImGui.InputText("##n", ref _newMaterialName, 80, ImGuiInputTextFlags.EnterReturnsTrue);
        if ((ImGui.Button("Create") || enter) && _newMaterialName.Trim().Length > 0 && _addingTo is not null)
        {
            var m = new MaterialDef { Name = _newMaterialName.Trim(), Id = UniqueMaterialId(_addingTo, _newMaterialName) };
            var created = m;
            Art.ArtBrowser.OpenPick(null, k => { created.Texture = k; Edited(); });
            _addingTo.Add(m);
            _editingMaterial = (_addingTo == _floor?.Materials ? "" : "local:") + m.Id;
            Edited();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private static string UniqueMaterialId(List<MaterialDef> list, string name)
    {
        string baseId = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (baseId.Length == 0) baseId = "material";
        string id = baseId;
        for (int n = 2; list.Any(m => m.Id == id); n++) id = $"{baseId}_{n}";
        return id;
    }

    // ------------------------------------------------------------------ floor + clouds

    private void FloorPanel()
    {
        if (_floor is null) { ImGui.TextDisabled("This tile has no floor."); return; }
        ImGui.TextColored(Warn, "Changes here affect EVERY tile on this floor.");
        Ui.Colored(Ui.Muted, $"Saved with the tile (Ctrl+S). File: content/defs/floors/{_floor.Id}.json");
        string name = _floor.Name;
        ImGui.Text("Name"); ImGui.SameLine(170); ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##fname", ref name, 100)) { _floor.Name = name; Edited(); }
        ImGui.BeginChild("floor");
        Edit(_floor, p => p.Name is not (nameof(FloorDef.Materials) or nameof(FloorDef.LowerClouds) or nameof(FloorDef.UpperClouds) or "Id" or "Name"));
        ImGui.Spacing();
        Ui.Hint("Textures (the floor's material library) moved to the Textures button.");
        ImGui.EndChild();
    }

    private void FloorTexturesPanel()
    {
        if (_floor is null) { ImGui.TextDisabled("This tile has no floor."); return; }
        Ui.Hint("Materials here are shared by every tile on this floor. Cut-out cells are saved per tile.");
        ImGui.BeginChild("floortex");
        MaterialsEditor(_floor.Materials, isFloor: true);
        ImGui.EndChild();
    }

    private void CloudPanel(bool lower)
    {
        if (_floor is null) return;
        var cloud = lower ? _floor.LowerClouds : _floor.UpperClouds;
        Ui.Section(lower ? "Lower clouds (cliffs fade down into these)" : "Upper clouds (walls fade up into these)");
        ImGui.TextColored(Warn, "Floor-wide: affects every tile on this floor.");
        ImGui.TextWrapped(lower
            ? "Line: where the cloud starts (0 = top of screen, 1 = bottom). Materials set to 'fade into the lower clouds' are fully visible above the line and gone 'Fade Length' below it."
            : "Mirror image: materials set to 'fade into the upper clouds' disappear going UP past the line.");
        _showCloudLines = true;
        if (Edit(cloud)) Services.TileVisuals.InvalidateAll();
        ImGui.TextDisabled("Placeholder visuals: drifts right and wraps. More FX settings come with the cloud rework.");
    }

    // ------------------------------------------------------------------ props + archive

    private List<string>? _archiveKeys;
    private string _archiveSearch = "", _archiveCategory = "";

    private void PropsPanel()
    {
        var t = _tile!;
        Ui.Section("Art archive");
        _archiveKeys ??= Services.Textures.AllKeys().ToList();
        var categories = _archiveKeys.Select(k => k.Contains('/') ? k[..k.IndexOf('/')] : "(root)").Distinct().OrderBy(c => c).ToList();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##cat", _archiveCategory.Length == 0 ? "All categories" : _archiveCategory))
        {
            if (ImGui.Selectable("All categories", _archiveCategory.Length == 0)) _archiveCategory = "";
            foreach (var c in categories) if (ImGui.Selectable(c, c == _archiveCategory)) _archiveCategory = c;
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.SetNextItemWidth(150);
        ImGui.InputTextWithHint("##s", "search...", ref _archiveSearch, 60);
        ImGui.SameLine();
        if (ImGui.Button("+ Add new...")) Art.ArtBrowser.OpenPick(_propTexture, k => { _propTexture = k; _archiveKeys = null; }, addNew: true);
        ImGui.SameLine();
        if (ImGui.Button("Library...")) Art.ArtBrowser.OpenPick(_propTexture, k => { _propTexture = k; _archiveKeys = null; });
        ImGui.BeginChild("archive", new Num.Vector2(0, 220), ImGuiChildFlags.Borders);
        foreach (var k in _archiveKeys)
        {
            if (_archiveCategory.Length > 0 && !k.StartsWith(_archiveCategory + "/", StringComparison.OrdinalIgnoreCase)) continue;
            if (_archiveSearch.Length > 0 && !k.Contains(_archiveSearch, StringComparison.OrdinalIgnoreCase)) continue;
            if (ImGui.Selectable(k, k == _propTexture)) _propTexture = k;
            if (ImGui.IsItemHovered()) ImageTooltip(k);
        }
        ImGui.EndChild();
        if (_propTexture.Length > 0)
        {
            ImGui.TextUnformatted($"Placing: {_propTexture}");
            ImageInline(_propTexture, 120);
        }
        ImGui.Checkbox("Snap to cell centres", ref _snapProps);
        ImGui.TextDisabled("Click the map to place. Drag a prop to move it. Del removes.");
        if (_selectedProp is { } p && t.Props.Contains(p)) PropEditor(p);
    }

    private void PropEditor(PropDef p)
    {
        Ui.Section($"Prop {p.Id}");
        Edit(p, x => x.Name != nameof(PropDef.Interaction));
        if (p.Interaction is null)
        {
            if (ImGui.Button("Add an interaction to this prop"))
            {
                p.Interaction = new InteractablePlacement { Id = p.Id + "_interaction", Cell = IsoGrid.CellAt(new Num.Vector2(p.X, p.Y)), Label = "Interaction" };
                Edited();
            }
        }
        else
        {
            if (ImGui.CollapsingHeader("Interaction", ImGuiTreeNodeFlags.DefaultOpen)) Edit(p.Interaction);
            if (ImGui.Button("Remove interaction")) { p.Interaction = null; Edited(); }
        }
        if (ImGui.Button("Duplicate prop"))
        {
            var copy = ChasmJson.Clone(p);
            copy.Id = NextId("prop", _tile!.Props.Select(x => x.Id));
            copy.X += 40; copy.Y += 20;
            _tile.Props.Add(copy);
            _selectedProp = copy;
            Edited();
        }
        ImGui.SameLine();
        if (ImGui.Button("Delete prop")) DeleteSelected();
    }

    /// <summary>A small square thumbnail of a texture (a tinted box when the art is missing).</summary>
    private void Thumb(string key, float size)
    {
        var tex = Services.Textures.Get(key);
        var id = ImGuiExtension.Instance!.Renderer.BindTexture(tex);
        float scale = size / Math.Max(tex.Width, tex.Height);
        var p = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(p, p + new Num.Vector2(size, size), ImGui.GetColorU32(ImGuiCol.FrameBg), 4);
        ImGui.Image(id, new Num.Vector2(tex.Width * scale, tex.Height * scale));
    }

    private void ImageTooltip(string key)
    {
        ImGui.BeginTooltip();
        ImageInline(key, 220);
        ImGui.EndTooltip();
    }

    private void ImageInline(string key, float max)
    {
        var tex = Services.Textures.Get(key);
        var id = ImGuiExtension.Instance!.Renderer.BindTexture(tex);
        float scale = Math.Min(1f, max / Math.Max(tex.Width, tex.Height));
        ImGui.Image(id, new Num.Vector2(tex.Width * scale, tex.Height * scale));
        Ui.Colored(Ui.Muted, $"{tex.Width} x {tex.Height}");
    }

    // ------------------------------------------------------------------ selection + tile settings

    private void SelectionPanel()
    {
        var t = _tile!;
        if (_selectedProp is { } prop && t.Props.Contains(prop)) { PropEditor(prop); return; }
        if (_selectedPlacement is { } pl && t.Placements.Contains(pl))
        {
            Ui.Section($"{pl.Id}: {pl.Describe(Services.Defs)}");
            if (ImGui.Button("Delete")) { DeleteSelected(); return; }
            ImGui.SameLine();
            if (ImGui.Button("Duplicate"))
            {
                var copy = ChasmJson.Clone(pl);
                copy.Id = NextId(pl.Id.Split('_')[0], t.Placements.Select(x => x.Id));
                t.Placements.Add(copy); _selectedPlacement = copy; Edited();
            }
            ImGui.BeginChild("pl");
            Edit(pl);
            ImGui.EndChild();
            return;
        }
        if (_selectedCell is not { } cell)
        {
            ImGui.TextWrapped("Select tool: click a cell, prop or object on the map.\n\nUse the tools on the left to paint cells, textures, place art and add interactions.");
            return;
        }
        Ui.Section($"Cell {cell}");
        bool walk = t.Walkable[cell];
        if (ImGui.Checkbox("Walkable", ref walk)) { t.Walkable[cell] = walk; Edited(); }
        ImGui.TextUnformatted($"Player start: {(t.PlayerCombatStarts.Contains(cell) ? "yes" : "no")}   Enemy spawn: {(t.EnemyCombatStarts.Contains(cell) ? "yes" : "no")}");
        var regions = t.PaintRegions.Where(r => r.Cells.Contains(cell)).ToList();
        ImGui.TextUnformatted($"Paint regions here: {(regions.Count == 0 ? "none" : string.Join(", ", regions.Select(r => r.Id)))}");
        Ui.Section("Add to this cell");
        foreach (var type in PolymorphicTypes.ConcreteSubtypes(typeof(Placement)))
        {
            if (ImGui.Button(Editing.Inspector.LabelFor(type)))
            {
                var p = (Placement)Activator.CreateInstance(type)!;
                p.Id = NextId(PolymorphicTypes.TypeId(type).ToLowerInvariant(), t.Placements.Select(x => x.Id));
                p.Cell = cell;
                t.Placements.Add(p);
                _selectedPlacement = p;
                Edited();
            }
        }
        var here = t.Placements.Where(p => p.Cell == cell).ToList();
        if (here.Count > 0)
        {
            Ui.Section("On this cell");
            foreach (var p in here) if (ImGui.Selectable($"{p.Id}: {p.Describe(Services.Defs)}")) _selectedPlacement = p;
        }
    }

    private void TileSettingsPanel()
    {
        var t = _tile!;
        Ui.Section("Tile settings");
        ImGui.TextDisabled("Full-screen art layers (hand-painted tiles), extra exits, local materials, notes.");
        ImGui.BeginChild("ts");
        Edit(t, p => p.Name is not ("Id" or "Name" or nameof(TileDef.Floor) or nameof(TileDef.X) or nameof(TileDef.Y) or nameof(TileDef.LocalMaterials)));
        if (ImGui.CollapsingHeader("Local materials")) MaterialsEditor(t.LocalMaterials, isFloor: false);
        ImGui.EndChild();
    }
}
