using Chasm.Core.Grid;
using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Game.Rendering;
using Chasm.Game.World;
using Chasm.Tools.ImGuiHost;
using Chasm.Tools.Scenes;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// The ground builder: makes a ground prefab step by step, like the wall builder. The preview is a
/// fixed walkway shape (with this floor's cliffs and clouds), so every situation shows: open
/// ground, the edge at a cliff top, and (from step 4) a strip of another ground it meets.
/// Nothing is saved until Save (overwrite) or Save as new.
/// </summary>
public sealed partial class MapEditorScene
{
    private enum GroundStep { Base, Details, Edge, Meeting, Review }

    private static readonly (GroundStep step, string title, string help)[] GroundSteps =
    {
        (GroundStep.Base, "Base", "The main texture that covers the whole ground: picture, zoom, turn, tint."),
        (GroundStep.Details, "Detail layers", "Layers over the base (moss, dirt, stones...). Use Patches to show a layer only in natural blotches."),
        (GroundStep.Edge, "Edge", "How the ground ends at a cliff top or the void: soft or crisp, and an optional darker rim."),
        (GroundStep.Meeting, "Meeting other ground", "Where this ground is painted over another, how wide the blend between them is."),
        (GroundStep.Review, "Review & save", "Everything together. Save to use it on floors."),
    };

    private GroundPrefabDef? _ground;
    private bool _groundIsNew;
    private GroundStep _groundStep;
    private string? _openGroundLayer;
    private bool _openSaveAsGround;
    private string _groundSaveAsName = "";
    private static readonly GroundPrefabDef PreviewOtherGround = new()
    {
        Id = "__builder_other", Name = "Other ground",
        Layers = { new GroundLayer { Id = "layer_1", Name = "Stone", Texture = "placeholder/textures/stone_wall.png", Scale = 0.6f } },
    };

    private bool InBuilderPreview => _previewTile is not null &&
        ((_panel == Panel.WallEdit && _wall is not null) || (_panel == Panel.GroundEdit && _ground is not null));

    private void StartNewGround()
    {
        var g = new GroundPrefabDef { Category = "Ground" };
        g.Layers.Add(new GroundLayer { Id = "layer_1", Name = "Base" });
        OpenGroundEditor(g, isNew: true);
        Art.ArtBrowser.OpenPick(null, k =>
        {
            if (_ground is null) return;
            _ground.Layers[0].Texture = k;
            if (string.IsNullOrWhiteSpace(_ground.Name)) _ground.Name = Path.GetFileNameWithoutExtension(k);
            GroundPreviewChanged();
        });
    }

    private void OpenGroundEditor(GroundPrefabDef g, bool isNew)
    {
        _ground = isNew ? g : ChasmJson.Clone(g);
        _groundIsNew = isNew;
        ClearMapModes();
        _panel = Panel.GroundEdit;
        _groundStep = GroundSteps.Select(x => x.step).FirstOrDefault(st => !g.Approved.Contains(st.ToString()), GroundStep.Review);
        BuildGroundPreview();
    }

    private bool GroundDirty => _ground is not null && (_groundIsNew
        || !Services.Defs.TryGet<GroundPrefabDef>(_ground.Id, out var saved) || ChasmJson.Serialize(saved) != ChasmJson.Serialize(_ground));

    /// <summary>The preview: a walkway block with a notch and a step, on this floor's cliffs and clouds.</summary>
    private void BuildGroundPreview()
    {
        if (_previewOnTile && _tile is not null && _floor is not null) { BuildGroundPreviewOnTile(); return; }
        var t = new TileDef { Id = "__ground_builder", Name = "Ground builder" };
        foreach (var c in IsoGrid.AllCells) t.SetWalkable(c, false);
        var walk = new List<CellCoord>();
        for (int q = 9; q <= 18; q++) for (int r = -2; r <= 2; r++) walk.Add(new(q, r));
        walk.Remove(new(13, 2));
        walk.Add(new(16, 3)); walk.Add(new(17, 3));
        foreach (var c in walk.Where(IsoGrid.InTile)) t.SetWalkable(c, true);
        // From step 4: a strip of another ground along the left end, so the meeting blend shows.
        bool meeting = _groundStep is GroundStep.Meeting or GroundStep.Review;
        var f = _floor is null ? new FloorDef { Id = "__builder_floor" } : ChasmJson.Clone(_floor);
        f.Id = "__builder_floor";
        f.GroundUnwalkable = new();
        if (meeting)
        {
            f.GroundWalkable = new() { new FloorGround { Prefab = PreviewOtherGround.Id }, new FloorGround { Prefab = "__builder_ground" } };
            t.GroundPaint.Add(new TileGroundPaint { Side = "walk", Ground = "__builder_ground", Cells = walk.Where(c => c.Q >= 12).ToList() });
        }
        else f.GroundWalkable = new() { new FloorGround { Prefab = "__builder_ground" } };
        t.InvalidateCaches();
        _previewTile = t;
        _previewFloor = f;
        GroundPreviewChanged();
    }

    /// <summary>
    /// The preview on the tile being edited: a copy of it and its floor, with this ground standing in
    /// for itself wherever it's used (or, if the floor doesn't use it yet, as the first ground of the
    /// list you came from - the automatic one on the Walkable tab). Nothing real changes.
    /// </summary>
    private void BuildGroundPreviewOnTile()
    {
        var g = _ground!;
        var t = ChasmJson.Clone(_tile!);
        t.Id = "__ground_builder_tile";
        var f = ChasmJson.Clone(_floor!);
        f.Id = "__builder_floor";
        bool used = false;
        foreach (var e in f.GroundWalkable.Concat(f.GroundUnwalkable).Where(e => e.Prefab == g.Id && !string.IsNullOrEmpty(g.Id))) { e.Prefab = "__builder_ground"; used = true; }
        if (!used) (_groundVoid ? f.GroundUnwalkable : f.GroundWalkable).Insert(0, new FloorGround { Prefab = "__builder_ground" });
        foreach (var p in t.GroundPaint.Where(p => p.Ground == g.Id && !string.IsNullOrEmpty(g.Id))) p.Ground = "__builder_ground";
        t.InvalidateCaches();
        _previewTile = t;
        _previewFloor = f;
        GroundPreviewChanged();
    }

    private void GroundPreviewChanged()
    {
        if (_previewTile is null || _ground is null) return;
        var g = _ground;
        Services.TileVisuals.GroundOverride = id => id == "__builder_ground" ? g : id == PreviewOtherGround.Id ? PreviewOtherGround : null;
        Services.TileVisuals.Invalidate(_previewTile);
    }

    private void GroundEditPanel()
    {
        if (_ground is not { } g) { _panel = Panel.Ground; return; }
        int idx = Array.FindIndex(GroundSteps, x => x.step == _groundStep);
        var (step, title, help) = GroundSteps[idx];
        Ui.Colored(_groundIsNew ? Ui.Warn : Ui.Muted, _groundIsNew ? "New ground (not saved yet)" : $"content/defs/groundprefabs/{g.Id}.json");
        int done = GroundSteps.Count(x => g.Approved.Contains(x.step.ToString()));
        ImGui.ProgressBar(done / (float)GroundSteps.Length, new Num.Vector2(-1, 6), "");
        ImGui.BeginChild("gsteps", new Num.Vector2(0, 3 * ImGui.GetTextLineHeightWithSpacing() + 18), ImGuiChildFlags.Borders);
        for (int i = 0; i < GroundSteps.Length; i++)
        {
            var s = GroundSteps[i];
            bool ok = g.Approved.Contains(s.step.ToString());
            if (i % 2 == 1) ImGui.SameLine(ImGui.GetContentRegionAvail().X / 2 + 8);
            ImGui.PushStyleColor(ImGuiCol.Text, ok ? Ui.Good : s.step == step ? Ui.Accent : Ui.Muted);
            if (ImGui.Selectable($"{(ok ? "v" : " ")} {i + 1}. {s.title}##gst{i}", s.step == step, ImGuiSelectableFlags.None, new Num.Vector2(ImGui.GetContentRegionAvail().X / (i % 2 == 0 ? 2 : 1) - 8, 0)))
                GoToGroundStep(s.step);
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();
        Ui.Title($"{idx + 1}. {title}");
        Ui.Hint(help);
        if (Ui.ToggleButton("Grid & captions", _previewHelpers, new Num.Vector2(0, 0), "Show the grid on the preview (H).")) _previewHelpers = !_previewHelpers;
        ImGui.SameLine(0, 12);
        if (ImGui.Checkbox("Preview on this tile", ref _previewOnTile)) BuildGroundPreview();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("On: the ground as it would look on the tile you're editing (nothing real changes until you save).\nOff: a fixed test shape (with a strip of another ground from step 4).");
        if (_previewOnTile && _tile is { PaintRegions.Count: > 0 } && !_groundVoid)
            Ui.Colored(Ui.Muted, "Painted textures on this tile draw over the ground.");
        if (_previewOnTile && _groundVoid && !(_previewTile?.GroundPaint.Any(p => p.Ground == "__builder_ground") ?? false))
            Ui.Colored(Ui.Warn, "No unwalkable cells use this ground on this tile yet: paint some (Assign), or untick to see the test shape.");

        ImGui.BeginChild("gstepbody", new Num.Vector2(0, -78));
        bool changed = step switch
        {
            GroundStep.Base => GroundBaseStep(g),
            GroundStep.Details => GroundDetailsStep(g),
            GroundStep.Edge => GroundEdgeStep(g),
            GroundStep.Meeting => GroundMeetingStep(g),
            _ => GroundReviewStep(g),
        };
        ImGui.EndChild();
        if (changed) GroundPreviewChanged();

        bool approved = g.Approved.Contains(step.ToString());
        ImGui.BeginDisabled(idx == 0);
        if (ImGui.Button("< Back", new Num.Vector2(80, 0))) GoToGroundStep(GroundSteps[idx - 1].step);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (step != GroundStep.Review && Ui.AccentButton(approved ? "Approved - next >" : "Approve & next >", new Num.Vector2(170, 0)))
        {
            if (!approved) g.Approved.Add(step.ToString());
            GoToGroundStep(GroundSteps[idx + 1].step);
        }
        ImGui.Spacing();
        bool canSave = !string.IsNullOrWhiteSpace(g.Name) && g.Layers.Any(l => !string.IsNullOrEmpty(l.Texture));
        ImGui.BeginDisabled(!canSave);
        if (Ui.AccentButton(GroundDirty ? "Save ground *" : "Save ground", new Num.Vector2(130, 0))) SaveGround();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(_groundIsNew ? "Save it to the ground library (and add it to this floor's list if the list is empty)." : "Save over this ground: every floor and tile using it updates.");
        ImGui.SameLine();
        if (ImGui.Button("Save as new...")) { _groundSaveAsName = g.Name + (_groundIsNew ? "" : " 2"); _openSaveAsGround = true; }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Close")) CloseGroundEditor();
        SaveAsGroundPopup();
    }

    private void GoToGroundStep(GroundStep step)
    {
        bool wasMeeting = _groundStep is GroundStep.Meeting or GroundStep.Review;
        _groundStep = step;
        if (!_previewOnTile && wasMeeting != (step is GroundStep.Meeting or GroundStep.Review)) BuildGroundPreview();
    }

    // ---- steps

    private bool GroundBaseStep(GroundPrefabDef g)
    {
        const float label = 120;
        bool changed = false;
        ImGui.TextUnformatted("Name"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string name = g.Name;
        if (ImGui.InputText("##gname", ref name, 80)) g.Name = name;
        ImGui.TextUnformatted("Category"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string cat = g.Category;
        if (ImGui.InputTextWithHint("##gcat", "e.g. Grass", ref cat, 60)) g.Category = cat;
        if (g.Layers.Count == 0) { g.Layers.Add(new GroundLayer { Id = "layer_1", Name = "Base" }); changed = true; }
        ImGui.Spacing();
        changed |= GroundLayerFields(g.Layers[0], allowPatches: false);
        Ui.Hint("Paint it seamless (it repeats across the screen). Detail layers go on top in the next step.");
        return changed;
    }

    private bool GroundDetailsStep(GroundPrefabDef g)
    {
        bool changed = false;
        if (g.Layers.Count == 0) { g.Layers.Add(new GroundLayer { Id = "layer_1", Name = "Base" }); changed = true; }
        Ui.Colored(Ui.Muted, $"1. {g.Layers[0].Name} (the base: step 1)");
        int up = -1, down = -1, remove = -1;
        for (int i = 1; i < g.Layers.Count; i++)
        {
            var l = g.Layers[i];
            ImGui.PushID("gl" + l.Id);
            if (ImGui.ArrowButton("up", ImGuiDir.Up)) up = i;
            ImGui.SameLine(0, 2);
            if (ImGui.ArrowButton("down", ImGuiDir.Down)) down = i;
            ImGui.SameLine();
            bool on = l.Enabled;
            if (ImGui.Checkbox("##on", ref on)) { l.Enabled = on; changed = true; }
            ImGui.SameLine();
            Thumb(l.Texture, 22);
            ImGui.SameLine();
            bool open = _openGroundLayer == l.Id;
            ImGui.SetNextItemOpen(open);
            bool node = ImGui.TreeNodeEx($"{i + 1}. {l.Name}{(l.Patches ? "  (patches)" : "")}###node", ImGuiTreeNodeFlags.SpanAvailWidth);
            if (node != open) _openGroundLayer = node ? l.Id : null;
            if (node)
            {
                changed |= GroundLayerFields(l, allowPatches: true);
                if (ImGui.Button("Delete layer")) remove = i;
                ImGui.TreePop();
                ImGui.Spacing();
            }
            ImGui.PopID();
        }
        if (up > 1) { (g.Layers[up - 1], g.Layers[up]) = (g.Layers[up], g.Layers[up - 1]); changed = true; }
        if (down >= 1 && down < g.Layers.Count - 1) { (g.Layers[down + 1], g.Layers[down]) = (g.Layers[down], g.Layers[down + 1]); changed = true; }
        if (remove >= 1) { g.Layers.RemoveAt(remove); changed = true; }
        if (Ui.AccentButton("+ Add layer"))
        {
            var layer = new GroundLayer { Id = g.NewLayerId(), Name = $"Layer {g.Layers.Count + 1}", Patches = true, PatchSeed = g.Layers.Count + 1 };
            g.Layers.Add(layer);
            _openGroundLayer = layer.Id;
            changed = true;
            Art.ArtBrowser.OpenPick(null, k => { layer.Texture = k; if (layer.Name.StartsWith("Layer ")) layer.Name = Path.GetFileNameWithoutExtension(k); GroundPreviewChanged(); });
        }
        if (g.Layers.Count == 1) Ui.Hint("No detail layers yet. Add one (it starts in patches) - e.g. moss, dirt or pebbles over the base.");
        return changed;
    }

    /// <summary>One layer's look: picture, tint, opacity, zoom, turn, offset, flip, and (details) patches.</summary>
    private bool GroundLayerFields(GroundLayer l, bool allowPatches)
    {
        const float label = 120;
        bool changed = NameRow(l.Name, n => l.Name = n);
        ImGui.TextUnformatted("Texture"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string tex = l.Texture;
        ImGui.PushID("gtex");
        if (ToolContext.Wizards.Inspector.TexturePicker(ref tex)) { l.Texture = tex; changed = true; }
        ImGui.PopID();
        if (string.IsNullOrEmpty(l.Texture)) Ui.Colored(Ui.Warn, "Pick a texture.");
        ImGui.TextUnformatted("Tint"); ImGui.SameLine(label);
        var col = HexToColor(l.Tint);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 64);
        if (ImGui.ColorEdit3("##gtint", ref col, ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.DisplayHex)) { l.Tint = ColorToHex(col); changed = true; }
        ImGui.SameLine(0, 4);
        if (ImGui.Button("Reset##gt")) { l.Tint = "#ffffff"; changed = true; }
        changed |= SliderRow("Opacity", label, () => l.Opacity, x => l.Opacity = x, 0, 1, "%.2f");
        changed |= SliderRow("Zoom", label, () => l.Scale, x => l.Scale = x, 0.05f, 8f, "%.2fx", log: true);
        changed |= SliderRow("Turn", label, () => l.Rotation, x => l.Rotation = x, 0, 360, "%.0f deg");
        changed |= SliderRow("Shift X", label, () => l.OffsetX, x => l.OffsetX = x, -1024, 1024, "%.0f px");
        changed |= SliderRow("Shift Y", label, () => l.OffsetY, x => l.OffsetY = x, -1024, 1024, "%.0f px");
        bool fx = l.FlipX;
        if (ImGui.Checkbox("Flip##gfx", ref fx)) { l.FlipX = fx; changed = true; }
        if (!allowPatches) return changed;
        ImGui.Spacing();
        bool patches = l.Patches;
        if (ImGui.Checkbox("Only in patches", ref patches)) { l.Patches = patches; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Show this layer in natural blotches instead of everywhere.");
        if (l.Patches)
        {
            changed |= SliderRow("Patch size", label, () => l.PatchSize, x => l.PatchSize = x, 16, 1200, "%.0f px", log: true);
            changed |= SliderRow("Coverage", label, () => l.PatchAmount, x => l.PatchAmount = x, 0, 1, "%.2f");
            changed |= SliderRow("Softness", label, () => l.PatchSoftness, x => l.PatchSoftness = x, 0, 1, "%.2f");
            ImGui.TextUnformatted("Arrangement"); ImGui.SameLine(label);
            int seed = l.PatchSeed;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("##gseed", ref seed)) { l.PatchSeed = seed; changed = true; }
            ImGui.SameLine();
            if (ImGui.Button("Shuffle")) { l.PatchSeed = Random.Shared.Next(1, 100000); changed = true; }
        }
        return changed;
    }

    private bool GroundEdgeStep(GroundPrefabDef g)
    {
        const float label = 120;
        bool changed = false;
        float soft = g.EdgeSoftness;
        changed |= SliderRow("Edge softness", label, () => soft, x => { g.EdgeSoftness = (int)MathF.Round(x); }, 0, 64, "%.0f px");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("0 = a crisp edge along the cells; higher = softer.");
        changed |= SliderRow("Rim darkening", label, () => g.EdgeShade, x => g.EdgeShade = x, 0, 1, g.EdgeShade <= 0 ? "off" : "%.2f");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Darkens the ground near its edge: a worn, shaded rim.");
        if (g.EdgeShade > 0) changed |= SliderRow("Rim width", label, () => g.EdgeShadeWidth, x => g.EdgeShadeWidth = x, 4, 160, "%.0f px");
        Ui.Hint("The cliff's own rim (wall builder, step 8) can also lay a strip of rock over this edge.");
        return changed;
    }

    private bool GroundMeetingStep(GroundPrefabDef g)
    {
        float tr = g.Transition;
        bool changed = SliderRow("Blend width", 120, () => tr, x => g.Transition = (int)MathF.Round(x), 0, 128, "%.0f px");
        Ui.Hint("The preview paints this ground over a strip of stone on the left. This sets how wide the blend is where it's painted over another ground (the automatic ground, or another painted one).");
        return changed;
    }

    private bool GroundReviewStep(GroundPrefabDef g)
    {
        foreach (var (step, title, _) in GroundSteps.Where(x => x.step != GroundStep.Review))
        {
            bool ok = g.Approved.Contains(step.ToString());
            Ui.Colored(ok ? Ui.Good : Ui.Warn, $"{(ok ? "Approved" : "Not approved yet")}:  {title}");
        }
        ImGui.Spacing();
        Ui.Hint($"{g.Layers.Count} layer(s). Save, then add it to a floor's Walkable or Unwalkable list.");
        return false;
    }

    private void SaveGround()
    {
        var g = _ground!;
        if (string.IsNullOrWhiteSpace(g.Id)) g.Id = Services.Defs.SuggestId(typeof(GroundPrefabDef), g.Name);
        Services.Defs.Save(ChasmJson.Clone(g));
        if (_groundIsNew && _floor is not null && GroundList.Count == 0)
        {
            GroundList.Add(new FloorGround { Prefab = g.Id });
            Edited();
        }
        _groundIsNew = false;
        GroundPreviewChanged();
        Services.TileVisuals.InvalidateAll();
        Status($"Saved ground '{g.DisplayName}'. Every floor and tile using it updates.");
    }

    private void SaveAsGroundPopup()
    {
        if (_openSaveAsGround) { ImGui.OpenPopup("Save as a new ground"); _openSaveAsGround = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Save as a new ground", ImGuiWindowFlags.AlwaysAutoResize)) return;
        Ui.Hint("Saves this as a new ground in the library. The one you opened stays as it was.");
        ImGui.SetNextItemWidth(300);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        bool enter = ImGui.InputTextWithHint("##gsan", "Ground name", ref _groundSaveAsName, 60, ImGuiInputTextFlags.EnterReturnsTrue);
        bool ok = _groundSaveAsName.Trim().Length > 0;
        ImGui.BeginDisabled(!ok);
        if (Ui.AccentButton("Save", new Num.Vector2(120, 0)) || (enter && ok))
        {
            var copy = ChasmJson.Clone(_ground!);
            copy.Name = _groundSaveAsName.Trim();
            copy.Id = Services.Defs.SuggestId(typeof(GroundPrefabDef), copy.Name);
            _ground = copy;
            _groundIsNew = true;
            SaveGround();
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(100, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void CloseGroundEditor()
    {
        _ground = null;
        _previewTile = null;
        Services.TileVisuals.GroundOverride = null;
        Services.TileVisuals.InvalidateAll();
        _panel = Panel.Ground;
    }

    /// <summary>The ground builder's preview, drawn instead of the tile.</summary>
    private void DrawGroundPreview(SpriteBatch batch)
    {
        TileRenderer.QueueLayers(_queue, Services, _previewTile!, _previewFloor, includeProps: false);
        _queue.Flush(batch);
        if (!_previewHelpers) return;
        var d = Services.Draw;
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        foreach (var c in IsoGrid.AllCells) d.DiamondOutline(batch, IsoGrid.CellCenter(c), IsoGrid.CellWidth, IsoGrid.CellHeight, EditorPrefs.ToColor(_prefs.GridColor));
        d.TextShadowed(batch, $"Ground builder: {_ground!.DisplayName}  -  {GroundSteps.First(x => x.step == _groundStep).title}", new Vector2(24, 24), Color.White, 26 / CanvasScale(), bold: true);
        batch.End();
    }
}
