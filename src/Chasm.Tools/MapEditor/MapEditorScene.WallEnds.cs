using Chasm.Core.Serialization;
using Chasm.Tools.ImGuiHost;
using Chasm.Core.Grid;
using Chasm.Core.World;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// Wall builder, step 6 (wall ends). End 1 is always the plain straight edge. "+ Add variation"
/// starts from a saved combo or from scratch; from scratch shows one wall stopping on the right,
/// zoomed in, in this wall's own picture and settings, to shape cell by cell. Saving adds it to
/// the wall (and, by default, saves it as a combo other walls can use).
/// </summary>
public sealed partial class MapEditorScene
{
    /// <summary>The end being made from scratch (already in the wall's list while it's edited).</summary>
    private EndCapVariant? _capDraft;
    private bool _capDraftAsCombo = true;
    private bool _openAddCap;
    private bool _addCapFromSaved;

    private bool EndCapStep(WallDef w)
    {
        bool changed = false;
        var list = w.EndCapVariants;
        if (list.Count == 0 || !list[0].IsStraight)
        {
            list.Insert(0, EndCapVariant.Straight());
            changed = true;
            Status("Wall end 1 is now the plain straight edge (the default). Your other ends are variations.");
        }
        _variantTab = Math.Clamp(_variantTab, 0, list.Count - 1);

        if (Ui.AccentButton("+ Add variation")) _openAddCap = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Use a saved one, or make one from scratch.");
        AddCapPopup(w);
        Ui.Hint("End 1, the straight edge, is used everywhere. Variations can be swapped in on specific spots (Walls page > Swap mode).");
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) ImGui.SameLine(0, 4);
            if (Ui.ToggleButton($"{i + 1}. {list[i].Name}##ct{i}", _variantTab == i, new Num.Vector2(0, 0), i == 0 ? "The default everywhere" : "Swap in on tiles"))
            { _variantTab = i; _rowTab = 0; changed = true; }
            if (ImGui.GetContentRegionAvail().X < 120) ImGui.NewLine();
        }
        if (list.Count > 1)
        {
            if (ImGui.Checkbox("Compare side by side", ref _compareVariants)) changed = true;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("On: the ends in the preview show 1, 2, 3... left to right.\nOff: every end shows the one you're editing.");
        }
        ImGui.Separator();

        if (_variantTab == 0)
        {
            Ui.Colored(Ui.Accent, "Straight edge");
            Ui.Hint("The wall is simply cut straight where it stops. Nothing to set.");
            return changed;
        }
        var v = list[_variantTab];
        changed |= EndCapFields(v);
        ImGui.Spacing();
        if (ImGui.Button("Delete this variation")) { list.RemoveAt(_variantTab); _variantTab = Math.Max(0, _variantTab - 1); changed = true; }
        Ui.Hint("Spots that swapped to a deleted variation fall back to the nearest one.");
        return changed;
    }

    private void AddCapPopup(WallDef w)
    {
        if (_openAddCap) { ImGui.OpenPopup("Add a wall end variation"); _openAddCap = false; _addCapFromSaved = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Add a wall end variation", ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (!_addCapFromSaved)
        {
            Ui.Hint("Start from a saved one, or make a new one cell by cell.");
            if (ImGui.Button("Use a saved one", new Num.Vector2(220, 48))) _addCapFromSaved = true;
            ImGui.SameLine();
            if (Ui.AccentButton("Make one from scratch", new Num.Vector2(220, 48))) { ImGui.CloseCurrentPopup(); StartCapDraft(w); }
        }
        else
        {
            var combos = Services.Defs.All<EndCapComboDef>().OrderBy(c => c.DisplayName).ToList();
            if (combos.Count == 0) Ui.Hint("Nothing saved yet. Make one from scratch: it's saved for every wall to use.");
            else
            {
                Ui.Hint("Pick one. It stays linked: improve the saved one and every wall using it updates.");
                ImGui.BeginChild("combos", new Num.Vector2(460, Math.Min(260, combos.Count * 26 + 12)), ImGuiChildFlags.Borders);
                foreach (var c in combos)
                    if (ImGui.Selectable($"{c.DisplayName}    ({RowSummary(c.Rows)})##{c.Id}"))
                    {
                        w.EndCapVariants.Add(new EndCapVariant { Name = c.DisplayName, Style = EndCapStyle.Stencil, Combo = c.Id });
                        _variantTab = w.EndCapVariants.Count - 1;
                        _rowTab = 0;
                        PreviewChanged();
                        Status($"Added '{c.DisplayName}' to this wall's ends. Save the wall to keep it.");
                        ImGui.CloseCurrentPopup();
                    }
                ImGui.EndChild();
            }
            if (ImGui.Button("< Back")) _addCapFromSaved = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(100, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private static string RowSummary(List<EndCapRow> rows) =>
        string.Join(", ", rows.Select(r => r.Mode switch { EndCapRowMode.Stencil => "mask", EndCapRowMode.Hide => "hide", _ => "keep" }));

    private void StartCapDraft(WallDef w)
    {
        if (w.EndCapVariants.Count == 0 || !w.EndCapVariants[0].IsStraight) w.EndCapVariants.Insert(0, EndCapVariant.Straight());
        _capDraft = new EndCapVariant { Name = $"End {w.EndCapVariants.Count + 1}", Style = EndCapStyle.Stencil };
        w.EndCapVariants.Add(_capDraft);
        _variantTab = w.EndCapVariants.Count - 1;
        _rowTab = 0;
        _compareVariants = false;
        _capDraftAsCombo = true;
        BuildPreview();
    }

    private bool CapDraftPanel(WallDef w)
    {
        var d = _capDraft!;
        Ui.Colored(Ui.Accent, "New wall end, from scratch");
        Ui.Hint("The view shows one wall stopping on the right, in this wall's own picture and settings. Shape the end cell by cell.");
        bool changed = NameRow(d.Name, n => d.Name = n);
        changed |= EndCapRows(d.Rows);
        ImGui.Spacing();
        ImGui.Checkbox("Also save it as a combo other walls can use", ref _capDraftAsCombo);
        return changed;
    }

    private void CapDraftFooter(WallDef w)
    {
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_capDraft!.Name));
        if (Ui.AccentButton("Save & add to wall", new Num.Vector2(180, 0))) EndCapDraft(keep: true);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(100, 0))) EndCapDraft(keep: false);
    }

    /// <summary>Leaves the from-scratch view: keeps the new end (saved) or throws it away.</summary>
    private void EndCapDraft(bool keep)
    {
        var w = _wall;
        var d = _capDraft;
        _capDraft = null;
        if (w is not null && d is not null)
        {
            if (!keep) { w.EndCapVariants.Remove(d); _variantTab = 0; }
            else
            {
                d.Name = d.Name.Trim();
                if (_capDraftAsCombo)
                {
                    var c = new EndCapComboDef { Name = d.Name, Rows = d.Rows.Select(ChasmJson.Clone).ToList() };
                    c.Id = Services.Defs.SuggestId(typeof(EndCapComboDef), c.Name);
                    Services.Defs.Save(c);
                    d.Combo = c.Id;
                    d.Rows = new();
                }
                _variantTab = w.EndCapVariants.IndexOf(d);
            }
        }
        _rowTab = 0;
        BuildPreview();
        FitView();
        if (!keep || w is null || d is null) return;
        if (!_wallIsNew && !string.IsNullOrWhiteSpace(w.Name) && !string.IsNullOrEmpty(w.Texture))
        {
            SaveWall();
            Status($"Added '{d.Name}' to {w.DisplayName} and saved{(_capDraftAsCombo ? " (also saved as a combo for other walls)" : "")}.");
        }
        else Status($"Added '{d.Name}'. Save the wall to keep it.");
    }

    /// <summary>
    /// The from-scratch view: a short straight wall that stops on the right next to a 'no wall'
    /// gap (masks are drawn for a right-hand end), zoomed in on the end.
    /// </summary>
    private void BuildCapDraftPreview()
    {
        var w = _wall!;
        bool up = w.Kind == MaterialKind.WallUp;
        var walk = new List<CellCoord>();
        var gaps = new List<CellCoord>();
        for (int q = 9; q <= 15; q++) for (int r = -2; r <= 0; r++) walk.Add(new(q, r));
        for (int q = 13; q <= 15; q++) gaps.Add(new(q, 1));      // the wall stops here...
        for (int r = -2; r <= 0; r++) gaps.Add(new(16, r));      // ...and no right faces
        int depth = Math.Max(1, w.Depth);
        // Walls up: the same shape mirrored upward. Then slide it to sit in the middle of the tile.
        CellCoord Map(CellCoord c, int k) => up ? new(-c.R + k, -c.Q + k) : new(c.Q + k, c.R + k);
        int best = 0;
        float bestD = float.MaxValue;
        for (int k = -40; k <= 40; k++)
        {
            var all = walk.Concat(gaps).Select(c => Map(c, k)).ToList();
            var column = Enumerable.Range(0, depth).Select(i => Map(new CellCoord(12 + i, 1 + i), k));
            if (!all.Concat(column).All(IsoGrid.InTile)) continue;
            float y = all.Average(c => IsoGrid.CellCenter(c).Y) + (up ? -1 : 1) * depth * IsoGrid.HalfH;
            float dist = Math.Abs(y - 540);
            if (dist < bestD) { bestD = dist; best = k; }
        }
        var t = new TileDef { Id = "__wall_builder", Name = "Wall end" };
        foreach (var c in IsoGrid.AllCells) t.SetWalkable(c, false);
        var cells = walk.Select(c => Map(c, best)).ToList();
        foreach (var c in cells) t.SetWalkable(c, true);
        var paint = new TileWallPaint { Side = up ? "up" : "down", Wall = TileWallPaint.NoWall };
        paint.Cells.AddRange(gaps.Select(c => Map(c, best)));
        t.WallPaint.Add(paint);
        FinishPreview(t, cells);
        if (_previewJunctions.FirstOrDefault(j => j.Part == WallPart.EndCap) is { } end)
            FocusOn(new Vector2(end.Point.X - 40, end.Point.Y), 2.4f);
    }

    /// <summary>Zooms to a canvas point and puts it in the middle of the map area.</summary>
    private void FocusOn(Vector2 canvasPoint, float zoom)
    {
        var c = Services.Canvas;
        _zoomTarget = null;
        _panTarget = null;
        c.Zoom = zoom;
        c.Pan = Vector2.Zero;
        c.UpdateDestination();
        c.Pan = c.Area.Center.ToVector2() - c.CanvasToWindow(canvasPoint);
        c.UpdateDestination();
    }
}
