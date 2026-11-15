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
                WallEntryBottom(e);
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
        if (Ui.ToggleButton(_swapMode ? "Done swapping (Esc)" : "Swap corners & ends", _swapMode, new Num.Vector2(190, 0),
                "Markers appear on corners and wall ends that have more than one variant.\nClick one to switch to its next variant; right-click to go back to the first."))
        { bool on = !_swapMode; ClearMapModes(); _swapMode = on; }
        if (_swapMode)
        {
            ImGui.Checkbox("Corners & ends", ref _swapCorners);
            ImGui.SameLine();
            ImGui.Checkbox("Bottom edges", ref _swapBottoms);
        }
        int swapped = _tile.WallVariants.Count(v => v.Side == WallSide);
        if (swapped > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button($"Reset {swapped} swap(s)")) { _tile.WallVariants.RemoveAll(v => v.Side == WallSide); Edited(); }
        }

        // ---- one-off pieces
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("One-off pieces (prefabs)")) OneOffPiecesSection(kind);
        ImGui.EndChild();
    }

    // ------------------------------------------------------------------ swapping variants on this tile

    private bool _swapMode, _swapCorners = true, _swapBottoms = true;

    private List<WallJunction> TileJunctions()
    {
        var list = new List<WallJunction>();
        if (_tile is null || _floor is null) return list;
        Services.TileVisuals.WallsFor(_tile, _floor, list);
        return list.Where(j => j.Up == _wallsUp && j.Count > 1 && (j.Part == WallPart.BottomEdge ? _swapBottoms : _swapCorners)).ToList();
    }

    /// <summary>Click a marker: next variant. Right-click: back to the first.</summary>
    private bool UpdateWallSwap(InputState input)
    {
        if (!_swapMode) return false;
        if (input.Pressed(InputAction.Cancel) && !input.KeyboardCapturedByUi) { _swapMode = false; Status("Done swapping."); return true; }
        if (input.MouseCapturedByUi || !(input.LeftClicked || input.RightClicked)) return true;
        float pick = 20f / CanvasScale();
        var hit = TileJunctions().OrderBy(j => Num.Vector2.Distance(j.Point, _hoverPx)).FirstOrDefault();
        if (hit is null || Num.Vector2.Distance(hit.Point, _hoverPx) > pick) return true;
        var t = _tile!;
        string side = hit.Up ? "up" : "down";
        t.WallVariants.RemoveAll(v => v.Side == side && v.Part == hit.Part && v.Cell.Equals(hit.Cell));
        int next = input.LeftClicked ? (hit.Index + 1) % hit.Count : 0;
        if (next != 0) t.WallVariants.Add(new TileWallVariant { Side = side, Part = hit.Part, Cell = hit.Cell, Index = next });
        Edited();
        string partName = hit.Part switch { WallPart.OuterCorner => "Outer corner", WallPart.InnerCorner => "Inner corner", WallPart.EndCap => "Wall end", _ => "Bottom edge" };
        var entry = WallList.FirstOrDefault(e => e.Wall == hit.Wall.Id);
        string varName = (hit with { Index = next }).VariantName(entry);
        Status($"{partName}: variant {next + 1} of {hit.Count} ({varName}).");
        return true;
    }

    private void DrawSwapMarkers(SpriteBatch b)
    {
        if (!_swapMode || _tile is null) return;
        var d = Services.Draw;
        float px = 1f / CanvasScale();
        foreach (var j in TileJunctions())
        {
            var p = new Vector2(j.Point.X, j.Point.Y);
            var col = j.Index == 0 ? new Color(120, 200, 255) : new Color(255, 190, 70);
            d.DiamondFill(b, p, 34 * px, 34 * px, Color.Black * 0.55f);
            d.DiamondOutline(b, p, 38 * px, 38 * px, col, 2 * px);
            d.TextCentered(b, $"{j.Index + 1}/{j.Count}", p, col, 15 * px, bold: true);
        }
    }

    private void ToggleBrush(string? brush)
    {
        ClearMapModes();
        _wallBrush = brush;
        if (brush is not null) Status(brush == TileWallPaint.NoWall ? "Painting 'no wall' cells. Esc when done." : $"Painting {WallOf(brush)?.DisplayName ?? brush} onto cells. Esc when done.");
    }

    private int _bottomTab;

    /// <summary>The floor's bottom-edge shapes for one wall: where it fades into the clouds.</summary>
    private void WallEntryBottom(FloorWall e)
    {
        ImGui.PushID("bottoms");
        Ui.Section("Bottom edge");
        Ui.Hint("A shape for the bottom where the wall fades away (rock tips, crumbling...). It sits at the fade, so it moves with the cloud level. The first is used everywhere; swap single cells with 'Swap corners & ends'.");
        bool changed = false;
        if (e.BottomEdges.Count == 0)
        {
            if (ImGui.Button("+ Add a bottom edge")) { e.BottomEdges.Add(new BottomEdgeVariant { Name = "Bottom 1", Stencil = "stencils/bottom edges/jagged_rocks.png" }); _bottomTab = 0; changed = true; }
            ImGui.SameLine(); ImGui.TextDisabled("(none: a smooth fade)");
        }
        else
        {
            _bottomTab = Math.Clamp(_bottomTab, 0, e.BottomEdges.Count - 1);
            for (int i = 0; i < e.BottomEdges.Count; i++)
            {
                if (i > 0) ImGui.SameLine(0, 4);
                if (Ui.ToggleButton($"{i + 1}. {e.BottomEdges[i].Name}##bt{i}", _bottomTab == i, new Num.Vector2(0, 0), i == 0 ? "The default" : "Swap in on cells")) _bottomTab = i;
                if (ImGui.GetContentRegionAvail().X < 120) ImGui.NewLine();
            }
            ImGui.SameLine(0, 8);
            if (ImGui.Button("+##addbottom")) { e.BottomEdges.Add(new BottomEdgeVariant { Name = $"Bottom {e.BottomEdges.Count + 1}", Stencil = e.BottomEdges[^1].Stencil }); _bottomTab = e.BottomEdges.Count - 1; changed = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a variant");
            var b = e.BottomEdges[_bottomTab];
            const float label = 110;
            changed |= NameRow(b.Name, n => b.Name = n);
            string st = b.Stencil;
            if (StencilGrid("stencils/bottom edges", ref st, 96, 24)) { b.Stencil = st; changed = true; }
            changed |= SliderRow("Position", label, () => b.Offset, x => b.Offset = x, -4, 4, "%.2f cells");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Up (-) or down (+) from the middle of the fade.");
            changed |= SliderRow("Height", label, () => b.Height, x => b.Height = x, 8, 256, "%.0f px");
            changed |= SliderRow("Repeat width", label, () => b.Repeat, x => b.Repeat = x, 32, 1024, "%.0f px");
            bool fx = b.FlipX;
            if (ImGui.Checkbox("Flip X##bfx", ref fx)) { b.FlipX = fx; changed = true; }
            if (_bottomTab > 0) { ImGui.SameLine(); if (ImGui.Button("Make default")) { e.BottomEdges.RemoveAt(_bottomTab); e.BottomEdges.Insert(0, b); _bottomTab = 0; changed = true; } }
            ImGui.SameLine();
            if (ImGui.Button("Delete##bdel")) { e.BottomEdges.RemoveAt(_bottomTab); _bottomTab = Math.Max(0, _bottomTab - 1); changed = true; }
        }
        if (changed) { Edited(); Services.TileVisuals.InvalidateAll(); }
        ImGui.PopID();
    }

    // ------------------------------------------------------------------ stencil thumbnail grid

    private (uint field, string key)? _gridPicked;

    /// <summary>
    /// Every mask in a folder of art/stencils as clickable thumbnails (no scrolling through all the
    /// art), plus '...' (art library) and '+' (add a new one from your computer, into this folder).
    /// </summary>
    private bool StencilGrid(string folder, ref string key, float w, float h)
    {
        bool changed = false;
        uint id = ImGui.GetID("##grid" + folder);
        if (_gridPicked is { } done && done.field == id) { key = done.key; _gridPicked = null; changed = true; }
        var keys = Services.Textures.AllKeys().Where(k => k.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)).ToList();
        float cell = w + ImGui.GetStyle().FramePadding.X * 2 + 4;
        int perRow = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + 4) / cell));
        for (int i = 0; i < keys.Count; i++)
        {
            if (i % perRow != 0) ImGui.SameLine(0, 4);
            var k = keys[i];
            bool sel = string.Equals(k, key, StringComparison.OrdinalIgnoreCase);
            ImGui.PushStyleColor(ImGuiCol.Button, sel ? Ui.AccentDark : new Num.Vector4(0.16f, 0.18f, 0.24f, 1));
            if (ImGui.ImageButton($"g{folder}{i}", ImGuiExtension.Instance!.Renderer.BindTexture(Services.Textures.Get(k)), new Num.Vector2(w, h))) { key = k; changed = true; }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Path.GetFileNameWithoutExtension(k));
        }
        if (keys.Count > 0) ImGui.SameLine(0, 4);
        string seed = keys.FirstOrDefault() ?? folder + "/_";
        string current = key;
        if (ImGui.Button($"...##lib{folder}", new Num.Vector2(30, h + 6))) Art.ArtBrowser.OpenPick(string.IsNullOrEmpty(current) ? seed : current, k => _gridPicked = (id, k));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Art library");
        ImGui.SameLine(0, 4);
        if (ImGui.Button($"+##add{folder}", new Num.Vector2(30, h + 6))) Art.ArtBrowser.OpenPick(seed, k => _gridPicked = (id, k), addNew: true);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Add a mask from your computer (into {folder})");
        if (keys.Count == 0) Ui.Hint($"No masks in {folder} yet: '+' adds one.");
        return changed;
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

    // ------------------------------------------------------------------ wall builder (step by step)

    private enum WallStep { Picture, LeftFace, RightFace, OuterCorners, InnerCorners, EndCaps, Steps, Rim, DepthFade, Review }

    private static readonly (WallStep step, string title, string help)[] WallSteps =
    {
        (WallStep.Picture, "Picture", "The look every part shares: picture, zoom, tint, lip line, perspective."),
        (WallStep.LeftFace, "Left faces", "Walls facing down-left: brightness and slant."),
        (WallStep.RightFace, "Right faces", "Walls facing down-right: usually the left face mirrored and darker."),
        (WallStep.OuterCorners, "Outer corners", "Where a wall turns outward to a point. Bevel or seam, lean, highlight. Add variants to swap on tiles."),
        (WallStep.InnerCorners, "Inner corners", "Notches where a wall turns inward: seam lean and shadow. Variants allowed."),
        (WallStep.EndCaps, "Wall ends", "Where a wall stops next to a 'no wall' cell. End 1 is a plain straight edge; add variations to shape ends cell by cell."),
        (WallStep.Steps, "Steps", "Staircase edges: check the faces carry on cleanly from step to step."),
        (WallStep.Rim, "Rim", "Optional strip of the picture's top laid over the walkway edge."),
        (WallStep.DepthFade, "Depth & fade", "How deep faces reach and how long a piece is; the fade comes from each floor."),
        (WallStep.Review, "Review & save", "Everything together. Save to use it on floors."),
    };

    private WallDef? _wall;
    private bool _wallIsNew;
    private WallStep _wallStep;
    private TileDef? _previewTile;
    private FloorDef? _previewFloor;
    /// <summary>Grid and captions in the wall / prefab previews. Off = as in game.</summary>
    private bool _previewHelpers;
    /// <summary>Builder preview: use the current floor's fade and bottom edges for this wall (they belong to the floor, not the wall).</summary>
    private bool _showFloorFade = true;
    private int _variantTab;
    /// <summary>Builder: each corner / end in the preview shows a different variant, side by side.</summary>
    private bool _compareVariants;
    private (float taper, float squash) _wallTaper;
    private List<WallJunction> _previewJunctions = new();

    private void StartNewWall(MaterialKind kind)
    {
        var w = new WallDef { Kind = kind, LipLine = kind == MaterialKind.WallUp ? 1f : 0f, Category = kind == MaterialKind.WallUp ? "Walls" : "Cliffs" };
        OpenWallEditor(w, isNew: true);
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
        _variantTab = 0;
        ClearMapModes();
        _wallBrush = null;
        _panel = Panel.WallEdit;
        // Carry on from the first step not yet approved.
        _wallStep = WallSteps.Select(x => x.step).FirstOrDefault(st => !w.Approved.Contains(st.ToString()), WallStep.Review);
        _showFloorFade = _wallStep != WallStep.EndCaps;
        BuildPreview();
    }

    private bool WallDirty => _wall is not null && (_wallIsNew
        || !Services.Defs.TryGet<WallDef>(_wall.Id, out var saved) || ChasmJson.Serialize(saved) != ChasmJson.Serialize(_wall));

    /// <summary>
    /// The builder's preset: one walkway shape with every situation in it - long left and right
    /// faces, outer corners, a notch (inner corner), a step, and a gap painted 'no wall' (two ends).
    /// </summary>
    private void BuildPreview()
    {
        if (_capDraft is not null) { BuildCapDraftPreview(); return; }
        var w = _wall!;
        bool up = w.Kind == MaterialKind.WallUp;
        int o = up ? 3 : 0; // walls up rise above the walkway, so sit it lower on screen
        var t = new TileDef { Id = "__wall_builder", Name = "Wall builder" };
        foreach (var c in IsoGrid.AllCells) t.SetWalkable(c, false);
        var walk = new List<CellCoord>();
        for (int q = 9; q <= 18; q++) for (int r = -2; r <= 2; r++) walk.Add(new(q + o, r + o));   // the block
        walk.Remove(new(13 + o, 2 + o));                                                       // a notch
        walk.Add(new(16 + o, 3 + o)); walk.Add(new(17 + o, 3 + o));                            // a step
        if (up) { walk.Remove(new(13 + o, -2 + o)); walk.Add(new(15 + o, -3 + o)); walk.Add(new(16 + o, -3 + o)); }
        foreach (var c in walk.Where(IsoGrid.InTile)) t.SetWalkable(c, true);
        // A gap with no wall: the faces either side of it end there.
        t.WallPaint.Add(new TileWallPaint { Side = up ? "up" : "down", Wall = TileWallPaint.NoWall,
            Cells = { up ? new CellCoord(10 + o, -3 + o) : new CellCoord(10 + o, 3 + o) } });
        FinishPreview(t, walk.Where(IsoGrid.InTile).ToList());
    }

    /// <summary>The preview tile's ground, and a floor with just this wall on it (with the floor's fade and bottom edges for it).</summary>
    private void FinishPreview(TileDef t, List<CellCoord> walk)
    {
        var w = _wall!;
        bool up = w.Kind == MaterialKind.WallUp;
        t.LocalMaterials.Add(new MaterialDef { Id = "ground", Name = "Preview ground", Texture = "placeholder/textures/grass.png" });
        t.PaintRegions.Add(new PaintRegion { Id = "ground", Material = "local:ground", Cells = walk });
        t.InvalidateCaches();
        var f = _floor is null ? new FloorDef { Id = "__builder_floor" } : ChasmJson.Clone(_floor);
        f.Id = "__builder_floor";
        f.WallsDown = new(); f.WallsUp = new();
        var builderEntry = new FloorWall { Wall = "__builder_wall" };
        // Optionally with this floor's fade and bottom edges for the wall (else the full depth, no fade).
        if (!_showFloorFade) builderEntry.FadeMode = WallFadeMode.None;
        else if (_floor is not null && (up ? _floor.WallsUp : _floor.WallsDown).FirstOrDefault(x => x.Wall == w.Id) is { } real)
        {
            builderEntry.FadeMode = real.FadeMode;
            builderEntry.Fade = ChasmJson.Clone(real.Fade);
            builderEntry.BottomEdges = real.BottomEdges.Select(ChasmJson.Clone).ToList();
        }
        (up ? f.WallsUp : f.WallsDown).Add(builderEntry);
        _previewTile = t;
        _previewFloor = f;
        PreviewChanged();
    }

    private void PreviewChanged()
    {
        if (_previewTile is null || _wall is null) return;
        var w = _wall;
        Services.TileVisuals.WallOverride = x => x == "__builder_wall" ? w : null;
        // Show the variant being edited everywhere it applies.
        _previewTile.WallVariants.Clear();
        var part = _wallStep switch
        {
            WallStep.OuterCorners => (WallPart?)WallPart.OuterCorner,
            WallStep.InnerCorners => WallPart.InnerCorner,
            WallStep.EndCaps => WallPart.EndCap,
            _ => null,
        };
        _previewJunctions.Clear();
        Services.TileVisuals.WallsFor(_previewTile, _previewFloor, _previewJunctions);
        if (part is { } pt)
        {
            // Left to right across the preview: all the variant being edited, or 1, 2, 3... to compare.
            int k = 0;
            foreach (var j in _previewJunctions.Where(j => j.Part == pt).OrderBy(j => j.Point.X))
            {
                int index = _compareVariants && j.Count > 0 ? k++ % j.Count : _variantTab;
                _previewTile.WallVariants.Add(new TileWallVariant { Side = j.Up ? "up" : "down", Part = pt, Cell = j.Cell, Index = index });
            }
        }
        Services.TileVisuals.Invalidate(_previewTile);
    }

    private void WallEditPanel()
    {
        if (_wall is not { } w) { _panel = Panel.Walls; return; }
        int idx = Array.FindIndex(WallSteps, x => x.step == _wallStep);
        var (step, title, help) = WallSteps[idx];
        Ui.Colored(_wallIsNew ? Ui.Warn : Ui.Muted, _wallIsNew ? "New wall (not saved yet)" : $"content/defs/walls/{w.Id}.json");

        // Progress: every step, approved ones ticked; click to jump.
        int done = WallSteps.Count(x => w.Approved.Contains(x.step.ToString()));
        ImGui.ProgressBar(done / (float)WallSteps.Length, new Num.Vector2(-1, 6), "");
        ImGui.BeginChild("steps", new Num.Vector2(0, 5 * ImGui.GetTextLineHeightWithSpacing() + 18), ImGuiChildFlags.Borders);
        for (int i = 0; i < WallSteps.Length; i++)
        {
            var s = WallSteps[i];
            bool ok = w.Approved.Contains(s.step.ToString());
            if (i % 2 == 1) ImGui.SameLine(ImGui.GetContentRegionAvail().X / 2 + 8);
            ImGui.PushStyleColor(ImGuiCol.Text, ok ? Ui.Good : s.step == step ? Ui.Accent : Ui.Muted);
            if (ImGui.Selectable($"{(ok ? "v" : " ")} {i + 1}. {s.title}##st{i}", s.step == step, ImGuiSelectableFlags.None, new Num.Vector2(ImGui.GetContentRegionAvail().X / (i % 2 == 0 ? 2 : 1) - 8, 0)))
                GoToStep(s.step);
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();

        Ui.Title($"{idx + 1}. {title}");
        Ui.Hint(help);
        if (Ui.ToggleButton("Grid & captions", _previewHelpers, new Num.Vector2(0, 0), "Show the grid and labels on the preview (H).")) _previewHelpers = !_previewHelpers;
        ImGui.SameLine(0, 12);
        string floorName = _floor?.DisplayName ?? "the floor";
        if (ImGui.Checkbox($"Show {floorName}'s fade & bottom edge", ref _showFloorFade)) BuildPreview();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The fade into the clouds and the bottom edge belong to each floor, not the wall:\nset them on the Walls page (this wall's entry on the floor).\nOff: the wall at its full depth, no fade.");

        ImGui.BeginChild("stepbody", new Num.Vector2(0, -78));
        bool changed = step switch
        {
            WallStep.Picture => PictureStep(w),
            WallStep.LeftFace => LeftFaceStep(w),
            WallStep.RightFace => RightFaceStep(w),
            WallStep.OuterCorners => VariantStep(w.OuterCorners, () => new OuterCornerVariant { Name = $"Corner {w.OuterCorners.Count + 1}" }, OuterCornerFields),
            WallStep.InnerCorners => VariantStep(w.InnerCorners, () => new InnerCornerVariant { Name = $"Notch {w.InnerCorners.Count + 1}" }, InnerCornerFields),
            WallStep.EndCaps => _capDraft is not null ? CapDraftPanel(w) : EndCapStep(w),
            WallStep.Steps => StepsStep(),
            WallStep.Rim => RimStep(w),
            WallStep.DepthFade => DepthStep(w),
            _ => ReviewStep(w),
        };
        ImGui.EndChild();
        if (changed) PreviewChanged();
        if (_capDraft is not null) { CapDraftFooter(w); return; }

        // Footer: back / approve / save / close.
        bool approved = w.Approved.Contains(step.ToString());
        ImGui.BeginDisabled(idx == 0);
        if (ImGui.Button("< Back", new Num.Vector2(80, 0))) GoToStep(WallSteps[idx - 1].step);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (step != WallStep.Review)
        {
            if (Ui.AccentButton(approved ? "Approved - next >" : "Approve & next >", new Num.Vector2(170, 0)))
            {
                if (!approved) w.Approved.Add(step.ToString());
                GoToStep(WallSteps[idx + 1].step);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Happy with this part? Tick it off and go to the next.");
        }
        ImGui.Spacing();
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(w.Name) || string.IsNullOrEmpty(w.Texture));
        if (Ui.AccentButton(WallDirty ? "Save wall *" : "Save wall", new Num.Vector2(120, 0))) SaveWall();
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
    }

    private void GoToStep(WallStep step)
    {
        if (_capDraft is not null) EndCapDraft(keep: false);
        // Wall ends are designed at the wall's full depth: the floor's fade would hide the lower cells.
        bool showFade = step != WallStep.EndCaps;
        bool rebuild = showFade != _showFloorFade;
        _showFloorFade = showFade;
        _wallStep = step;
        if (rebuild) BuildPreview();
        _variantTab = 0;
        _rowTab = 0;
        PreviewChanged();
    }

    // ---- steps

    private bool PictureStep(WallDef w)
    {
        const float label = 120;
        bool changed = false;
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
        ImGui.TextUnformatted("Texture"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string tex = w.Texture;
        ImGui.PushID("wtex");
        if (ToolContext.Wizards.Inspector.TexturePicker(ref tex)) { w.Texture = tex; changed = true; }
        ImGui.PopID();
        Ui.Hint("One left-facing strip of wall. The right side, corners and ends are made from it.");
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
        return changed;
    }

    private bool LeftFaceStep(WallDef w)
    {
        bool changed = SliderRow("Brightness", 120, () => w.LeftShade, v => w.LeftShade = v, 0.2f, 1.5f, "%.2f");
        bool skew = w.SkewFaces;
        if (ImGui.Checkbox("Slant along the cell edges", ref skew)) { w.SkewFaces = skew; changed = true; }
        Ui.Hint("Slant keeps vertical lines vertical while the face follows the diagonal edge (the isometric look). Turn it off only if your picture is already drawn on the diagonal.");
        return changed;
    }

    private bool RightFaceStep(WallDef w)
    {
        bool changed = SliderRow("Brightness", 120, () => w.RightShade, v => w.RightShade = v, 0.2f, 1.5f, "%.2f");
        Ui.Hint("A little darker than the left face reads as light from one side - this is most of the 3D feel.");
        ImGui.TextUnformatted("Own art"); ImGui.SameLine(120); ImGui.SetNextItemWidth(-1);
        string rtex = w.RightTexture;
        ImGui.PushID("wrtex");
        if (ToolContext.Wizards.Inspector.TexturePicker(ref rtex)) { w.RightTexture = rtex; changed = true; }
        ImGui.PopID();
        Ui.Hint(string.IsNullOrEmpty(w.RightTexture) ? "Empty = the left face, mirrored (usually all you need)." : "Using your own right-face picture (same zoom and lip line).");
        return changed;
    }

    /// <summary>Tabs for a part's variants (1 = the default everywhere), with add / delete, then the selected one's fields.</summary>
    private bool VariantStep<T>(List<T> list, Func<T> make, Func<T, bool> fields)
    {
        bool changed = false;
        if (list.Count == 0) { list.Add(make()); changed = true; }
        _variantTab = Math.Clamp(_variantTab, 0, list.Count - 1);
        Ui.Hint("Variant 1 is used everywhere by default. Extra variants can be swapped in on specific spots of a tile (Walls page > Swap corners & ends).");
        for (int i = 0; i < list.Count; i++)
        {
            string name = list[i] switch { OuterCornerVariant o => o.Name, InnerCornerVariant n => n.Name, EndCapVariant e => e.Name, _ => "" };
            if (i > 0) ImGui.SameLine(0, 4);
            if (Ui.ToggleButton($"{i + 1}. {name}##vt{i}", _variantTab == i, new Num.Vector2(0, 0), i == 0 ? "The default" : "Swap in on tiles"))
            { _variantTab = i; changed = true; }
            if (ImGui.GetContentRegionAvail().X < 120) ImGui.NewLine();
        }
        ImGui.SameLine(0, 8);
        if (ImGui.Button("+ Add variant")) { list.Add(make()); _variantTab = list.Count - 1; changed = true; }
        if (list.Count > 1)
        {
            if (ImGui.Checkbox("Compare variants side by side", ref _compareVariants)) changed = true;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("On: the spots in the preview show variant 1, 2, 3... left to right.\nOff: every spot shows the variant you're editing.");
        }
        ImGui.Separator();
        var v = list[_variantTab];
        changed |= fields(v);
        if (list.Count > 1)
        {
            ImGui.Spacing();
            if (_variantTab > 0 && ImGui.Button("Make this the default (move to 1)")) { list.RemoveAt(_variantTab); list.Insert(0, v); _variantTab = 0; changed = true; }
            if (_variantTab > 0) ImGui.SameLine();
            if (ImGui.Button("Delete this variant")) { list.RemoveAt(_variantTab); _variantTab = Math.Max(0, _variantTab - 1); changed = true; }
            Ui.Hint("Tiles that swapped to a deleted variant fall back to the nearest one.");
        }
        return changed;
    }

    private bool OuterCornerFields(OuterCornerVariant v)
    {
        const float label = 120;
        bool changed = NameRow(v.Name, n => v.Name = n);
        ImGui.TextUnformatted("Style"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] styles = { "Seam (the faces meet on a line)", "Bevel (narrow cut face)" };
        int st = (int)v.Style;
        if (ImGui.Combo("##ocstyle", ref st, styles, styles.Length)) { v.Style = (CornerStyle)st; changed = true; }
        changed |= SliderRow("Lean", label, () => v.Lean, x => v.Lean = x, -60, 60, "%.0f deg");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Tilts the corner line: one face wraps a little further round as it goes down.");
        changed |= SliderRow("Softness", label, () => v.Blend, x => v.Blend = x, 0, 80, "%.0f px");
        if (v.Style == CornerStyle.Bevel)
        {
            changed |= SliderRow("Bevel width", label, () => v.BevelWidth, x => v.BevelWidth = x, 2, 60, "%.0f px");
            bool auto = v.BevelShade is null;
            if (ImGui.Checkbox("Bevel brightness: halfway between the faces", ref auto)) { v.BevelShade = auto ? null : 0.86f; changed = true; }
            if (!auto) changed |= SliderRow("Bevel brightness", label, () => v.BevelShade ?? 0.86f, x => v.BevelShade = x, 0.2f, 1.5f, "%.2f");
        }
        changed |= SliderRow("Highlight", label, () => v.Highlight, x => v.Highlight = x, 0, 1, "%.2f");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A thin light line down the corner edge.");
        if (v.Highlight > 0) changed |= SliderRow("Highlight width", label, () => v.HighlightWidth, x => v.HighlightWidth = x, 1, 20, "%.0f px");
        return changed;
    }

    private bool InnerCornerFields(InnerCornerVariant v)
    {
        const float label = 120;
        bool changed = NameRow(v.Name, n => v.Name = n);
        changed |= SliderRow("Lean", label, () => v.Lean, x => v.Lean = x, -60, 60, "%.0f deg");
        changed |= SliderRow("Softness", label, () => v.Blend, x => v.Blend = x, 0, 80, "%.0f px");
        changed |= SliderRow("Shadow", label, () => v.Shadow, x => v.Shadow = x, 0, 1, "%.2f");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Darkening in the notch: makes it read as going in.");
        if (v.Shadow > 0) changed |= SliderRow("Shadow width", label, () => v.ShadowWidth, x => v.ShadowWidth = x, 2, 80, "%.0f px");
        return changed;
    }

    private int _rowTab;
    private string _newComboName = "";
    private bool _openSaveCombo;
    private EndCapVariant? _comboTarget;

    private bool EndCapFields(EndCapVariant v)
    {
        const float label = 120;
        bool changed = NameRow(v.Name, n => v.Name = n);
        ImGui.TextUnformatted("Style"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] caps = { "Straight (hard cut)", "Soft (fades out)", "Masks, cell by cell" };
        int cap = (int)v.Style;
        if (ImGui.Combo("##ecstyle", ref cap, caps, caps.Length)) { v.Style = (EndCapStyle)cap; changed = true; }
        if (v.Style == EndCapStyle.Soft) changed |= SliderRow("Softness", label, () => v.Softness, x => v.Softness = x, 4, 128, "%.0f px");
        if (v.Style != EndCapStyle.Stencil) return changed;

        // Combo: a saved, reusable column of rows (linked) - or this variant's own rows.
        var combos = Services.Defs.All<EndCapComboDef>().OrderBy(c => c.DisplayName).ToList();
        var linked = string.IsNullOrEmpty(v.Combo) ? null : combos.FirstOrDefault(c => c.Id == v.Combo);
        ImGui.TextUnformatted("Combo"); ImGui.SameLine(label); ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 120);
        if (ImGui.BeginCombo("##combo", linked?.DisplayName ?? "(this wall's own rows)"))
        {
            if (ImGui.Selectable("(this wall's own rows)", linked is null)) { v.Combo = ""; changed = true; }
            foreach (var c in combos)
                if (ImGui.Selectable($"{c.DisplayName}  ({c.Rows.Count} rows)##{c.Id}", c == linked)) { v.Combo = c.Id; changed = true; }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Use a saved combo (stays linked: improving it improves every wall using it).");
        ImGui.SameLine(0, 4);
        if (linked is null)
        {
            if (ImGui.Button("Save as combo", new Num.Vector2(116, 0))) { _comboTarget = v; _newComboName = v.Name; _openSaveCombo = true; }
        }
        else if (ImGui.Button("Unlink", new Num.Vector2(116, 0)))
        {
            v.Rows = linked.Rows.Select(ChasmJson.Clone).ToList();
            v.Combo = "";
            changed = true;
        }
        if (linked is not null) Ui.ColoredWrapped(Ui.Blue, $"Linked to '{linked.DisplayName}': edits below change the combo for every wall using it (saved with Save wall).");
        SaveComboPopup();

        bool rc = EndCapRows(linked?.Rows ?? v.Rows);
        if (rc && linked is not null) _dirtyCombos.Add(linked.Id);
        return changed | rc;
    }

    /// <summary>The cell-by-cell editor: one tab per cell down the end's column (keep / mask / hide).</summary>
    private bool EndCapRows(List<EndCapRow> rows)
    {
        const float label = 120;
        int depth = Math.Max(1, _wall?.Depth ?? 4);
        while (rows.Count < depth) rows.Add(new EndCapRow());
        _rowTab = Math.Clamp(_rowTab, 0, rows.Count - 1);
        Ui.Hint("Cell 1 is the top of the end (under the walkway). Masks are drawn for a wall ending on the right; left ends mirror them.");
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) ImGui.SameLine(0, 4);
            string tag = rows[i].Mode switch { EndCapRowMode.Stencil => "mask", EndCapRowMode.Hide => "hide", _ => "keep" };
            if (Ui.ToggleButton($"Cell {i + 1}: {tag}##row{i}", _rowTab == i, new Num.Vector2(0, 0), null)) _rowTab = i;
            if (ImGui.GetContentRegionAvail().X < 110) ImGui.NewLine();
        }
        var row = rows[_rowTab];
        bool rc = false;
        int mode = (int)row.Mode;
        if (ImGui.RadioButton("Keep", mode == 0)) { row.Mode = EndCapRowMode.Keep; rc = true; }
        ImGui.SameLine();
        if (ImGui.RadioButton("Mask", mode == 1)) { row.Mode = EndCapRowMode.Stencil; if (string.IsNullOrEmpty(row.Stencil)) row.Stencil = "stencils/end caps/taper_1.png"; rc = true; }
        ImGui.SameLine();
        if (ImGui.RadioButton("Hide (draw nothing)", mode == 2)) { row.Mode = EndCapRowMode.Hide; rc = true; }
        if (row.Mode == EndCapRowMode.Stencil)
        {
            string st = row.Stencil;
            if (StencilGrid("stencils/end caps", ref st, 64, 32)) { row.Stencil = st; rc = true; }
            rc |= SliderRow("Turn", label, () => row.Rotation, x => row.Rotation = x, -180, 180, "%.0f deg");
            rc |= SliderRow("Move X", label, () => row.OffsetX, x => row.OffsetX = x, -64, 64, "%.0f px");
            rc |= SliderRow("Move Y", label, () => row.OffsetY, x => row.OffsetY = x, -32, 32, "%.0f px");
            rc |= SliderRow("Size", label, () => row.Scale, x => row.Scale = x, 0.25f, 3f, "%.2fx");
            bool fy = row.FlipY;
            if (ImGui.Checkbox("Flip Y##rfy", ref fy)) { row.FlipY = fy; rc = true; }
            ImGui.SameLine();
            if (ImGui.Button("Reset placement")) { row.Rotation = 0; row.OffsetX = 0; row.OffsetY = 0; row.Scale = 1; row.FlipY = false; rc = true; }
        }
        return rc;
    }

    private readonly HashSet<string> _dirtyCombos = new();

    private void SaveComboPopup()
    {
        if (_openSaveCombo) { ImGui.OpenPopup("Save end-cap combo"); _openSaveCombo = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Save end-cap combo", ImGuiWindowFlags.AlwaysAutoResize)) return;
        Ui.Hint("Saves these rows as a combo any wall can use (and stay linked to).");
        ImGui.SetNextItemWidth(300);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        bool enter = ImGui.InputTextWithHint("##cname", "e.g. Taper, 4 cells", ref _newComboName, 60, ImGuiInputTextFlags.EnterReturnsTrue);
        if ((Ui.AccentButton("Save", new Num.Vector2(120, 0)) || enter) && _newComboName.Trim().Length > 0 && _comboTarget is { } v)
        {
            var c = new EndCapComboDef { Name = _newComboName.Trim(), Rows = v.Rows.Select(ChasmJson.Clone).ToList() };
            c.Id = Services.Defs.SuggestId(typeof(EndCapComboDef), c.Name);
            Services.Defs.Save(c);
            v.Combo = c.Id;
            Status($"Saved combo '{c.Name}'. This end now uses it (linked).");
            PreviewChanged();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(100, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private bool StepsStep()
    {
        Ui.Hint("Nothing to set here: check that the faces carry on cleanly where the walkway steps. If not, go back and adjust zoom, lip line or slant.");
        return false;
    }

    private bool RimStep(WallDef w)
    {
        bool changed = false;
        bool rim = w.Rim;
        if (ImGui.Checkbox("Rim over the walkway edge", ref rim)) { w.Rim = rim; changed = true; }
        Ui.Hint("A thin strip of the picture's top laid over the walkway edge, hiding the seam between ground and wall.");
        if (w.Rim) changed |= SliderRow("Rim height", 120, () => w.RimHeight, v => w.RimHeight = v, 2, 64, "%.0f px");
        return changed;
    }

    private bool DepthStep(WallDef w)
    {
        bool changed = false;
        int depth = w.Depth, run = w.MaxRun;
        ImGui.TextUnformatted("Depth"); ImGui.SameLine(120); ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderInt("##wdepth", ref depth, 1, 10, "%d cells")) { w.Depth = depth; changed = true; }
        ImGui.TextUnformatted("Piece length"); ImGui.SameLine(120); ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderInt("##wrun", ref run, 1, 24, "up to %d cells")) { w.MaxRun = run; changed = true; }
        int fits = Math.Max(1, (int)(Services.Textures.Get(w.Texture).Width * w.Scale / 64) - 1);
        Ui.Colored(run > fits ? Ui.Warn : Ui.Muted, $"Your picture covers about {fits} cells at this zoom{(run > fits ? " - longer pieces will run out of picture" : "")}.");
        var fade = _floor is null ? null : (w.Kind == MaterialKind.WallUp ? _floor.WallUpFade : _floor.WallDownFade);
        Ui.Hint(fade is null ? "The fade into the clouds is set per floor (Walls page)."
            : $"Fade (from this floor's default): starts {fade.Start:0.##} cells, gone after {fade.Length:0.##} more. Set per floor on the Walls page; each wall on a floor can have its own.");
        return changed;
    }

    private bool ReviewStep(WallDef w)
    {
        foreach (var (step, title, _) in WallSteps.Where(x => x.step != WallStep.Review))
        {
            bool ok = w.Approved.Contains(step.ToString());
            Ui.Colored(ok ? Ui.Good : Ui.Warn, $"{(ok ? "Approved" : "Not approved yet")}:  {title}");
        }
        ImGui.Spacing();
        Ui.Hint($"Variants: {w.OuterCorners.Count} outer corner(s), {w.InnerCorners.Count} inner corner(s), {w.EndCapVariants.Count} end(s). Save, then it's ready to add to floors.");
        return false;
    }

    private bool NameRow(string value, Action<string> set)
    {
        ImGui.TextUnformatted("Name"); ImGui.SameLine(120); ImGui.SetNextItemWidth(-1);
        string n = value;
        if (ImGui.InputText("##vname", ref n, 60)) { set(n); return true; }
        return false;
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
        foreach (var id in _dirtyCombos)
            if (Services.Defs.TryGet<EndCapComboDef>(id, out var combo)) Services.Defs.Save(combo);
        _dirtyCombos.Clear();
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

    /// <summary>Wall builder: the map only previews (H toggles grid & captions).</summary>
    private bool UpdateWallPreviewInput(InputState input)
    {
        if (_panel != Panel.WallEdit || _previewTile is null) return false;
        if (input.KeyPressed(Microsoft.Xna.Framework.Input.Keys.H) && !input.KeyboardCapturedByUi) _previewHelpers = !_previewHelpers;
        return true;
    }

    /// <summary>The wall builder's preview, drawn instead of the tile.</summary>
    private void DrawWallPreview(SpriteBatch batch)
    {
        var t = _previewTile!;
        var s = Services;
        TileRenderer.QueueLayers(_queue, s, t, _previewFloor, includeProps: false);
        _queue.Flush(batch);
        var d = s.Draw;
        if (!_previewHelpers) return;
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        if (_previewHelpers)
        {
            foreach (var c in IsoGrid.AllCells) d.DiamondOutline(batch, IsoGrid.CellCenter(c), IsoGrid.CellWidth, IsoGrid.CellHeight, EditorPrefs.ToColor(_prefs.GridColor));
            d.TextShadowed(batch, $"Wall builder: {_wall!.DisplayName}  -  {WallSteps.First(x => x.step == _wallStep).title}", new Vector2(24, 24), Color.White, 26 / CanvasScale(), bold: true);
        }
        batch.End();
    }
}
