using System.Numerics;
using System.Text.Json.Serialization;
using Chasm.Core.Content;
using Chasm.Core.Grid;

namespace Chasm.Core.World;

public enum EndCapStyle
{
    [Label("None (hard cut)")] None,
    [Label("Soft (fades out)")] Soft,
    [Label("Stencil (carved shape)")] Stencil,
}

public enum CornerStyle
{
    [Label("Seam (the faces meet on a line)")] Seam,
    [Label("Bevel (a narrow cut face across the corner)")] Bevel,
}

/// <summary>One look for the outer corners (where a wall sticks out to a point).</summary>
public sealed class OuterCornerVariant
{
    public string Name { get; set; } = "Corner";
    public CornerStyle Style { get; set; } = CornerStyle.Bevel;
    [Range(-60, 60), Tooltip("Tilts the corner line (degrees). 0 = straight down.")] public float Lean { get; set; }
    [Range(0, 80), Tooltip("Softness where the pieces meet (pixels).")] public float Blend { get; set; } = 6f;
    [Range(2, 60), Tooltip("Bevel: width of the cut face (pixels).")] public float BevelWidth { get; set; } = 14f;
    [Range(0.2f, 1.5f), Tooltip("Bevel: brightness of the cut face. Empty = halfway between the left and right shades.")]
    public float? BevelShade { get; set; }
    [Range(0, 1), Tooltip("A thin light line down the corner edge.")] public float Highlight { get; set; } = 0.25f;
    [Range(1, 20)] public float HighlightWidth { get; set; } = 4f;
}

/// <summary>One look for the inner corners (a notch where two walls meet going in).</summary>
public sealed class InnerCornerVariant
{
    public string Name { get; set; } = "Notch";
    [Range(-60, 60)] public float Lean { get; set; }
    [Range(0, 80)] public float Blend { get; set; } = 8f;
    [Range(0, 1), Tooltip("Darkening in the notch, which reads as depth.")] public float Shadow { get; set; } = 0.3f;
    [Range(2, 80)] public float ShadowWidth { get; set; } = 26f;
}

/// <summary>One look for where a wall ends (next to cells painted 'no wall').</summary>
public sealed class EndCapVariant
{
    public string Name { get; set; } = "End";
    public EndCapStyle Style { get; set; } = EndCapStyle.Soft;
    [Range(4, 128)] public float Softness { get; set; } = 48f;
    /// <summary>Stencil style: what happens in each cell of the end's column, top first.</summary>
    public List<EndCapRow> Rows { get; set; } = new();
    /// <summary>Stencil style: use a saved combo's rows instead (stays linked to it).</summary>
    [Ref(typeof(EndCapComboDef))] public string Combo { get; set; } = "";

    // Older files had one stencil: it becomes the top row.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stencil { get => null; set { if (!string.IsNullOrEmpty(value) && Rows.Count == 0) Rows.Add(new EndCapRow { Mode = EndCapRowMode.Stencil, Stencil = value }); } }

    /// <summary>The rows in use: the linked combo's, else its own.</summary>
    public List<EndCapRow> RowsFrom(Func<string, EndCapComboDef?>? combos) =>
        !string.IsNullOrEmpty(Combo) && combos?.Invoke(Combo) is { } c ? c.Rows : Rows;
}

public enum EndCapRowMode
{
    [Label("Keep (draw as normal)")] Keep,
    [Label("Stencil (shape it)")] Stencil,
    [Label("Hide (draw nothing)")] Hide,
}

/// <summary>One cell of a wall end's column (row 1 = the top cell, under the walkway).</summary>
public sealed class EndCapRow
{
    public EndCapRowMode Mode { get; set; } = EndCapRowMode.Keep;
    [Texture] public string Stencil { get; set; } = "";
    [Range(-180, 180)] public float Rotation { get; set; }
    [Range(-64, 64)] public float OffsetX { get; set; }
    [Range(-32, 32)] public float OffsetY { get; set; }
    [Range(0.25f, 3)] public float Scale { get; set; } = 1f;
    public bool FlipY { get; set; }
}

/// <summary>
/// A saved set of end-cap rows (e.g. "Taper, 4 cells"), reusable by any wall. Walls that use it
/// stay linked, so improving the combo improves every wall using it.
/// </summary>
[DefFolder("endcaps", "End cap combos")]
[Tooltip("A reusable column of end-cap masks.")]
public sealed class EndCapComboDef : Def
{
    public List<EndCapRow> Rows { get; set; } = new();
}

/// <summary>
/// One look for a wall's bottom edge on a floor: a strip mask laid along the wall where it fades
/// into the clouds, giving the bottom a shape (rock tips, crumbling...). It's anchored to the fade,
/// so it moves with the cloud level.
/// </summary>
public sealed class BottomEdgeVariant
{
    public string Name { get; set; } = "Bottom";
    [Texture, Tooltip("A strip: opaque = keep, transparent = cut. Repeats along the wall.")]
    public string Stencil { get; set; } = "";
    [Range(-4, 4), Tooltip("Moves the strip up (-) or down (+) from the middle of the fade, in cells.")]
    public float Offset { get; set; }
    [Range(8, 256), Tooltip("How tall the strip is (pixels).")] public float Height { get; set; } = 64f;
    [Range(32, 1024), Tooltip("How wide one repeat of the strip is (pixels).")] public float Repeat { get; set; } = 256f;
    public bool FlipX { get; set; }
}

/// <summary>A stretch of a wall piece (screen x range) that uses one bottom edge.</summary>
public sealed record BottomSegment(float MinX, float MaxX, string Stencil, float Offset, float Height, float Repeat, bool FlipX)
{
    public string Key => $"{MinX:0},{MaxX:0}:{Stencil}/{Offset:0.##}/{Height:0}/{Repeat:0}{(FlipX ? "x" : "")}";
}

/// <summary>
/// One wall / cliff look ("Grey wall"), made from a single left-facing picture. Every variation is
/// worked out when a tile is drawn: right face (mirrored, or your own art), outer and inner corners,
/// end caps and an optional rim. Corners and caps can have several variants: tiles use the first
/// unless a specific spot is swapped to another.
/// </summary>
[DefFolder("walls", "Walls")]
[Tooltip("A wall / cliff look made from one left-facing picture.")]
public sealed class WallDef : Def
{
    [Tooltip("Grouping in lists.")] public string Category { get; set; } = "Cliffs";
    [Tooltip("Wall down = cliffs under walkway edges. Wall up = walls rising above them.")]
    public MaterialKind Kind { get; set; } = MaterialKind.WallDown;

    // ---- the picture (global: every part uses these)
    [Texture, Required, Tooltip("The left face: a long strip of wall, facing down-left. Seamless left-right is best.")]
    public string Texture { get; set; } = "";
    [Range(0.1f, 4)] public float Scale { get; set; } = 1f;
    [Range(0, 1), Tooltip("Where the top edge (wall down) or foot (wall up) is in the picture: 0 = top, 1 = bottom.")]
    public float LipLine { get; set; }
    public string Tint { get; set; } = "#ffffff";
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    public MaterialWarp? Warp { get; set; }
    [Range(1, 10)] public int Depth { get; set; } = 4;
    [Range(1, 24)] public int MaxRun { get; set; } = 12;

    // ---- faces
    [Range(0.2f, 1.5f)] public float LeftShade { get; set; } = 1f;
    [Range(0.2f, 1.5f)] public float RightShade { get; set; } = 0.72f;
    [Texture] public string RightTexture { get; set; } = "";
    public bool SkewFaces { get; set; } = true;

    // ---- parts with variants (the first is the default everywhere)
    public List<OuterCornerVariant> OuterCorners { get; set; } = new() { new() };
    public List<InnerCornerVariant> InnerCorners { get; set; } = new() { new() };
    public List<EndCapVariant> EndCapVariants { get; set; } = new() { new() };

    // ---- rim
    public bool Rim { get; set; }
    [Range(2, 64)] public float RimHeight { get; set; } = 14f;

    /// <summary>Steps of the wall builder that have been approved.</summary>
    [Hidden] public List<string> Approved { get; set; } = new();

    // ---- older files (one corner / cap setting): folded into the first variant when loaded
    [Hidden, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? CornerBlend { get => null; set { if (value is { } v) { OuterCorners[0].Blend = v; InnerCorners[0].Blend = v; } } }
    [Hidden, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EndCapStyle? EndCaps { get => null; set { if (value is { } v) EndCapVariants[0].Style = v; } }
    [Hidden, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? EndCapSoftness { get => null; set { if (value is { } v) EndCapVariants[0].Softness = v; } }
    [Hidden, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EndCapStencil { get => null; set { if (value is { } v) EndCapVariants[0].Stencil = v; } }

    public OuterCornerVariant Outer(int i) => OuterCorners.Count == 0 ? new() : OuterCorners[Math.Clamp(i, 0, OuterCorners.Count - 1)];
    public InnerCornerVariant Inner(int i) => InnerCorners.Count == 0 ? new() : InnerCorners[Math.Clamp(i, 0, InnerCorners.Count - 1)];
    public EndCapVariant Cap(int i) => EndCapVariants.Count == 0 ? new() : EndCapVariants[Math.Clamp(i, 0, EndCapVariants.Count - 1)];
}

/// <summary>How a wall on a floor fades into the clouds.</summary>
public enum WallFadeMode
{
    [Label("Floor default")] Default,
    [Label("Own numbers")] Own,
    [Label("No fade")] None,
}

/// <summary>A wall in a floor's Walls down / Walls up list, with its optional own fade.</summary>
public sealed class FloorWall
{
    [Ref(typeof(WallDef))] public string Wall { get; set; } = "";
    public WallFadeMode FadeMode { get; set; } = WallFadeMode.Default;
    public WallFade Fade { get; set; } = new();
    /// <summary>Shapes for the bottom where the wall fades away (first = default; empty = a smooth fade).</summary>
    public List<BottomEdgeVariant> BottomEdges { get; set; } = new();
}

/// <summary>Cells on a tile painted with a wall other than the floor's default ("-" = no wall).</summary>
public sealed class TileWallPaint
{
    public const string NoWall = "-";
    [Tooltip("\"down\" or \"up\".")] public string Side { get; set; } = "down";
    [Tooltip("A wall id from the floor's list, or '-' for no wall.")] public string Wall { get; set; } = "";
    public List<CellCoord> Cells { get; set; } = new();
}

/// <summary>Which part of a wall a variant choice is for.</summary>
public enum WallPart { OuterCorner, InnerCorner, EndCap, BottomEdge }

/// <summary>A tile's choice of variant for one specific corner / end (default = the first).</summary>
public sealed class TileWallVariant
{
    public string Side { get; set; } = "down";
    public WallPart Part { get; set; }
    [Tooltip("The cell that identifies the spot: the walkway cell above an outer corner, the notch cell, or the end's top cell.")]
    public CellCoord Cell { get; set; }
    public int Index { get; set; }
}

/// <summary>A spot on a tile where a variant applies (for the editor's swap markers).</summary>
public sealed record WallJunction(WallPart Part, CellCoord Cell, Vector2 Point, WallDef Wall, int Index, bool Up, int Count)
{
    /// <summary>The name of the variant in use.</summary>
    public string VariantName(FloorWall? entry) => Part switch
    {
        WallPart.OuterCorner => Wall.Outer(Index).Name,
        WallPart.InnerCorner => Wall.Inner(Index).Name,
        WallPart.EndCap => Wall.Cap(Index).Name,
        _ => entry is { BottomEdges.Count: > 0 } e ? e.BottomEdges[Math.Clamp(Index, 0, e.BottomEdges.Count - 1)].Name : "",
    };
}

/// <summary>
/// Part of a mask kept on one side of a line, inside some cells (or the whole mask when Cells is
/// null). The line goes through (X, Y0) and leans by Lean degrees. The piece drawn first keeps a
/// hard edge just past the line; the piece drawn second fades in across it, so together they cover
/// shared cells exactly once.
/// </summary>
public sealed record ClipZone(IReadOnlyCollection<CellCoord>? Cells, float X, bool KeepLeft, bool Ramp, float Blend, float Lean = 0, float Y0 = 0)
{
    private float Tan => MathF.Tan(Lean * MathF.PI / 180f);

    public float Factor(float x, float y = 0)
    {
        if (Lean != 0) x -= Tan * (y - Y0);
        float half = Blend / 2f;
        if (!Ramp) return KeepLeft ? (x <= X + half ? 1f : 0f) : (x >= X - half ? 1f : 0f);
        float t = Blend <= 0.5f ? (x >= X ? 1f : 0f) : Math.Clamp((x - (X - half)) / Blend, 0f, 1f);
        t = t * t * (3 - 2 * t); // smoothstep
        return KeepLeft ? 1f - t : t;
    }

    public string Key => $"{X:0.#}{(KeepLeft ? "L" : "R")}{(Ramp ? "r" : "h")}{Blend:0.#}/{Lean:0.#}@{Y0:0.#}:{(Cells is null ? "all" : string.Join(' ', Cells.OrderBy(c => c.Q).ThenBy(c => c.R)))}";
}

public enum WallPieceKind { Face, Bevel, Highlight, Shadow, Rim }

/// <summary>One drawn piece of a wall on a tile.</summary>
public sealed class WallPiece
{
    public required WallDef Wall { get; init; }
    public WallPieceKind Kind { get; init; } = WallPieceKind.Face;
    public bool RightFace { get; init; }
    public bool Up { get; init; }
    public List<CellCoord> Cells { get; } = new();
    /// <summary>Where the picture's lip sits.</summary>
    public Vector2 Lip { get; set; }
    public float Skew { get; set; }
    public bool Mirror { get; set; }
    public List<ClipZone> Clips { get; } = new();
    public List<CellStencil> Stencils { get; } = new();
    public EdgeFade? Fade { get; set; }
    /// <summary>Bottom-edge shapes along the piece (need a fade to anchor to).</summary>
    public List<BottomSegment> Bottom { get; } = new();
    /// <summary>Bevel faces: their own brightness.</summary>
    public float Shade { get; set; } = 1f;
    /// <summary>Highlight / shadow strips: how strong.</summary>
    public float Opacity { get; set; } = 1f;

    public bool Rim => Kind == WallPieceKind.Rim;
    public bool IsOverlay => Kind is WallPieceKind.Highlight or WallPieceKind.Shadow;
    public string Texture => RightFace && !string.IsNullOrEmpty(Wall.RightTexture) ? Wall.RightTexture : Wall.Texture;
    public string Tint => Kind switch
    {
        WallPieceKind.Highlight => "#ffffff",
        WallPieceKind.Shadow => "#000000",
        WallPieceKind.Bevel => ArtMath.Shaded(Wall.Tint, Shade),
        _ => ArtMath.Shaded(Wall.Tint, RightFace ? Wall.RightShade : Wall.LeftShade),
    };

    /// <summary>Screen corners (TL, TR, BR, BL) of the piece's picture.</summary>
    public Vector2[] Corners(int texW, int texH)
    {
        float w = texW * Wall.Scale, h = texH * Wall.Scale;
        float top = -Wall.LipLine * h, bottom = (1 - Wall.LipLine) * h;
        var local = new[] { new Vector2(-w / 2, top), new Vector2(w / 2, top), new Vector2(w / 2, bottom), new Vector2(-w / 2, bottom) };
        var warp = Wall.Warp;
        if (warp is not null && RightFace && !string.IsNullOrEmpty(Wall.RightTexture)) warp = ArtMath.Mirrored(warp);
        if (warp is not null) for (int i = 0; i < 4; i++) local[i] += warp.Get(i);
        for (int i = 0; i < 4; i++) local[i] = new Vector2(local[i].X, local[i].Y + Skew * local[i].X);
        var anchor = Lip;
        if (Rim) anchor += new Vector2(0, Up ? Wall.RimHeight : -Wall.RimHeight);
        var pts = local.Select(p => anchor + p).ToArray();
        if (Mirror) pts = new[] { M(pts[1]), M(pts[0]), M(pts[3]), M(pts[2]) };
        return pts;
        Vector2 M(Vector2 p) => new(2 * anchor.X - p.X, p.Y);
    }
}

/// <summary>
/// Works out a tile's walls from its walkable cells, live.
///
/// Iso directions: (q+1, r) is down-right, (q, r+1) down-left, (q+1, r+1) straight down. A walkable
/// cell whose down-left neighbour isn't walkable has an open lower-left edge: a left face hangs from
/// it. Same on the right. Each face is trimmed to the width of its edges. Outer corners: the faces
/// continue into the cells under the point and meet along a (possibly leaning) line, or a narrow
/// bevel face, with an optional highlight. Inner corners (notches) split the same way, with an
/// optional shadow. Faces next to 'no wall' cells get end caps. Walls up mirror all of this upward.
/// Draw order: left faces, bevels, right faces, highlights / shadows, rims.
/// </summary>
public static class AutoWalls
{
    private const float Slope = 0.5f;

    public static List<WallPiece> Build(TileDef tile, FloorDef? floor, bool up, Func<string, WallDef?> walls, List<WallJunction>? junctions = null,
        Func<string, EndCapComboDef?>? combos = null)
    {
        var lefts = new List<WallPiece>();
        var bevels = new List<WallPiece>();
        var rights = new List<WallPiece>();
        var overlays = new List<WallPiece>();
        var rims = new List<WallPiece>();
        var entries = floor is null ? new List<FloorWall>() : up ? floor.WallsUp : floor.WallsDown;
        var resolved = entries.Select(e => (e, w: walls(e.Wall))).Where(x => x.w is not null).ToList();
        if (resolved.Count == 0) return new();
        var (defEntry, defWall) = (resolved[0].e, resolved[0].w!);
        string side = up ? "up" : "down";
        var paint = new Dictionary<CellCoord, string>();
        foreach (var p in tile.WallPaint.Where(p => p.Side == side))
            foreach (var c in p.Cells) paint[c] = p.Wall;
        var chosen = tile.WallVariants.Where(v => v.Side == side).GroupBy(v => (v.Part, v.Cell)).ToDictionary(g => g.Key, g => g.Last().Index);
        int VariantAt(WallPart part, CellCoord cell) => chosen.TryGetValue((part, cell), out var i) ? i : 0;

        int s = up ? -1 : 1;
        bool Walk(CellCoord c) => tile.IsWalkable(c);
        bool Void(CellCoord c) => IsoGrid.InTile(c) && !tile.Walkable[c];
        CellCoord LeftN(CellCoord c) => up ? new(c.Q - 1, c.R) : new(c.Q, c.R + 1);
        CellCoord RightN(CellCoord c) => up ? new(c.Q, c.R - 1) : new(c.Q + 1, c.R);
        CellCoord Down(CellCoord c) => new(c.Q + s, c.R + s);
        List<CellCoord> Column(CellCoord start, int depth)
        {
            var cells = new List<CellCoord>();
            var c = start;
            for (int i = 0; i < depth && Void(c); i++, c = Down(c)) cells.Add(c);
            return cells;
        }
        (WallDef wall, FloorWall entry)? WallFor(CellCoord columnTop)
        {
            var c = columnTop;
            for (int i = 0; i < 10 && Void(c); i++, c = Down(c))
                if (paint.TryGetValue(c, out var id))
                {
                    if (id == TileWallPaint.NoWall) return null;
                    var hit = resolved.FirstOrDefault(x => x.e.Wall == id);
                    if (hit.w is not null) return (hit.w, hit.e);
                }
            return (defWall, defEntry);
        }

        var walk = IsoGrid.AllCells.Where(Walk).ToList();
        var leftOpen = walk.Where(c => Void(LeftN(c))).ToHashSet();
        var rightOpen = walk.Where(c => Void(RightN(c))).ToHashSet();
        var outer = walk.Where(c => leftOpen.Contains(c) && rightOpen.Contains(c)).ToHashSet();
        // Notches: the walkway up-left of a notch gives it a right face, the one up-right a left face.
        var notchRight = new Dictionary<CellCoord, CellCoord>(); // walkable cell -> notch its RIGHT face meets
        var notchLeft = new Dictionary<CellCoord, CellCoord>();  // walkable cell -> notch its LEFT face meets
        foreach (var v in IsoGrid.AllCells.Where(Void))
        {
            var a = up ? new CellCoord(v.Q, v.R + 1) : new CellCoord(v.Q - 1, v.R);   // right face comes from here
            var b = up ? new CellCoord(v.Q + 1, v.R) : new CellCoord(v.Q, v.R - 1);   // left face comes from here
            if (Walk(a) && Walk(b) && rightOpen.Contains(a) && leftOpen.Contains(b)) { notchRight[a] = v; notchLeft[b] = v; }
        }
        var reportedInner = new HashSet<CellCoord>();
        var cornerExtras = new Dictionary<CellCoord, List<WallPiece>>();

        foreach (bool isLeft in new[] { true, false })
        {
            var open = isLeft ? leftOpen : rightOpen;
            // Cells whose open edges lie on one straight line, and their order along it:
            //   down: lower-left edges of (q, r), (q+1, r)...   lower-right edges of (q, r), (q, r+1)...
            //   up:   upper-left edges of (q, r), (q, r-1)...   upper-right edges of (q, r), (q+1, r)...
            Func<CellCoord, int> key = (isLeft ^ up) ? (c => c.R) : (c => c.Q);
            Func<CellCoord, int> pos = !up ? (isLeft ? (c => c.Q) : (c => c.R)) : (isLeft ? (c => -c.R) : (c => c.Q));
            foreach (var line in open.GroupBy(key))
            {
                var sorted = line.OrderBy(pos).ToList();
                var run = new List<CellCoord>();
                (WallDef wall, FloorWall entry)? runWall = null;
                // End caps only where a wall really stops: next to cells painted 'no wall'.
                bool startCap = false;
                void Flush(bool endCap)
                {
                    if (run.Count > 0 && runWall is { } rw) MakeFace(rw.wall, rw.entry, run.ToList(), isLeft, startCap, endCap);
                    run.Clear();
                    startCap = false;
                }
                CellCoord? prev = null;
                foreach (var w in sorted)
                {
                    var choice = WallFor(isLeft ? LeftN(w) : RightN(w));
                    bool adjacent = prev is { } p && pos(w) == pos(p) + 1;
                    if (!adjacent) { Flush(false); startCap = false; }
                    if (choice is null) { Flush(true); startCap = true; prev = w; continue; }
                    if (run.Count > 0 && (choice.Value.wall != runWall?.wall || run.Count >= runWall!.Value.wall.MaxRun)) Flush(false);
                    if (run.Count == 0) runWall = choice;
                    run.Add(w);
                    prev = w;
                }
                Flush(false);
            }
        }
        return lefts.Concat(bevels).Concat(rights).Concat(overlays).Concat(rims).ToList();

        void MakeFace(WallDef wall, FloorWall entry, List<CellCoord> run, bool isLeft, bool capStart, bool capEnd)
        {
            bool customRight = !isLeft && !string.IsNullOrEmpty(wall.RightTexture);
            bool mirror = !isLeft && !customRight;
            float desired = !wall.SkewFaces ? 0 : (isLeft ^ up ? Slope : -Slope);
            var fade = FadeFor(entry);
            var piece = new WallPiece
            {
                Wall = wall, RightFace = !isLeft, Up = up,
                Skew = mirror ? -desired : desired, Mirror = mirror,
                Lip = run.Select(w => EdgeMid(w, isLeft, up)).Aggregate(Vector2.Zero, (a, b) => a + b) / run.Count,
                Fade = fade,
            };
            foreach (var w in run) piece.Cells.AddRange(Column(isLeft ? LeftN(w) : RightN(w), wall.Depth));

            // The face's own width: from its edges' leftmost to rightmost point (trimmed below,
            // except on a side that meets a corner, which its seam handles).
            float minX = float.MaxValue, maxX = float.MinValue;
                foreach (var w in run)
            {
                float cx = IsoGrid.CellCenter(w).X;
                float a = isLeft ? cx - IsoGrid.HalfW : cx, b = isLeft ? cx : cx + IsoGrid.HalfW;
                minX = Math.Min(minX, a); maxX = Math.Max(maxX, b);
            }
            bool cornerAtMin = false, cornerAtMax = false;

            foreach (var w in run)
            {
                // Outer corner under this cell's point.
                if (outer.Contains(w))
                {
                    var col = Column(Down(w), wall.Depth);
                    if (col.Count > 0)
                    {
                        var v = wall.Outer(VariantAt(WallPart.OuterCorner, w));
                        var point = IsoGrid.CellCenter(w) + new Vector2(0, s * IsoGrid.HalfH);
                        float x = point.X, bw = v.Style == CornerStyle.Bevel ? v.BevelWidth : 0;
                        piece.Cells.AddRange(col);
                        // Left face keeps the left side (drawn first, hard); right face fades in.
                        // Along the whole line (not just inside the corner column): the cells either side
                        // interlock like teeth, and cutting only one column left a zig-zag down the corner.
                        piece.Clips.Add(isLeft
                            ? new ClipZone(null, x - bw / 2, KeepLeft: true, Ramp: false, v.Blend, v.Lean, point.Y)
                            : new ClipZone(null, x + bw / 2, KeepLeft: false, Ramp: true, v.Blend, v.Lean, point.Y));
                        // The point is on the left face's right-hand end and the right face's left-hand end.
                        if (isLeft) cornerAtMax = true; else cornerAtMin = true;
                        if (isLeft) cornerExtras[w] = AddOuterCornerExtras(wall, v, col, point, fade, w);
                    }
                }
                // Inner corner (notch) this cell shares.
                if ((isLeft ? notchLeft : notchRight).TryGetValue(w, out var notch))
                {
                    var col = Column(notch, wall.Depth);
                    if (col.Count > 0)
                    {
                        var v = wall.Inner(VariantAt(WallPart.InnerCorner, notch));
                        var point = IsoGrid.CellCenter(notch) - new Vector2(0, s * IsoGrid.HalfH);
                        // The right face (from the up-left walkway) keeps the left half and fades in;
                        // the left face (from the up-right walkway, drawn first) keeps the right half.
                        piece.Clips.Add(new ClipZone(null, point.X, KeepLeft: !isLeft, Ramp: !isLeft, v.Blend, v.Lean, point.Y));
                        if (isLeft) cornerAtMin = true; else cornerAtMax = true;
                        if (reportedInner.Add(notch))
                        {
                            junctions?.Add(new WallJunction(WallPart.InnerCorner, notch, point, wall, VariantAt(WallPart.InnerCorner, notch), up, wall.InnerCorners.Count));
                            if (v.Shadow > 0)
                            {
                                var shadow = Strip(wall, WallPieceKind.Shadow, Around(point, wall.Depth), point, v.ShadowWidth, v.Lean, fade);
                                shadow.Opacity = v.Shadow;
                                overlays.Add(shadow);
                            }
                        }
                    }
                }
            }

            // End caps where the wall stops (next to 'no wall' cells).
            foreach (var (w, isStart) in new[] { (run[0], true), (run[^1], false) })
            {
                if (!(isStart ? capStart : capEnd)) continue;
                if (outer.Contains(w) || (isLeft ? notchLeft : notchRight).ContainsKey(w)) continue;
                var col = Column(isLeft ? LeftN(w) : RightN(w), wall.Depth);
                if (col.Count == 0) continue;
                var cap = wall.Cap(VariantAt(WallPart.EndCap, col[0]));
                float cx = IsoGrid.CellCenter(col[0]).X;
                bool endOnRight = cx >= piece.Lip.X;
                junctions?.Add(new WallJunction(WallPart.EndCap, col[0], IsoGrid.CellCenter(col[0]), wall, VariantAt(WallPart.EndCap, col[0]), up, wall.EndCapVariants.Count));
                if (cap.Style == EndCapStyle.Soft)
                    piece.Clips.Add(new ClipZone(null, cx, KeepLeft: endOnRight, Ramp: true, cap.Softness));
                else if (cap.Style == EndCapStyle.Stencil)
                {
                    // Row by row down the end's column: keep, shape with a mask, or hide. Masks are
                    // drawn for a right-hand end and mirrored for a left-hand one.
                    var rows = cap.RowsFrom(combos);
                    for (int i = 0; i < col.Count && i < rows.Count; i++)
                    {
                        var row = rows[i];
                        if (row.Mode == EndCapRowMode.Keep) continue;
                        piece.Stencils.Add(new CellStencil
                        {
                            Cell = col[i], Hide = row.Mode == EndCapRowMode.Hide, Stencil = row.Stencil, FlipX = !endOnRight, FlipY = row.FlipY ^ up,
                            Rotation = row.Rotation, OffsetX = row.OffsetX, OffsetY = up ? -row.OffsetY : row.OffsetY, Scale = row.Scale,
                        });
                    }
                }
            }

            // Bottom edges: each edge cell's stretch of the face can use its own variant.
            if (fade is not null && entry.BottomEdges.Count > 0)
                foreach (var w in run)
                {
                    float cx = IsoGrid.CellCenter(w).X;
                    float a = isLeft ? cx - IsoGrid.HalfW : cx, b = isLeft ? cx : cx + IsoGrid.HalfW;
                    var top = isLeft ? LeftN(w) : RightN(w);
                    int vi = Math.Clamp(VariantAt(WallPart.BottomEdge, top), 0, entry.BottomEdges.Count - 1);
                    var be = entry.BottomEdges[vi];
                    // Corners: stretch the end segments over the seam so both sides of a corner match.
                    if (outer.Contains(w) || (isLeft ? notchLeft : notchRight).ContainsKey(w)) { a -= 40; b += 40; }
                    if (!string.IsNullOrEmpty(be.Stencil))
                    {
                        var seg = new BottomSegment(a, b, be.Stencil, be.Offset, be.Height, be.Repeat, be.FlipX);
                        piece.Bottom.Add(seg);
                        // The corner's bevel / highlight end the same way as the face beside them.
                        if (cornerExtras.TryGetValue(w, out var extras)) foreach (var x in extras) x.Bottom.Add(seg with { MinX = cx - 80, MaxX = cx + 80 });
                    }
                    junctions?.Add(new WallJunction(WallPart.BottomEdge, top, EdgeMid(w, isLeft, up) + new Vector2(0, s * (fade.Value.StartCells + fade.Value.LengthCells * 0.5f) * EdgeFade.CellPx), wall, vi, up, entry.BottomEdges.Count));
                }

            // Trim to the edges' width (whole mask: the soft edge spills past cell borders).
            if (!cornerAtMax) piece.Clips.Add(new ClipZone(null, maxX + 0.5f, KeepLeft: true, Ramp: false, 0));
            if (!cornerAtMin) piece.Clips.Add(new ClipZone(null, minX - 0.5f, KeepLeft: false, Ramp: false, 0));
            var distinct = piece.Cells.Distinct().ToList();
            piece.Cells.Clear();
            piece.Cells.AddRange(distinct);
            if (piece.Cells.Count == 0) return;
            (isLeft ? lefts : rights).Add(piece);
            if (wall.Rim) rims.Add(MakeRim(wall, run, isLeft, piece));
        }

        // Bevel face + highlight for an outer corner (added once, with the left face). Returns them.
        List<WallPiece> AddOuterCornerExtras(WallDef wall, OuterCornerVariant v, List<CellCoord> col, Vector2 point, EdgeFade? fade, CellCoord w)
        {
            var made = new List<WallPiece>();
            junctions?.Add(new WallJunction(WallPart.OuterCorner, w, point, wall, VariantAt(WallPart.OuterCorner, w), up, wall.OuterCorners.Count));
            float x = point.X;
            float edge = x;
            if (v.Style == CornerStyle.Bevel && v.BevelWidth > 0)
            {
                // A narrow face looking straight out of the corner, between the left and right faces.
                var bevel = new WallPiece
                {
                    Wall = wall, Kind = WallPieceKind.Bevel, Up = up, Lip = point, Skew = 0, Mirror = false, Fade = fade,
                    Shade = v.BevelShade ?? (wall.LeftShade + wall.RightShade) / 2f,
                };
                bevel.Cells.AddRange(Around(point, wall.Depth));
                bevel.Clips.Add(new ClipZone(null, x - v.BevelWidth / 2, KeepLeft: false, Ramp: true, v.Blend, v.Lean, point.Y));
                bevel.Clips.Add(new ClipZone(null, x + v.BevelWidth / 2, KeepLeft: true, Ramp: false, v.Blend, v.Lean, point.Y));
                bevels.Add(bevel);
                made.Add(bevel);
                edge = x - v.BevelWidth / 2; // the lit edge: where the left face turns onto the bevel
            }
            if (v.Highlight > 0)
            {
                var hl = Strip(wall, WallPieceKind.Highlight, Around(point, wall.Depth), new Vector2(edge, point.Y), v.HighlightWidth, v.Lean, fade);
                hl.Opacity = v.Highlight;
                overlays.Add(hl);
                made.Add(hl);
            }
            return made;
        }

        // The empty cells under (or over) a corner point, one column either side included: a band down
        // the corner line crosses the tips of the neighbouring columns too.
        List<CellCoord> Around(Vector2 point, int depth)
        {
            float reach = depth * IsoGrid.CellHeight + IsoGrid.CellHeight;
            return IsoGrid.AllCells.Where(c =>
            {
                if (!Void(c)) return false;
                var p = IsoGrid.CellCenter(c);
                float dy = (p.Y - point.Y) * s;
                return Math.Abs(p.X - point.X) <= IsoGrid.HalfW + 1 && dy > -IsoGrid.HalfH && dy <= reach;
            }).ToList();
        }

        // A soft vertical (or leaning) band, e.g. a corner highlight or a notch shadow.
        WallPiece Strip(WallDef wall, WallPieceKind kind, List<CellCoord> col, Vector2 at, float width, float lean, EdgeFade? fade)
        {
            var strip = new WallPiece { Wall = wall, Kind = kind, Up = up, Lip = at, Fade = fade };
            strip.Cells.AddRange(col);
            float half = Math.Max(1f, width / 2f);
            strip.Clips.Add(new ClipZone(null, at.X - half / 2, KeepLeft: false, Ramp: true, half, lean, at.Y));
            strip.Clips.Add(new ClipZone(null, at.X + half / 2, KeepLeft: true, Ramp: true, half, lean, at.Y));
            return strip;
        }

        WallPiece MakeRim(WallDef wall, List<CellCoord> run, bool isLeft, WallPiece face)
        {
            var rim = new WallPiece { Wall = wall, Kind = WallPieceKind.Rim, RightFace = face.RightFace, Up = up, Skew = face.Skew, Mirror = face.Mirror, Lip = face.Lip };
            rim.Cells.AddRange(run);
            float cells = Math.Max(0.05f, wall.RimHeight / EdgeFade.CellPx);
            var angle = !wall.SkewFaces ? FadeAngle.Level : (isLeft ^ up ? FadeAngle.DownRight : FadeAngle.DownLeft);
            rim.Fade = new EdgeFade(Lower: up, 0, cells, angle);
            return rim;
        }

        EdgeFade? FadeFor(FloorWall entry)
        {
            WallFade? f = entry.FadeMode switch
            {
                WallFadeMode.None => null,
                WallFadeMode.Own => entry.Fade,
                _ => (up ? floor?.WallUpFade : floor?.WallDownFade) ?? new WallFade(),
            };
            return f is null ? null : new EdgeFade(!up, f.Start, f.Length, f.Angle);
        }
    }

    /// <summary>Midpoint of a cell's lower-left / lower-right (or upper) edge.</summary>
    public static Vector2 EdgeMid(CellCoord c, bool left, bool up)
    {
        var p = IsoGrid.CellCenter(c);
        return p + new Vector2(left ? -IsoGrid.HalfW / 2 : IsoGrid.HalfW / 2, (up ? -1 : 1) * IsoGrid.HalfH / 2);
    }
}
