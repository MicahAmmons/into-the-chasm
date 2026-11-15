using Chasm.Core.Grid;
using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Game.Input;
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
/// Walls page: the floor's Walls down / Walls up lists (first = drawn automatically along every
/// edge, others painted onto cells), fades, and the wall editor with its live preview backdrop.
/// </summary>
public sealed partial class MapEditorScene
{
    /// <summary>Wall id the map paints onto cells ("-" = no wall); null = not painting.</summary>
    private string? _wallBrush;
    private string? _openWallEntry;
    private bool _openAddWall;
    private string _wallSearch = "";

    private string WallSide => _wallsUp ? "up" : "down";
    private List<FloorWall> WallList => _wallsUp ? _floor!.WallsUp : _floor!.WallsDown;

    private WallDef? WallOf(string id) =>
        _wall is not null && _wall.Id == id ? _wall : Services.Defs.TryGet<WallDef>(id, out var w) ? w : null;

    private void WallsPanel()
    {
        if (_floor is null) { ImGui.TextDisabled("This tile has no floor."); return; }
        SideSwitch("Walls down", "Walls up", ref _wallsUp,
            "Cliffs under walkway edges: they fade into the bottom clouds.", "Walls rising above walkway edges: they fade into the top clouds.");
        var kind = _wallsUp ? MaterialKind.WallUp : MaterialKind.WallDown;
        ImGui.BeginChild("walls");

        // ---- default fade for this side
        Ui.Section(_wallsUp ? "Fade into the top clouds (floor default)" : "Fade into the bottom clouds (floor default)");
        Ui.Hint("Used by every wall on this side unless the wall has its own fade (below).");
        var wf = _wallsUp ? _floor.WallUpFade : _floor.WallDownFade;
        float fs = wf.Start, fl = wf.Length;
        var fa = wf.Angle;
        ImGui.PushID("floorfade");
        if (FadeControls(120, ref fs, ref fl, ref fa, !_wallsUp)) { wf.Start = fs; wf.Length = fl; wf.Angle = fa; Edited(); Services.TileVisuals.InvalidateAll(); }
        ImGui.PopID();

        // ---- the floor's walls
        Ui.Section(_wallsUp ? "Walls up on this floor" : "Walls down on this floor");
        Ui.Hint("The first wall is drawn automatically along every edge, on every tile. Others are painted onto cells with Assign.");
        var list = WallList;
        int up = -1, down = -1, remove = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            var w = WallOf(e.Wall);
            ImGui.PushID("fw" + i);
            float rowRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
            if (ImGui.ArrowButton("up", ImGuiDir.Up)) up = i;
            ImGui.SameLine(0, 2);
            if (ImGui.ArrowButton("down", ImGuiDir.Down)) down = i;
            ImGui.SameLine();
            if (w is not null) { Thumb(w.Texture, 26); ImGui.SameLine(); }
            bool open = _openWallEntry == e.Wall;
            string fadeTag = e.FadeMode switch { WallFadeMode.Own => "  own fade", WallFadeMode.None => "  no fade", _ => "" };
            string label = (w?.DisplayName ?? $"missing '{e.Wall}'") + (i == 0 ? "  [automatic]" : "") + fadeTag;
            if (ImGui.Selectable(label + "##sel", open, ImGuiSelectableFlags.None, new Num.Vector2(rowRight - ImGui.GetCursorPosX() - 190, 26))) _openWallEntry = open ? null : e.Wall;
            if (w is not null && ImGui.IsItemHovered()) WallTooltip(w);
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), rowRight - 184));
            if (i > 0)
            {
                bool painting = _wallBrush == e.Wall;
                if (Ui.ToggleButton(painting ? "Done" : "Assign", painting, new Num.Vector2(64, 0), "Paint this wall onto cells (click / drag on the map; again to un-paint). Esc when done."))
                    ToggleBrush(painting ? null : e.Wall);
            }
            else ImGui.Dummy(new Num.Vector2(64, 0));
            ImGui.SameLine(0, 4);
            if (ImGui.Button("Edit", new Num.Vector2(50, 0)) && w is not null) OpenWallEditor(w, isNew: false);
            ImGui.SameLine(0, 4);
            if (ImGui.Button("x", new Num.Vector2(28, 0))) remove = i;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Take off this floor (the wall stays in the library).");
            if (open)
            {
                ImGui.Indent();
                WallEntryFade(e);
                ImGui.Unindent();
                ImGui.Spacing();
            }
            ImGui.PopID();
        }
        if (list.Count == 0) ImGui.TextDisabled("No walls yet: add one below.");
        if (up > 0) { (list[up - 1], list[up]) = (list[up], list[up - 1]); Edited(); }
        if (down >= 0 && down < list.Count - 1) { (list[down + 1], list[down]) = (list[down], list[down + 1]); Edited(); }
        if (remove >= 0) { var id = list[remove].Wall; list.RemoveAt(remove); _tile?.WallPaint.RemoveAll(p => p.Wall == id && p.Side == WallSide); if (_wallBrush == id) _wallBrush = null; Edited(); }

        if (Ui.AccentButton("+ New wall")) StartNewWall(kind);
        ImGui.SameLine();
        if (ImGui.Button("+ Add from library...")) { _openAddWall = true; _wallSearch = ""; }
        AddWallPopup(kind);

        // ---- painting exceptions on this tile
        Ui.Section("This tile");
        int painted = _tile!.WallPaint.Where(p => p.Side == WallSide).Sum(p => p.Cells.Count);
        bool erasing = _wallBrush == TileWallPaint.NoWall;
        if (Ui.ToggleButton(erasing ? "Done (Esc)" : "No wall here...", erasing, new Num.Vector2(150, 0), "Paint cells where no wall should be drawn (e.g. a hidden gap)."))
            ToggleBrush(erasing ? null : TileWallPaint.NoWall);
        ImGui.SameLine();
        ImGui.BeginDisabled(painted == 0);
        if (ImGui.Button("Clear painted cells")) { _tile.WallPaint.RemoveAll(p => p.Side == WallSide); Edited(); }
        ImGui.EndDisabled();
        Ui.Colored(Ui.Muted, painted == 0 ? "Every edge uses the automatic wall." : $"{painted} cell(s) painted with another wall or 'no wall'.");

        // ---- one-off pieces
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("One-off pieces (prefabs)")) OneOffPiecesSection(kind);
        ImGui.EndChild();
    }

    private void ToggleBrush(string? brush)
    {
        ClearMapModes();
        _wallBrush = brush;
        if (brush is not null) Status(brush == TileWallPaint.NoWall ? "Painting 'no wall' cells. Esc when done." : $"Painting {WallOf(brush)?.DisplayName ?? brush} onto cells. Esc when done.");
    }

    private void WallEntryFade(FloorWall e)
    {
        int mode = (int)e.FadeMode;
        if (ImGui.RadioButton("Floor default", mode == 0)) { e.FadeMode = WallFadeMode.Default; Edited(); }
        ImGui.SameLine();
        if (ImGui.RadioButton("Own fade", mode == 1))
        {
            if (e.FadeMode != WallFadeMode.Own)
            {
                var d = _wallsUp ? _floor!.WallUpFade : _floor!.WallDownFade;
                e.Fade = new WallFade { Start = d.Start, Length = d.Length, Angle = d.Angle };
            }
            e.FadeMode = WallFadeMode.Own; Edited();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("No fade", mode == 2)) { e.FadeMode = WallFadeMode.None; Edited(); }
        if (e.FadeMode == WallFadeMode.Own)
        {
            float s = e.Fade.Start, l = e.Fade.Length;
            var a = e.Fade.Angle;
            if (FadeControls(120, ref s, ref l, ref a, !_wallsUp)) { e.Fade.Start = s; e.Fade.Length = l; e.Fade.Angle = a; Edited(); }
        }
    }

    private void WallTooltip(WallDef w)
    {
        ImGui.BeginTooltip();
        ImageInline(w.Texture, 220);
        ImGui.TextUnformatted($"{w.DisplayName}  [{w.Id}]\n{w.Category}   zoom {w.Scale:0.##}x   depth {w.Depth}{(w.Rim ? "   rim" : "")}");
        ImGui.EndTooltip();
    }

    private void AddWallPopup(MaterialKind kind)
    {
        if (_openAddWall) { ImGui.OpenPopup("Add wall to floor"); _openAddWall = false; }
        CenterNextWindow();
        ImGui.SetNextWindowSize(new Num.Vector2(540, 520), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("Add wall to floor")) return;
        Ui.Colored(Ui.Muted, $"The wall library ({(kind == MaterialKind.WallUp ? "walls up" : "walls down")}).");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ws", "search...", ref _wallSearch, 60);
        ImGui.BeginChild("wlib", new Num.Vector2(0, -36), ImGuiChildFlags.Borders);
        var have = WallList.Select(x => x.Wall).ToHashSet();
        var all = Services.Defs.All<WallDef>().Where(w => w.Kind == kind && !have.Contains(w.Id))
            .Where(w => _wallSearch.Length == 0 || w.DisplayName.Contains(_wallSearch, StringComparison.OrdinalIgnoreCase) || w.Category.Contains(_wallSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        if (all.Count == 0) ImGui.TextDisabled("No other walls of this kind. Use + New wall.");
        foreach (var g in all.GroupBy(w => string.IsNullOrWhiteSpace(w.Category) ? "(no category)" : w.Category).OrderBy(g => g.Key))
        {
            Ui.Section(g.Key);
            foreach (var w in g.OrderBy(w => w.DisplayName))
            {
                ImGui.PushID(w.Id);
                Thumb(w.Texture, 40);
                ImGui.SameLine();
                if (ImGui.Selectable($"{w.DisplayName}  [{w.Id}]", false, ImGuiSelectableFlags.None, new Num.Vector2(ImGui.GetContentRegionAvail().X - 76, 40)))
                {
                    WallList.Add(new FloorWall { Wall = w.Id });
                    Edited();
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.IsItemHovered()) WallTooltip(w);
                ImGui.SameLine();
                if (ImGui.Button("Delete", new Num.Vector2(70, 0))) { ImGui.CloseCurrentPopup(); AskDeleteWall(w); }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("Close", new Num.Vector2(120, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    /// <summary>Map input while painting walls onto cells. True = used.</summary>
    private bool UpdateWallBrush(InputState input)
    {
        if (_wallBrush is not { } brush) return false;
        if (_floor is null) { _wallBrush = null; return false; }
        if (input.Pressed(InputAction.Cancel) && !input.KeyboardCapturedByUi) { _wallBrush = null; Status("Done painting walls."); return true; }
        if (input.LeftReleased || input.RightReleased) _dragMode = null;
        var t = _tile!;
        string side = WallSide;
        TileWallPaint Entry(string wall)
        {
            var e = t.WallPaint.FirstOrDefault(p => p.Side == side && p.Wall == wall);
            if (e is null) { e = new TileWallPaint { Side = side, Wall = wall }; t.WallPaint.Add(e); }
            return e;
        }
        PaintCells(input, c => t.WallPaint.Any(p => p.Side == side && p.Wall == brush && p.Cells.Contains(c)), (c, v) =>
        {
            foreach (var p in t.WallPaint.Where(p => p.Side == side)) p.Cells.Remove(c);
            if (v) Entry(brush).Cells.Add(c);
            t.WallPaint.RemoveAll(p => p.Cells.Count == 0);
        }, rightPaintsFalse: true);
        return true;
    }

    /// <summary>Painted wall cells on the map (this side), coloured per wall.</summary>
    private void DrawWallPaint(SpriteBatch b)
    {
        if (_panel != Panel.Walls || _tile is null || _floor is null) return;
        var d = Services.Draw;
        foreach (var p in _tile.WallPaint.Where(p => p.Side == WallSide))
        {
            var col = p.Wall == TileWallPaint.NoWall ? new Color(230, 60, 60) : ColorFor(p.Wall);
            foreach (var c in p.Cells)
            {
                d.DiamondFill(b, IsoGrid.CellCenter(c), 100, 50, col * (_wallBrush is null ? 0.18f : 0.32f));
                d.DiamondOutline(b, IsoGrid.CellCenter(c), 112, 56, col, 2);
            }
        }
    }

    private static Color ColorFor(string id)
    {
        int h = id.Aggregate(17, (a, ch) => a * 31 + ch);
        var rnd = new Random(h);
        return new Color(90 + rnd.Next(160), 90 + rnd.Next(160), 90 + rnd.Next(160));
    }

    // ------------------------------------------------------------------ wall editor + live preview

    private enum Backdrop { Blank, Grid, ThisTile, Sample }

    private WallDef? _wall;
    private bool _wallIsNew;
    private Backdrop _backdrop = Backdrop.Sample;
    private TileDef? _previewTile;
    private FloorDef? _previewFloor;
    private int _sampleSeed = 1;
    private bool _openBackdrop, _previewPaint = true;
    /// <summary>Grid, walkable outlines, outlines and captions in the wall / prefab previews. Off = as in game.</summary>
    private bool _previewHelpers;
    private (float taper, float squash) _wallTaper;

    private void StartNewWall(MaterialKind kind)
    {
        var w = new WallDef { Kind = kind, LipLine = kind == MaterialKind.WallUp ? 1f : 0f, Category = kind == MaterialKind.WallUp ? "Walls" : "Cliffs" };
        OpenWallEditor(w, isNew: true);
        _openBackdrop = true; // ask which preview to use
        Art.ArtBrowser.OpenPick(null, k =>
        {
            if (_wall is null) return;
            _wall.Texture = k;
            if (string.IsNullOrWhiteSpace(_wall.Name)) _wall.Name = Path.GetFileNameWithoutExtension(k);
            PreviewChanged();
        });
    }

    private void OpenWallEditor(WallDef w, bool isNew)
    {
        _wall = isNew ? w : ChasmJson.Clone(w);
        _wallIsNew = isNew;
        _wallTaper = (0, 0);
        ClearMapModes();
        _wallBrush = null;
        _panel = Panel.WallEdit;
        BuildPreview();
    }

    private bool WallDirty => _wall is not null && (_wallIsNew
        || !Services.Defs.TryGet<WallDef>(_wall.Id, out var saved) || ChasmJson.Serialize(saved) != ChasmJson.Serialize(_wall));

    /// <summary>(Re)builds the preview tile + floor for the chosen backdrop.</summary>
    private void BuildPreview()
    {
        var w = _wall!;
        bool up = w.Kind == MaterialKind.WallUp;
        TileDef t;
        if (_backdrop == Backdrop.ThisTile && _tile is not null)
        {
            t = ChasmJson.Clone(_tile);
            t.Id = "__wall_preview";
            t.WallPaint.Clear();
        }
        else
        {
            t = new TileDef { Id = "__wall_preview", Name = "Wall preview" };
            foreach (var c in IsoGrid.AllCells) t.SetWalkable(c, false);
            if (_backdrop == Backdrop.Sample) foreach (var c in SampleWalkway(_sampleSeed)) t.SetWalkable(c, true);
            else foreach (var c in SampleWalkway(0, small: true)) t.SetWalkable(c, true);
            t.LocalMaterials.Add(new MaterialDef { Id = "ground", Name = "Preview ground", Texture = "placeholder/textures/grass.png" });
            t.PaintRegions.Add(new PaintRegion { Id = "ground", Material = "local:ground" });
        }
        t.InvalidateCaches();
        var f = _floor is null ? new FloorDef { Id = "__preview_floor" } : ChasmJson.Clone(_floor);
        f.Id = "__preview_floor";
        f.WallsDown = new(); f.WallsUp = new();
        (up ? f.WallsUp : f.WallsDown).Add(new FloorWall { Wall = w.Id.Length > 0 ? w.Id : "__new_wall" });
        if (_backdrop is Backdrop.Blank or Backdrop.Grid) { f.Clouds = new(); f.TopClouds.Clear(); f.BackgroundColor = "#14161f"; }
        _previewTile = t;
        _previewFloor = f;
        SyncPreviewGround();
        PreviewChanged();
    }

    /// <summary>Keeps the preview's ground texture on exactly its walkable cells (generated backdrops only).</summary>
    private void SyncPreviewGround()
    {
        if (_previewTile is null || _backdrop == Backdrop.ThisTile) return;
        var ground = _previewTile.PaintRegions.FirstOrDefault(r => r.Id == "ground");
        if (ground is null) return;
        ground.Cells = IsoGrid.AllCells.Where(c => _previewTile.Walkable[c]).ToList();
        ground.Opacity = _backdrop == Backdrop.Sample ? 1f : 0.35f;
    }

    private void PreviewChanged()
    {
        if (_previewTile is null || _wall is null) return;
        var w = _wall;
        string id = w.Id.Length > 0 ? w.Id : "__new_wall";
        foreach (var e in _previewFloor!.WallsDown.Concat(_previewFloor.WallsUp)) e.Wall = id;
        Services.TileVisuals.WallOverride = x => x == id ? w : null;
        Services.TileVisuals.Invalidate(_previewTile);
    }

    /// <summary>A random-looking walkway: a plateau, a couple of paths and a notch (seed 0 + small = a simple shape).</summary>
    private static List<CellCoord> SampleWalkway(int seed, bool small = false)
    {
        var centre = new CellCoord(16, 1);
        var cells = new HashSet<CellCoord>();
        void Blob(CellCoord c, int r) { foreach (var x in IsoGrid.AllCells) if (Math.Abs(x.Q - c.Q) + Math.Abs(x.R - c.R) <= r) cells.Add(x); }
        if (small) { Blob(centre, 2); cells.Remove(new CellCoord(17, 2)); return cells.ToList(); }
        var rnd = new Random(seed * 7919 + 13);
        Blob(centre, 2 + rnd.Next(2));
        // Paths wandering out along the two diagonals.
        foreach (var (dq, dr) in new[] { (1, 0), (0, -1), (-1, 0), (0, 1) }.OrderBy(_ => rnd.Next()).Take(2 + rnd.Next(2)))
        {
            var c = centre;
            int len = 4 + rnd.Next(6);
            for (int i = 0; i < len; i++)
            {
                c = new CellCoord(c.Q + dq, c.R + dr);
                if (rnd.Next(4) == 0) c = new CellCoord(c.Q + (dr != 0 ? rnd.Next(3) - 1 : 0), c.R + (dq != 0 ? rnd.Next(3) - 1 : 0));
                if (!IsoGrid.InTile(c)) break;
                cells.Add(c);
                if (rnd.Next(3) == 0) cells.Add(new CellCoord(c.Q + dr, c.R + dq)); // widen a little
            }
            if (rnd.Next(2) == 0) Blob(c, 1);
        }
        // A notch for an inner corner.
        cells.Remove(new CellCoord(centre.Q + 1, centre.R + 1));
        return cells.Where(IsoGrid.InTile).ToList();
    }

    private void WallEditPanel()
    {
        if (_wall is not { } w) { _panel = Panel.Walls; return; }
        const float label = 130;
        bool changed = false;
        Ui.Colored(_wallIsNew ? Ui.Warn : Ui.Muted, _wallIsNew ? "New wall (not saved yet)" : $"content/defs/walls/{w.Id}.json");

        // Preview controls
        ImGui.TextUnformatted("Preview on"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] backs = { "Blank", "Grid only", "This tile (a copy)", "Basic sample" };
        int bk = (int)_backdrop;
        if (ImGui.Combo("##backdrop", ref bk, backs, backs.Length)) { _backdrop = (Backdrop)bk; BuildPreview(); }
        if (_backdrop == Backdrop.Sample) { if (ImGui.Button("Reshuffle sample")) { _sampleSeed++; BuildPreview(); } ImGui.SameLine(); }
        if (Ui.ToggleButton(_previewPaint ? "Painting walkable" : "Paint walkable", _previewPaint, new Num.Vector2(170, 0),
                "Click / drag on the map to add or remove walkable cells in the preview. The wall builds itself around them.")) _previewPaint = !_previewPaint;
        ImGui.SameLine();
        if (Ui.ToggleButton("Grid & highlights", _previewHelpers, new Num.Vector2(0, 0), "Show the grid, walkable outlines and captions (H).\nOff = the preview looks as it will in the game.")) _previewHelpers = !_previewHelpers;
        Ui.Hint("The preview is a copy: painting here never changes your real tile.");
        ImGui.BeginChild("walled", new Num.Vector2(0, -40));

        ImGui.TextUnformatted("Name"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string name = w.Name;
        if (ImGui.InputText("##wname", ref name, 80)) w.Name = name;
        ImGui.TextUnformatted("Category"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string cat = w.Category;
        if (ImGui.InputTextWithHint("##wcat", "e.g. Cliffs", ref cat, 60)) w.Category = cat;
        ImGui.TextUnformatted("Kind"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] kinds = { "Wall down (cliff)", "Wall up" };
        int k = w.Kind == MaterialKind.WallUp ? 1 : 0;
        if (ImGui.Combo("##wkind", ref k, kinds, kinds.Length)) { w.Kind = k == 1 ? MaterialKind.WallUp : MaterialKind.WallDown; BuildPreview(); }

        Ui.Section("Picture (the left face)");
        ImGui.TextUnformatted("Texture"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string tex = w.Texture;
        ImGui.PushID("wtex");
        if (ToolContext.Wizards.Inspector.TexturePicker(ref tex)) { w.Texture = tex; changed = true; }
        ImGui.PopID();
        changed |= SliderRow("Zoom", label, () => w.Scale, v => w.Scale = v, 0.1f, 4f, "%.2fx", log: true);
        changed |= SliderRow(w.Kind == MaterialKind.WallUp ? "Foot line" : "Lip line", label, () => w.LipLine, v => w.LipLine = v, 0, 1, "%.2f");
        ImGui.TextUnformatted("Tint"); ImGui.SameLine(label);
        var col = HexToColor(w.Tint);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 64);
        if (ImGui.ColorEdit3("##wtint", ref col, ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.DisplayHex)) { w.Tint = ColorToHex(col); changed = true; }
        ImGui.SameLine(0, 4);
        if (ImGui.Button("Reset##wt")) { w.Tint = "#ffffff"; changed = true; }
        ImGui.TextUnformatted("Flip"); ImGui.SameLine(label);
        bool fx = w.FlipX, fy = w.FlipY;
        if (ImGui.Checkbox("Flip X##w", ref fx)) { w.FlipX = fx; changed = true; }
        ImGui.SameLine(0, 20);
        if (ImGui.Checkbox("Flip Y##w", ref fy)) { w.FlipY = fy; changed = true; }

        Ui.Section("Sides");
        changed |= SliderRow("Left shade", label, () => w.LeftShade, v => w.LeftShade = v, 0.2f, 1.5f, "%.2f");
        changed |= SliderRow("Right shade", label, () => w.RightShade, v => w.RightShade = v, 0.2f, 1.5f, "%.2f");
        ImGui.TextUnformatted("Right face art"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string rtex = w.RightTexture;
        ImGui.PushID("wrtex");
        if (ToolContext.Wizards.Inspector.TexturePicker(ref rtex)) { w.RightTexture = rtex; changed = true; }
        ImGui.PopID();
        if (string.IsNullOrEmpty(w.RightTexture)) Ui.Hint("Empty = the left face, mirrored (usually all you need).");

        Ui.Section("Shape");
        int depth = w.Depth, run = w.MaxRun;
        ImGui.TextUnformatted("Depth"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderInt("##wdepth", ref depth, 1, 10, "%d cells")) { w.Depth = depth; changed = true; }
        ImGui.TextUnformatted("Piece length"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderInt("##wrun", ref run, 1, 24, "up to %d cells")) { w.MaxRun = run; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Rule of thumb for this picture: about {Math.Max(1, (int)(Services.Textures.Get(w.Texture).Width * w.Scale / 64) - 1)} cells before it runs out.");
        bool skew = w.SkewFaces;
        if (ImGui.Checkbox("Slant faces along the cell edges", ref skew)) { w.SkewFaces = skew; changed = true; }
        changed |= SliderRow("Corner seam", label, () => w.CornerBlend, v => w.CornerBlend = v, 0, 80, "%.0f px");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Where a left and right face meet: 0 = crisp edge, higher = softer blend.");
        ImGui.TextUnformatted("End caps"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] caps = { "None (hard cut)", "Soft (fades out)", "Stencil (carved shape)" };
        int cap = (int)w.EndCaps;
        if (ImGui.Combo("##wcap", ref cap, caps, caps.Length)) { w.EndCaps = (EndCapStyle)cap; changed = true; }
        if (w.EndCaps == EndCapStyle.Soft) changed |= SliderRow("Cap softness", label, () => w.EndCapSoftness, v => w.EndCapSoftness = v, 4, 128, "%.0f px");
        if (w.EndCaps == EndCapStyle.Stencil)
        {
            ImGui.TextUnformatted("Cap stencil"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
            string st = w.EndCapStencil;
            ImGui.PushID("wcapst");
            if (ToolContext.Wizards.Inspector.TexturePicker(ref st)) { w.EndCapStencil = st; changed = true; }
            ImGui.PopID();
        }
        bool rim = w.Rim;
        if (ImGui.Checkbox("Rim over the walkway edge", ref rim)) { w.Rim = rim; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A thin strip of the picture's top laid over the walkway's edge, hiding the seam.");
        if (w.Rim) changed |= SliderRow("Rim height", label, () => w.RimHeight, v => w.RimHeight = v, 2, 64, "%.0f px");

        Ui.Section("Perspective (optional)");
        bool warp = w.Warp is not null;
        if (ImGui.Checkbox("Perspective warp", ref warp)) { w.Warp = warp ? new MaterialWarp() : null; changed = true; }
        if (w.Warp is not null)
        {
            var t = _wallTaper;
            var texObj = Services.Textures.Get(w.Texture);
            ImGui.TextUnformatted("Taper"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
            bool tc = ImGui.SliderFloat("##wtaper", ref t.taper, -0.9f, 0.9f, t.taper >= 0 ? "bottom %.2f" : "top %.2f");
            ImGui.TextUnformatted("Squash"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
            tc |= ImGui.SliderFloat("##wsquash", ref t.squash, 0f, 0.8f, "%.2f");
            if (tc) { _wallTaper = t; w.Warp = MaterialWarp.Taper(t.taper, t.squash, texObj.Width * w.Scale, texObj.Height * w.Scale); changed = true; }
            if (ImGui.Button("Reset warp")) { w.Warp = new MaterialWarp(); _wallTaper = (0, 0); changed = true; }
        }
        ImGui.EndChild();
        if (changed) PreviewChanged();

        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(w.Name) || string.IsNullOrEmpty(w.Texture));
        if (Ui.AccentButton(WallDirty ? "Save wall *" : "Save wall", new Num.Vector2(130, 0))) SaveWall();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (!_wallIsNew && ImGui.Button("Duplicate"))
        {
            var copy = ChasmJson.Clone(w);
            copy.Id = ""; copy.Name = w.Name + " copy";
            _wall = copy; _wallIsNew = true;
            PreviewChanged();
            Status("Duplicated. Change what you like and Save.");
        }
        if (!_wallIsNew) ImGui.SameLine();
        if (!_wallIsNew && ImGui.Button("Delete...") && Services.Defs.TryGet<WallDef>(w.Id, out var saved)) AskDeleteWall(saved);
        ImGui.SameLine();
        if (ImGui.Button("Close")) CloseWallEditor();
        BackdropPopup();
    }

    private bool SliderRow(string name, float label, Func<float> get, Action<float> set, float min, float max, string fmt, bool log = false)
    {
        ImGui.TextUnformatted(name); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        float v = get();
        if (!ImGui.SliderFloat("##" + name, ref v, min, max, fmt, log ? ImGuiSliderFlags.Logarithmic : ImGuiSliderFlags.None)) return false;
        set(v);
        return true;
    }

    private void SaveWall()
    {
        var w = _wall!;
        if (string.IsNullOrWhiteSpace(w.Id)) w.Id = Services.Defs.SuggestId(typeof(WallDef), w.Name);
        Services.Defs.Save(ChasmJson.Clone(w));
        if (_wallIsNew && _floor is not null)
        {
            var list = w.Kind == MaterialKind.WallUp ? _floor.WallsUp : _floor.WallsDown;
            if (list.All(x => x.Wall != w.Id)) list.Add(new FloorWall { Wall = w.Id });
            Edited();
        }
        _wallIsNew = false;
        PreviewChanged();
        Services.TileVisuals.InvalidateAll();
        Status($"Saved wall '{w.DisplayName}'. Every tile using it updates.");
    }

    private void CloseWallEditor()
    {
        _wall = null;
        _previewTile = null;
        Services.TileVisuals.WallOverride = null;
        Services.TileVisuals.InvalidateAll();
        _panel = Panel.Walls;
    }

    private void BackdropPopup()
    {
        if (_openBackdrop) { ImGui.OpenPopup("Preview the wall on..."); _openBackdrop = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Preview the wall on...", ImGuiWindowFlags.AlwaysAutoResize)) return;
        Ui.Hint("You'll paint walkable cells on it and watch the wall build itself. You can switch any time.");
        var size = new Num.Vector2(300, 40);
        void Pick(Backdrop b, string text, string tip)
        {
            if (ImGui.Button(text, size)) { _backdrop = b; BuildPreview(); ImGui.CloseCurrentPopup(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
        }
        Pick(Backdrop.Blank, "Blank", "Nothing but the wall on a plain background.");
        Pick(Backdrop.Grid, "Grid only", "The cell grid, so you can judge sizes.");
        Pick(Backdrop.ThisTile, "This tile (a copy)", "The tile you're working on, with its own walkable cells.");
        Pick(Backdrop.Sample, "Basic sample", "A generated walkway with paths and corners (reshuffle any time).");
        ImGui.EndPopup();
    }

    /// <summary>Map input in the wall editor: paint the preview's walkable cells.</summary>
    private bool UpdateWallPreviewInput(InputState input)
    {
        if (_panel != Panel.WallEdit || _previewTile is null) return false;
        if (input.KeyPressed(Microsoft.Xna.Framework.Input.Keys.H) && !input.KeyboardCapturedByUi) _previewHelpers = !_previewHelpers;
        if (!_previewPaint) return true;
        if (input.LeftReleased || input.RightReleased) _dragMode = null;
        bool before = false;
        PaintCells(input, c => _previewTile.Walkable[c], (c, v) => { _previewTile.SetWalkable(c, v); before = true; }, rightPaintsFalse: true, editsTile: false);
        if (before) { SyncPreviewGround(); Services.TileVisuals.Invalidate(_previewTile); }
        return true;
    }

    /// <summary>The wall editor's preview, drawn instead of the tile.</summary>
    private void DrawWallPreview(SpriteBatch batch)
    {
        var t = _previewTile!;
        var s = Services;
        TileRenderer.QueueLayers(_queue, s, t, _previewFloor, includeProps: _backdrop == Backdrop.ThisTile);
        _queue.Flush(batch);
        if (!_previewHelpers) return; // clean: exactly what the game draws
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        var d = s.Draw;
        bool grid = _backdrop != Backdrop.Blank;
        foreach (var c in IsoGrid.AllCells)
        {
            var p = IsoGrid.CellCenter(c);
            if (grid) d.DiamondOutline(batch, p, IsoGrid.CellWidth, IsoGrid.CellHeight, EditorPrefs.ToColor(_prefs.GridColor));
            if (_previewPaint && t.Walkable[c]) d.DiamondOutline(batch, p, 110, 55, WalkGreen * 0.55f, 1.5f);
        }
        d.TextShadowed(batch, $"Wall preview: {_wall!.DisplayName}   (paint walkable cells; right-click removes)", new Vector2(24, 24), Color.White, 26 / CanvasScale(), bold: true);
        batch.End();
    }
}
