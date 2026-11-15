using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Game.Input;
using Chasm.Tools.ImGuiHost;
using Chasm.Tools.Scenes;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// Prefabs: the floor's prefab shortlist and the tile's placements (Walls page), and the prefab
/// editor page (make the asset once: picture, look, perspective, where its lip is).
///
/// Fade is never part of a prefab: each placement uses its floor's wall fade (the floor's "cloud
/// level") unless it turns the fade off or sets its own numbers.
/// </summary>
public sealed partial class MapEditorScene
{
    // Prefab editor (working copy until saved)
    private PrefabDef? _prefab;
    private bool _prefabIsNew;
    private Panel _prefabReturn = Panel.Walls;
    private bool _prefabHandles;
    private int _prefabHandle = -1;
    private Num.Vector2 _prefabDragStart, _prefabStartValue;
    private (float taper, float squash) _prefabTaper;
    private bool _openAddPrefab, _openDeletePrefab;
    private string _prefabSearch = "";

    // Placements
    private string? _openPlacement;     // region id expanded on the Walls page
    private string? _placeCellsRegion;  // region id whose cells the map is picking
    private string? _nudgeRegion;       // region id being moved by dragging on the map
    private Num.Vector2 _nudgeDragStart, _nudgeStart;
    private bool _nudgeDragging;

    private PrefabDef? PrefabOf(PaintRegion r) =>
        r.Prefab is { } id && Services.Defs.TryGet<PrefabDef>(id, out var p) ? p : null;

    private static Num.Vector2 PrefabPreviewAnchor => new(960, 330);

    // ------------------------------------------------------------------ Walls page

    /// <summary>One-off pieces: hand-placed prefabs (rocks, ledges...) on this tile, from the floor's prefab list.</summary>
    private void OneOffPiecesSection(MaterialKind kind)
    {
        // ---- prefabs this floor uses
        Ui.Hint("Single pieces you place by hand (a boulder, a ledge). Walls along edges are automatic (above).");
        Ui.Section("Prefabs on this floor");
        var mine = _floor.Prefabs.Select(id => Services.Defs.TryGet<PrefabDef>(id, out var p) ? p : null)
            .Where(p => p is not null && p.Kind == kind).Cast<PrefabDef>().ToList();
        if (mine.Count == 0) ImGui.TextDisabled("None yet. Add one from the library, or make a new one.");
        string? removeId = null;
        foreach (var p in mine)
        {
            ImGui.PushID("fp_" + p.Id);
            float rowRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
            Thumb(p.Texture, 28);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(p.DisplayName);
            if (ImGui.IsItemHovered()) PrefabTooltip(p);
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), rowRight - 156));
            if (Ui.AccentButton("Place", new Num.Vector2(64, 0))) PlacePrefab(p);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add it to this tile, then pick its cells on the map.");
            ImGui.SameLine(0, 4);
            if (ImGui.Button("Edit", new Num.Vector2(56, 0))) OpenPrefabEditor(p, isNew: false);
            ImGui.SameLine(0, 4);
            if (ImGui.Button("x", new Num.Vector2(28, 0))) removeId = p.Id;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove from this floor's list (the prefab stays in the library; delete it there).");
            ImGui.PopID();
        }
        if (removeId is not null) { _floor.Prefabs.Remove(removeId); Edited(); }
        if (ImGui.Button("+ Add from library...")) { _openAddPrefab = true; _prefabSearch = ""; }
        ImGui.SameLine();
        if (ImGui.Button("+ New prefab")) OpenPrefabEditor(new PrefabDef { Kind = kind, LipLine = kind == MaterialKind.WallUp ? 1f : 0f, Category = "Cliff walls" }, isNew: true);
        AddPrefabPopup(kind);

        Ui.Section("Placed on this tile  (lower = drawn on top)");
        PlacementsList(kind);
    }

    private void PlacementsList(MaterialKind kind)
    {
        var t = _tile!;
        var regions = t.PaintRegions.Where(r => PrefabOf(r)?.Kind == kind || (r.Prefab is not null && PrefabOf(r) is null)).ToList();
        if (regions.Count == 0) ImGui.TextDisabled("Nothing placed yet: press Place next to a prefab.");
        int moveUp = -1, moveDown = -1;
        PaintRegion? delete = null;
        foreach (var r in regions)
        {
            var p = PrefabOf(r);
            ImGui.PushID("pl_" + r.Id);
            int idx = t.PaintRegions.IndexOf(r);
            if (ImGui.ArrowButton("up", ImGuiDir.Up)) moveUp = idx;
            ImGui.SameLine(0, 2);
            if (ImGui.ArrowButton("down", ImGuiDir.Down)) moveDown = idx;
            ImGui.SameLine();
            if (p is not null) { Thumb(p.Texture, 24); ImGui.SameLine(); }
            bool open = _openPlacement == r.Id;
            string label = p is null ? $"missing prefab '{r.Prefab}'" : $"{p.DisplayName}  [{r.Id}]  {r.Cells.Count} cells{(r.Layer == PaintLayer.InFrontOfCharacters ? "  [front]" : "")}{(r.Auto is not null ? "  [auto]" : "")}";
            if (ImGui.Selectable(label, open, ImGuiSelectableFlags.None, new Num.Vector2(0, 24))) _openPlacement = open ? null : r.Id;
            if (open && p is not null)
            {
                ImGui.Indent();
                PlacementFields(r, p);
                if (r.Auto is not null)
                {
                    if (ImGui.Button("Keep (stop regenerating)")) { r.Auto = null; Edited(); }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Auto pieces are replaced when you Regenerate. Keep makes this one a normal placement.");
                    ImGui.SameLine();
                }
                if (ImGui.Button("Remove from tile")) delete = r;
                ImGui.Unindent();
                ImGui.Spacing();
            }
            else if (open && ImGui.Button("Remove from tile")) delete = r;
            ImGui.PopID();
        }
        // Move within the tile's whole region list (that list is the drawing order).
        if (moveUp > 0) { (t.PaintRegions[moveUp - 1], t.PaintRegions[moveUp]) = (t.PaintRegions[moveUp], t.PaintRegions[moveUp - 1]); Edited(); }
        if (moveDown >= 0 && moveDown < t.PaintRegions.Count - 1) { (t.PaintRegions[moveDown + 1], t.PaintRegions[moveDown]) = (t.PaintRegions[moveDown], t.PaintRegions[moveDown + 1]); Edited(); }
        if (delete is not null)
        {
            t.PaintRegions.Remove(delete);
            if (_placeCellsRegion == delete.Id) _placeCellsRegion = null;
            if (_nudgeRegion == delete.Id) _nudgeRegion = null;
            Edited();
        }
    }

    private void PlacementFields(PaintRegion r, PrefabDef p)
    {
        const float label = 120;
        // Cells
        bool picking = _placeCellsRegion == r.Id;
        if (Ui.ToggleButton(picking ? "Done selecting (Esc)" : "Select cells", picking, new Num.Vector2(170, 0),
                "Click / drag cells on the map to show this prefab through them. Click a picked cell (or right-drag) to remove."))
        {
            ClearMapModes();
            _placeCellsRegion = picking ? null : r.Id;
            if (!picking) Status($"Picking cells for {p.DisplayName}. Click/drag; Esc when done.");
        }
        ImGui.SameLine();
        Ui.Colored(r.Cells.Count > 0 ? Ui.Good : Ui.Warn, $"{r.Cells.Count} cell(s)");

        // Position
        bool moving = _nudgeRegion == r.Id;
        if (Ui.ToggleButton(moving ? "Done moving (Esc)" : "Move on map", moving, new Num.Vector2(170, 0),
                "Drag anywhere on the map to slide the prefab from where it snapped (its lip sits on the top of its cells)."))
        {
            ClearMapModes();
            _nudgeRegion = moving ? null : r.Id;
        }
        ImGui.SameLine();
        var nudge = new Num.Vector2(r.NudgeX, r.NudgeY);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 60);
        if (ImGui.DragFloat2("##nudge", ref nudge, 1, -2000, 2000, "%.0f px")) { r.NudgeX = nudge.X; r.NudgeY = nudge.Y; Edited(); }
        ImGui.SameLine(0, 4);
        if (ImGui.Button("Snap")) { r.NudgeX = r.NudgeY = 0; Edited(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Back to the automatic spot (lip on the top of its cells).");

        bool mirror = r.MirrorX;
        if (ImGui.Checkbox("Mirror on this tile (left <-> right)", ref mirror)) { r.MirrorX = mirror; Edited(); }
        int layer = (int)r.Layer;
        ImGui.TextUnformatted("Layer"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] layers = { "Behind characters", "In front of characters" };
        if (ImGui.Combo("##layer", ref layer, layers, layers.Length)) { r.Layer = (PaintLayer)layer; Edited(); }

        // Fade: floor default / own numbers / off
        Ui.Section("Fade");
        int mode = r.FadeEnabled == false ? 2 : (r.FadeStart is not null || r.FadeLength is not null || r.FadeAngle is not null) ? 1 : 0;
        int newMode = mode;
        if (ImGui.RadioButton("Floor default", mode == 0)) newMode = 0;
        ImGui.SameLine();
        if (ImGui.RadioButton("Own numbers", mode == 1)) newMode = 1;
        ImGui.SameLine();
        if (ImGui.RadioButton("No fade", mode == 2)) newMode = 2;
        var floorFade = p.Kind == MaterialKind.WallUp ? _floor?.WallUpFade : _floor?.WallDownFade;
        floorFade ??= new WallFade();
        if (newMode != mode)
        {
            r.FadeEnabled = newMode == 2 ? false : (p.Kind == MaterialKind.General && newMode == 1 ? true : null);
            if (newMode == 1) { r.FadeStart = floorFade.Start; r.FadeLength = floorFade.Length; r.FadeAngle = floorFade.Angle; }
            else { r.FadeStart = null; r.FadeLength = null; r.FadeAngle = null; }
            Edited();
        }
        if (newMode == 1)
        {
            float s = r.FadeStart ?? floorFade.Start, l = r.FadeLength ?? floorFade.Length;
            var a = r.FadeAngle ?? floorFade.Angle;
            if (FadeControls(label, ref s, ref l, ref a, p.Kind != MaterialKind.WallUp)) { r.FadeStart = s; r.FadeLength = l; r.FadeAngle = a; Edited(); }
        }
        else if (newMode == 0) Ui.Hint($"Floor: starts {floorFade.Start:0.##} cells, length {floorFade.Length:0.##} cells.");

        StencilControls(r);
    }

    /// <summary>Adds a placement of a prefab to this tile and starts picking its cells.</summary>
    private void PlacePrefab(PrefabDef p)
    {
        var t = _tile!;
        var r = new PaintRegion { Id = NextId("r", t.PaintRegions.Select(x => x.Id)), Prefab = p.Id, Material = "" };
        t.PaintRegions.Add(r);
        _openPlacement = r.Id;
        ClearMapModes();
        _placeCellsRegion = r.Id;
        Edited();
        Status($"Placed {p.DisplayName}. Now click/drag its cells on the map (Esc when done).");
    }

    private void ClearMapModes()
    {
        _placeCellsRegion = null;
        _nudgeRegion = null;
        _cutOutMaterial = null;
        _stencilRegionId = null;
        _sheetMaterial = null;
        _prefabHandles = false;
        _wallBrush = null;
        _groundBrush = null;
        _swapMode = false;
    }

    private void PrefabTooltip(PrefabDef p)
    {
        ImGui.BeginTooltip();
        ImageInline(p.Texture, 220);
        ImGui.TextUnformatted($"{p.DisplayName}  [{p.Id}]\n{p.Category}   rotation {p.Rotation:0}   zoom {p.Scale:0.##}x{(p.Warp is null ? "" : "   warped")}");
        ImGui.EndTooltip();
    }

    private void AddPrefabPopup(MaterialKind kind)
    {
        if (_openAddPrefab) { ImGui.OpenPopup("Add prefab to floor"); _openAddPrefab = false; }
        CenterNextWindow();
        ImGui.SetNextWindowSize(new Num.Vector2(520, 520), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("Add prefab to floor")) return;
        Ui.Colored(Ui.Muted, $"The prefab library ({(kind == MaterialKind.WallUp ? "Wall up" : "Wall down")}). Pick one to use on floor {_floor!.Number}.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ps", "search...", ref _prefabSearch, 60);
        ImGui.BeginChild("lib", new Num.Vector2(0, -36), ImGuiChildFlags.Borders);
        var all = Services.Defs.All<PrefabDef>().Where(p => p.Kind == kind && !_floor.Prefabs.Contains(p.Id))
            .Where(p => _prefabSearch.Length == 0 || p.DisplayName.Contains(_prefabSearch, StringComparison.OrdinalIgnoreCase) || p.Category.Contains(_prefabSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (all.Count == 0) ImGui.TextDisabled("No other prefabs of this kind. Use + New prefab, or 'Save as prefab' on an older material.");
        foreach (var group in all.GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "(no category)" : p.Category).OrderBy(g => g.Key))
        {
            Ui.Section(group.Key);
            foreach (var p in group.OrderBy(p => p.DisplayName))
            {
                ImGui.PushID(p.Id);
                Thumb(p.Texture, 40);
                ImGui.SameLine();
                if (ImGui.Selectable($"{p.DisplayName}  [{p.Id}]", false, ImGuiSelectableFlags.None, new Num.Vector2(ImGui.GetContentRegionAvail().X - 76, 40)))
                {
                    _floor.Prefabs.Add(p.Id);
                    Edited();
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.IsItemHovered()) PrefabTooltip(p);
                ImGui.SameLine();
                if (ImGui.Button("Delete", new Num.Vector2(70, 0))) { ImGui.CloseCurrentPopup(); AskDeletePrefab(p); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Delete this prefab from the library (asks first).");
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("Close", new Num.Vector2(120, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    /// <summary>"Save as prefab" for an older material: a reusable prefab with the same look, added to this floor.</summary>
    private void SaveMaterialAsPrefab(MaterialDef m)
    {
        var kind = m.EffectiveKind;
        var p = new PrefabDef
        {
            Id = Services.Defs.SuggestId(typeof(PrefabDef), m.Name.Length > 0 ? m.Name : m.Id),
            Name = m.Name, Category = kind == MaterialKind.General ? "Textures" : "Cliff walls", Kind = kind,
            Texture = m.Texture, Rotation = m.Rotation, Tint = m.Tint, FlipX = m.FlipX, FlipY = m.FlipY, Scale = m.Scale,
            Warp = m.Warp is null ? null : ChasmJson.Clone(m.Warp), LipLine = kind == MaterialKind.WallUp ? 1f : 0f,
        };
        Services.Defs.Save(p);
        if (_floor is not null && !_floor.Prefabs.Contains(p.Id)) { _floor.Prefabs.Add(p.Id); Edited(); }
        Status($"Saved prefab '{p.DisplayName}' ({p.Id}). Place it from the Walls page.");
    }

    // ------------------------------------------------------------------ prefab editor page

    private void OpenPrefabEditor(PrefabDef p, bool isNew)
    {
        _prefab = isNew ? p : ChasmJson.Clone(p);
        _prefabIsNew = isNew;
        _prefabReturn = _panel == Panel.Prefab ? _prefabReturn : _panel;
        _prefabTaper = (0, 0);
        ClearMapModes();
        _panel = Panel.Prefab;
        if (isNew) Art.ArtBrowser.OpenPick(null, k =>
        {
            if (_prefab is null) return;
            _prefab.Texture = k;
            if (string.IsNullOrWhiteSpace(_prefab.Name)) _prefab.Name = Path.GetFileNameWithoutExtension(k);
        });
    }

    private bool PrefabDirty => _prefab is not null && (_prefabIsNew
        || !Services.Defs.TryGet<PrefabDef>(_prefab.Id, out var saved) || ChasmJson.Serialize(saved) != ChasmJson.Serialize(_prefab));

    private void PrefabPanel()
    {
        if (_prefab is not { } p) { _panel = Panel.Walls; return; }
        const float label = 120;
        Ui.Hint("A prefab is made once and placed on any tile or floor. It has no fade: each placement fades by its floor's default (or its own).");
        Ui.Colored(_prefabIsNew ? Ui.Warn : Ui.Muted, _prefabIsNew ? "New prefab (not saved yet)" : $"content/defs/prefabs/{p.Id}.json");
        ImGui.BeginChild("prefabed", new Num.Vector2(0, -40));

        ImGui.TextUnformatted("Name"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string name = p.Name;
        if (ImGui.InputText("##pname", ref name, 80)) p.Name = name;

        ImGui.TextUnformatted("Category"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string cat = p.Category;
        var cats = Services.Defs.All<PrefabDef>().Select(x => x.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
        if (ImGui.InputTextWithHint("##pcat", "e.g. Cliff walls", ref cat, 60)) p.Category = cat;
        if (cats.Count > 0 && ImGui.BeginPopupContextItem("catpick", ImGuiPopupFlags.MouseButtonLeft | ImGuiPopupFlags.NoOpenOverExistingPopup))
        {
            foreach (var c in cats) if (ImGui.Selectable(c)) p.Category = c;
            ImGui.EndPopup();
        }

        ImGui.TextUnformatted("Kind"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] kinds = { "Texture (general)", "Wall down (cliff)", "Wall up (wall)" };
        int k = (int)p.Kind;
        if (ImGui.Combo("##pkind", ref k, kinds, kinds.Length)) p.Kind = (MaterialKind)k;

        ImGui.TextUnformatted("Texture"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string tex = p.Texture;
        ImGui.PushID("ptex");
        if (ToolContext.Wizards.Inspector.TexturePicker(ref tex)) p.Texture = tex;
        ImGui.PopID();
        if (string.IsNullOrEmpty(p.Texture)) Ui.Colored(Ui.Warn, "Pick the picture (make it long enough to never show gaps).");

        ImGui.TextUnformatted("Rotation"); ImGui.SameLine(label);
        float rot = p.Rotation;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 150);
        if (ImGui.SliderFloat("##prot", ref rot, 0, 360, "%.0f deg")) p.Rotation = rot;
        foreach (int q in new[] { 0, 90, 180, 270 })
        {
            ImGui.SameLine(0, 3);
            if (ImGui.Button(q + "##pq" + q, new Num.Vector2(34, 0))) p.Rotation = q;
        }

        ImGui.TextUnformatted("Tint"); ImGui.SameLine(label);
        var col = HexToColor(p.Tint);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 64);
        if (ImGui.ColorEdit3("##ptint", ref col, ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.DisplayHex)) p.Tint = ColorToHex(col);
        ImGui.SameLine(0, 4);
        if (ImGui.Button("Reset##pt")) p.Tint = "#ffffff";

        ImGui.TextUnformatted("Flip"); ImGui.SameLine(label);
        bool fx = p.FlipX, fy = p.FlipY;
        if (ImGui.Checkbox("Flip X##pf", ref fx)) p.FlipX = fx;
        ImGui.SameLine(0, 20);
        if (ImGui.Checkbox("Flip Y##pf", ref fy)) p.FlipY = fy;

        ImGui.TextUnformatted("Zoom"); ImGui.SameLine(label);
        float zoom = p.Scale;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 50);
        if (ImGui.SliderFloat("##pzoom", ref zoom, 0.1f, 4f, "%.2fx", ImGuiSliderFlags.Logarithmic)) p.Scale = zoom;
        ImGui.SameLine(0, 4);
        if (ImGui.Button("1:1##pz")) p.Scale = 1;

        ImGui.TextUnformatted(p.Kind == MaterialKind.WallUp ? "Foot line" : "Lip line"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        float lip = p.LipLine;
        if (ImGui.SliderFloat("##plip", ref lip, 0, 1, "%.2f")) p.LipLine = lip;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(p.Kind == MaterialKind.WallUp
            ? "Where the wall's foot is in the picture (0 = top, 1 = bottom). This line sits on the bottom of its cells."
            : "Where the cliff's top edge is in the picture (0 = top, 1 = bottom). This line sits on the top of its cells.");

        Ui.Section("Perspective");
        bool warp = p.Warp is not null;
        if (ImGui.Checkbox("Perspective warp (corner pin)", ref warp)) { p.Warp = warp ? new MaterialWarp() : null; _prefabHandles = warp; }
        if (p.Warp is not null)
        {
            var t = _prefabTaper;
            var texObj = Services.Textures.Get(p.Texture);
            ImGui.TextUnformatted("Taper"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
            bool tc = ImGui.SliderFloat("##ptaper", ref t.taper, -0.9f, 0.9f, t.taper >= 0 ? "bottom %.2f" : "top %.2f");
            ImGui.TextUnformatted("Squash"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
            tc |= ImGui.SliderFloat("##psquash", ref t.squash, 0f, 0.8f, "%.2f");
            if (tc) { _prefabTaper = t; p.Warp = MaterialWarp.Taper(t.taper, t.squash, texObj.Width * p.Scale, texObj.Height * p.Scale); }
            if (ImGui.Button("Reset corners")) { p.Warp = new MaterialWarp(); _prefabTaper = (0, 0); }
            ImGui.SameLine();
            if (Ui.ToggleButton(_prefabHandles ? "Hide handles" : "Corner handles", _prefabHandles, new Num.Vector2(150, 0), "Drag the corners on the map preview.")) _prefabHandles = !_prefabHandles;
        }
        if (Ui.ToggleButton("Grid & highlights", _previewHelpers, new Num.Vector2(0, 0), "Outline, lip line and caption on the preview. Off = as in game.")) _previewHelpers = !_previewHelpers;
        Ui.Hint("The map shows a preview of the prefab. With highlights on, the orange line is its lip (or foot): that line sits on the edge of its cells when placed.");
        ImGui.EndChild();

        // Save / revert / close
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrEmpty(p.Texture));
        if (Ui.AccentButton(PrefabDirty ? "Save prefab *" : "Save prefab", new Num.Vector2(140, 0))) SavePrefab();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (!_prefabIsNew && ImGui.Button("Duplicate")) DuplicatePrefab();
        if (!_prefabIsNew) ImGui.SameLine();
        if (!_prefabIsNew && ImGui.Button("Delete...") && Services.Defs.TryGet<PrefabDef>(p.Id, out var savedP)) AskDeletePrefab(savedP);
        ImGui.SameLine();
        if (ImGui.Button("Close")) { _prefab = null; _panel = _prefabReturn == Panel.Prefab ? Panel.Walls : _prefabReturn; }
    }

    private void SavePrefab()
    {
        var p = _prefab!;
        if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Services.Defs.SuggestId(typeof(PrefabDef), p.Name);
        Services.Defs.Save(ChasmJson.Clone(p));
        if (_prefabIsNew && _floor is not null && !_floor.Prefabs.Contains(p.Id)) { _floor.Prefabs.Add(p.Id); Edited(); }
        _prefabIsNew = false;
        Services.TileVisuals.InvalidateAll();
        Status($"Saved prefab '{p.DisplayName}'. Every placement of it updates.");
    }

    /// <summary>Copy as a new prefab: Left/Right (or Down/Up) swapped in the name and Flip X toggled.</summary>
    private void DuplicatePrefab()
    {
        var src = _prefab!;
        var copy = ChasmJson.Clone(src);
        copy.Id = "";
        copy.Name = SwapWords(src.Name, ("Left", "Right"), ("left", "right")) is var n && n != src.Name ? n : src.Name + " copy";
        copy.FlipX = !copy.FlipX;
        _prefab = copy;
        _prefabIsNew = true;
        Status("Duplicated (flipped left/right). Change Kind to move it to the other side; Save to keep it.");
    }

    // ------------------------------------------------------------------ map: picking, moving, preview

    /// <summary>Map input for placement cell picking, moving, and the prefab editor's handles. True = used.</summary>
    private bool UpdatePrefabInput(InputState input)
    {
        var t = _tile!;
        bool cancel = input.Pressed(InputAction.Cancel) && !input.KeyboardCapturedByUi;
        if (_placeCellsRegion is { } pid)
        {
            var r = t.PaintRegions.FirstOrDefault(x => x.Id == pid);
            if (r is null) { _placeCellsRegion = null; return false; }
            if (cancel) { _placeCellsRegion = null; Status("Done picking cells."); return true; }
            if (input.LeftReleased || input.RightReleased) _dragMode = null;
            PaintCells(input, c => r.Cells.Contains(c), (c, v) =>
            {
                if (v) { if (!r.Cells.Contains(c)) r.Cells.Add(c); }
                else { r.Cells.Remove(c); r.Stencils.RemoveAll(s => s.Cell == c); }
            }, rightPaintsFalse: true);
            return true;
        }
        if (_nudgeRegion is { } nid)
        {
            var r = t.PaintRegions.FirstOrDefault(x => x.Id == nid);
            if (r is null) { _nudgeRegion = null; return false; }
            if (cancel) { _nudgeRegion = null; Status("Done moving."); return true; }
            if (input.LeftClicked && !input.MouseCapturedByUi) { _nudgeDragging = true; _nudgeDragStart = _hoverPx; _nudgeStart = new Num.Vector2(r.NudgeX, r.NudgeY); }
            else if (input.LeftHeld && _nudgeDragging)
            {
                var d = _hoverPx - _nudgeDragStart;
                float nx = MathF.Round(_nudgeStart.X + d.X), ny = MathF.Round(_nudgeStart.Y + d.Y);
                if (nx != r.NudgeX || ny != r.NudgeY) { r.NudgeX = nx; r.NudgeY = ny; Edited(); }
            }
            if (input.LeftReleased) _nudgeDragging = false;
            return true;
        }
        if (_panel == Panel.Prefab && _prefab is { Warp: not null } p && _prefabHandles)
        {
            if (cancel) { _prefabHandles = false; return true; }
            var tex = Services.Textures.Get(p.Texture);
            var corners = PrefabPreviewCorners(p, tex);
            float pick = 16f / CanvasScale();
            if (input.LeftClicked && !input.MouseCapturedByUi)
            {
                _prefabHandle = -1;
                for (int i = 0; i < 4; i++) if (Num.Vector2.Distance(_hoverPx, corners[i]) <= pick) { _prefabHandle = i; break; }
                if (_prefabHandle >= 0) { _prefabDragStart = _hoverPx; _prefabStartValue = p.Warp.Get(_prefabHandle); }
            }
            else if (input.LeftHeld && _prefabHandle >= 0)
            {
                var d = _hoverPx - _prefabDragStart;
                float r = -p.Rotation * MathF.PI / 180f, cs = MathF.Cos(r), sn = MathF.Sin(r);
                var local = new Num.Vector2(d.X * cs - d.Y * sn, d.X * sn + d.Y * cs);
                p.Warp.Set(_prefabHandle, _prefabStartValue + local);
            }
            if (input.LeftReleased) _prefabHandle = -1;
            return true;
        }
        return _panel == Panel.Prefab; // the preview owns the map while the prefab editor is open
    }

    private static Num.Vector2[] PrefabPreviewCorners(PrefabDef p, Texture2D tex)
    {
        var dummy = new PaintRegion { NudgeX = PrefabPreviewAnchor.X - 960, NudgeY = PrefabPreviewAnchor.Y - 540 };
        return PrefabPlacement.Corners(dummy, p, tex.Width, tex.Height);
    }

    /// <summary>Prefab editor: the prefab drawn over a dimmed map, with its outline, lip line and handles.</summary>
    private void DrawPrefabPreview(SpriteBatch batch)
    {
        if (_panel != Panel.Prefab || _prefab is not { } p || string.IsNullOrEmpty(p.Texture)) return;
        var d = Services.Draw;
        batch.Begin();
        d.Rect(batch, new Rectangle(0, 0, 1920, 1080), new Color(10, 12, 20) * 0.72f);
        batch.End();
        var tex = Services.Textures.Get(p.Texture);
        var c = PrefabPreviewCorners(p, tex);
        Services.TileVisuals.DrawQuad(tex, c, p.Tint, p.FlipX, p.FlipY);
        if (!_previewHelpers && !(_prefabHandles && p.Warp is not null)) return; // clean view
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        float px = 1f / CanvasScale();
        var col = new Color(255, 200, 80);
        for (int i = 0; i < 4; i++) d.Line(batch, ToXna(c[i]), ToXna(c[(i + 1) % 4]), col * 0.6f, 2 * px);
        // Lip / foot line across the picture, and the anchor point.
        var a = PrefabPreviewAnchor;
        float rr = p.Rotation * MathF.PI / 180f;
        var across = new Num.Vector2(MathF.Cos(rr), MathF.Sin(rr)) * (tex.Width * p.Scale / 2 + 40);
        d.Line(batch, ToXna(a - across), ToXna(a + across), new Color(255, 150, 40), 3 * px);
        d.TextShadowed(batch, p.Kind == MaterialKind.WallUp ? "foot line" : "lip line", ToXna(a + across) + new Vector2(8 * px, -12 * px), new Color(255, 170, 70), 18 * px);
        if (p.Warp is not null && _prefabHandles)
            foreach (var q in c)
            {
                float s = 9 * px;
                var rect = new Rectangle((int)(q.X - s), (int)(q.Y - s), (int)(s * 2), (int)(s * 2));
                d.Rect(batch, rect, Color.Black * 0.6f);
                d.RectOutline(batch, rect, col, (int)MathF.Max(1, 2 * px));
            }
        d.TextShadowed(batch, $"Prefab preview: {p.DisplayName}", new Vector2(24, 24), Color.White, 26 * px, bold: true);
        batch.End();
    }

    /// <summary>Map overlays for placements: picked cells while picking, anchor of the open placement.</summary>
    private void DrawPlacementOverlays(SpriteBatch b)
    {
        var t = _tile!;
        var d = Services.Draw;
        var id = _placeCellsRegion ?? _nudgeRegion ?? (_panel == Panel.Walls ? _openPlacement : null);
        if (id is null || t.PaintRegions.FirstOrDefault(x => x.Id == id) is not { } r || PrefabOf(r) is not { } p) return;
        foreach (var c in r.Cells) d.DiamondOutline(b, Chasm.Core.Grid.IsoGrid.CellCenter(c), 118, 59, new Color(255, 190, 70), 2);
        var tex = Services.Textures.Get(p.Texture);
        var corners = PrefabPlacement.Corners(r, p, tex.Width, tex.Height);
        float px = 1f / CanvasScale();
        for (int i = 0; i < 4; i++) d.Line(b, ToXna(corners[i]), ToXna(corners[(i + 1) % 4]), new Color(255, 200, 80) * 0.45f, 2 * px);
        var a = ToXna(PrefabPlacement.Anchor(r, p));
        float s = 12 * px;
        d.Line(b, a - new Vector2(s, 0), a + new Vector2(s, 0), new Color(255, 150, 40), 3 * px);
        d.Line(b, a - new Vector2(0, s), a + new Vector2(0, s), new Color(255, 150, 40), 3 * px);
    }
}
