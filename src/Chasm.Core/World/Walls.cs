using System.Numerics;
using Chasm.Core.Content;
using Chasm.Core.Grid;

namespace Chasm.Core.World;

public enum EndCapStyle
{
    [Label("None (hard cut)")] None,
    [Label("Soft (fades out)")] Soft,
    [Label("Stencil (carved shape)")] Stencil,
}

/// <summary>
/// One wall / cliff look ("Grey wall"), made from a single left-facing picture. Every variation is
/// worked out when a tile is drawn: right face (mirrored, or your own art), corners (the two faces
/// meet along a soft seam), end caps and an optional rim along the walkway edge.
/// Floors list walls (Walls down / Walls up); the first in a list draws automatically.
/// </summary>
[DefFolder("walls", "Walls")]
[Tooltip("A wall / cliff look made from one left-facing picture.")]
public sealed class WallDef : Def
{
    [Tooltip("Grouping in lists.")] public string Category { get; set; } = "Cliffs";
    [Tooltip("Wall down = cliffs under walkway edges. Wall up = walls rising above them.")]
    public MaterialKind Kind { get; set; } = MaterialKind.WallDown;

    [Texture, Required, Tooltip("The left face: a long strip of wall, facing down-left. Seamless left-right is best.")]
    public string Texture { get; set; } = "";
    [Range(0.1f, 4)] public float Scale { get; set; } = 1f;
    [Range(0, 1), Tooltip("Where the top edge (wall down) or foot (wall up) is in the picture: 0 = top, 1 = bottom.")]
    public float LipLine { get; set; }
    public string Tint { get; set; } = "#ffffff";
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    /// <summary>Perspective warp of the left face, in the picture's own frame (the right face gets it mirrored).</summary>
    public MaterialWarp? Warp { get; set; }

    [Range(0.2f, 1.5f), Tooltip("Brightness of the left faces (1 = as painted).")] public float LeftShade { get; set; } = 1f;
    [Range(0.2f, 1.5f), Tooltip("Brightness of the right faces. Lower = in shadow, which reads as 3D.")] public float RightShade { get; set; } = 0.72f;
    [Texture, Tooltip("Optional: your own right-face art. Empty = the left face, mirrored.")] public string RightTexture { get; set; } = "";

    [Range(1, 10), Tooltip("How many cells each face reaches (the fade usually hides the end).")] public int Depth { get; set; } = 4;
    [Range(1, 24), Tooltip("A straight edge longer than this becomes several pieces (so the picture never runs out).")]
    public int MaxRun { get; set; } = 12;
    [Tooltip("Slant faces along the cell edges so vertical lines stay vertical (isometric look).")] public bool SkewFaces { get; set; } = true;

    [Range(0, 80), Tooltip("Width of the soft seam where a left and right face meet at a corner (pixels).")]
    public float CornerBlend { get; set; } = 12f;
    [Tooltip("How a wall ends where it stops partway along an edge.")] public EndCapStyle EndCaps { get; set; } = EndCapStyle.Soft;
    [Range(4, 128), Tooltip("Soft end caps: how wide the fade-out is (pixels).")] public float EndCapSoftness { get; set; } = 48f;
    [Texture, Tooltip("Stencil end caps: the carve shape for the last cell (art/stencils).")] public string EndCapStencil { get; set; } = "stencils/notch_right.png";

    [Tooltip("Draw a thin strip of the picture's top over the walkway edge, hiding the seam.")] public bool Rim { get; set; }
    [Range(2, 64), Tooltip("Rim height in pixels.")] public float RimHeight { get; set; } = 14f;
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
}

/// <summary>Cells on a tile painted with a wall other than the floor's default ("-" = no wall).</summary>
public sealed class TileWallPaint
{
    public const string NoWall = "-";
    [Tooltip("\"down\" or \"up\".")] public string Side { get; set; } = "down";
    [Tooltip("A wall id from the floor's list, or '-' for no wall.")] public string Wall { get; set; } = "";
    public List<CellCoord> Cells { get; set; } = new();
}

/// <summary>
/// Part of a mask that's only kept on one side of a vertical line, inside some cells: how two
/// faces share a corner cell. The piece drawn first keeps a hard edge just past the line; the
/// piece drawn second fades in across it, so together they cover the cell exactly once.
/// </summary>
public sealed record ClipZone(IReadOnlyCollection<CellCoord> Cells, float X, bool KeepLeft, bool Ramp, float Blend)
{
    public float Factor(float x)
    {
        float half = Blend / 2f;
        if (!Ramp) return KeepLeft ? (x <= X + half ? 1f : 0f) : (x >= X - half ? 1f : 0f);
        float t = Blend <= 0.5f ? (x >= X ? 1f : 0f) : Math.Clamp((x - (X - half)) / Blend, 0f, 1f);
        t = t * t * (3 - 2 * t); // smoothstep
        return KeepLeft ? 1f - t : t;
    }

    public string Key => $"{X:0.#}{(KeepLeft ? "L" : "R")}{(Ramp ? "r" : "h")}{Blend:0.#}:{string.Join(' ', Cells.OrderBy(c => c.Q).ThenBy(c => c.R))}";
}

/// <summary>One drawn piece of a wall on a tile: a face run (with its corners and caps) or a rim strip.</summary>
public sealed class WallPiece
{
    public required WallDef Wall { get; init; }
    public bool RightFace { get; init; }
    public bool Up { get; init; }
    public List<CellCoord> Cells { get; } = new();
    /// <summary>Middle of the run's open edge: the picture's lip sits here.</summary>
    public Vector2 Lip { get; set; }
    public float Skew { get; set; }
    public bool Mirror { get; set; }
    public List<ClipZone> Clips { get; } = new();
    public List<CellStencil> Stencils { get; } = new();
    public EdgeFade? Fade { get; set; }
    /// <summary>A rim strip (drawn over the walkway) rather than a face.</summary>
    public bool Rim { get; init; }

    public string Texture => RightFace && !string.IsNullOrEmpty(Wall.RightTexture) ? Wall.RightTexture : Wall.Texture;
    public string Tint => ArtMath.Shaded(Wall.Tint, RightFace ? Wall.RightShade : Wall.LeftShade);

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
/// Works out a tile's walls from its walkable cells, live (nothing is stored but the floor's wall
/// lists and the tile's painted exceptions).
///
/// Iso directions: (q+1, r) is down-right, (q, r+1) down-left, (q+1, r+1) straight down. A walkable
/// cell whose down-left neighbour isn't walkable has an open lower-left edge: a left face hangs from
/// it, over the empty cells straight below. Same on the right. Corners: where a left and right face
/// meet (a point sticking out, or a notch going in) both faces continue into the shared cells and
/// split down the middle with a soft seam. Where a face stops partway it gets an end cap. Walls up
/// mirror all of this upward.
/// </summary>
public static class AutoWalls
{
    private const float Slope = 0.5f;

    private sealed record Edge(CellCoord Walk, bool Left, WallDef Wall, FloorWall Entry);

    public static List<WallPiece> Build(TileDef tile, FloorDef? floor, bool up, Func<string, WallDef?> walls)
    {
        var pieces = new List<WallPiece>();
        var entries = floor is null ? new List<FloorWall>() : up ? floor.WallsUp : floor.WallsDown;
        var resolved = entries.Select(e => (e, w: walls(e.Wall))).Where(x => x.w is not null).ToList();
        if (resolved.Count == 0) return pieces;
        var (defEntry, defWall) = (resolved[0].e, resolved[0].w!);
        string side = up ? "up" : "down";
        var paint = new Dictionary<CellCoord, string>();
        foreach (var p in tile.WallPaint.Where(p => p.Side == side))
            foreach (var c in p.Cells) paint[c] = p.Wall;

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
        // Which wall an edge uses: a painted cell in its column wins, else the default.
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

        // Corner cells: outer = under a cell open on both sides; inner = a notch between two walkways.
        var outer = walk.Where(c => leftOpen.Contains(c) && rightOpen.Contains(c)).ToHashSet();
        var innerTop = new Dictionary<CellCoord, CellCoord>(); // walkable cell -> notch cell it shares
        foreach (var v in IsoGrid.AllCells.Where(Void))
        {
            var a = up ? new CellCoord(v.Q, v.R + 1) : new CellCoord(v.Q - 1, v.R);   // right face comes from here
            var b = up ? new CellCoord(v.Q + 1, v.R) : new CellCoord(v.Q, v.R - 1);   // left face comes from here
            if (Walk(a) && Walk(b) && rightOpen.Contains(a) && leftOpen.Contains(b)) { innerTop[a] = v; innerTop[b] = v; }
        }

        foreach (bool isLeft in new[] { true, false }) // left faces first: corner seams rely on this order
        {
            var open = isLeft ? leftOpen : rightOpen;
            Func<CellCoord, int> key = isLeft ? (c => c.R) : (c => c.Q);
            Func<CellCoord, int> pos = isLeft ? (c => c.Q) : (c => c.R);
            foreach (var line in open.GroupBy(key))
            {
                var sorted = line.OrderBy(pos).ToList();
                var run = new List<CellCoord>();
                (WallDef wall, FloorWall entry)? runWall = null;
                // End caps only where a wall really stops: next to cells painted 'no wall'. Steps,
                // turns and corners carry on into the neighbouring face, and splits (piece length,
                // a different wall) continue straight on, so none of those get a cap.
                bool startCap = false;
                void Flush(bool endCap)
                {
                    if (run.Count > 0 && runWall is { } rw)
                    {
                        var piece = MakeFace(rw.wall, rw.entry, run, isLeft, startCap, endCap);
                        if (piece is not null) pieces.Add(piece);
                        if (piece is not null && rw.wall.Rim) pieces.Add(MakeRim(rw.wall, run, isLeft, piece));
                    }
                    run.Clear();
                    startCap = false;
                }
                CellCoord? prev = null;
                foreach (var w in sorted)
                {
                    var top = isLeft ? LeftN(w) : RightN(w);
                    var choice = WallFor(top);
                    bool adjacent = prev is { } p && pos(w) == pos(p) + 1;
                    if (!adjacent) { Flush(false); startCap = false; }
                    if (choice is null)
                    {
                        Flush(true);        // the face before this cell stops here
                        startCap = true;    // and the next one starts here
                        prev = w;
                        continue;
                    }
                    if (run.Count > 0 && (choice.Value.wall != runWall?.wall || run.Count >= runWall!.Value.wall.MaxRun)) Flush(false);
                    if (run.Count == 0) runWall = choice;
                    run.Add(w);
                    prev = w;
                }
                Flush(false);
            }
        }
        return pieces;

        WallPiece? MakeFace(WallDef wall, FloorWall entry, List<CellCoord> run, bool isLeft, bool capStart, bool capEnd)
        {
            bool customRight = !isLeft && !string.IsNullOrEmpty(wall.RightTexture);
            bool mirror = !isLeft && !customRight;
            float desired = !wall.SkewFaces ? 0 : (isLeft ^ up ? Slope : -Slope);
            var piece = new WallPiece
            {
                Wall = wall, RightFace = !isLeft, Up = up,
                Skew = mirror ? -desired : desired, Mirror = mirror,
                Lip = run.Select(w => EdgeMid(w, isLeft, up)).Aggregate(Vector2.Zero, (a, b) => a + b) / run.Count,
                Fade = FadeFor(entry, wall),
            };
            foreach (var w in run) piece.Cells.AddRange(Column(isLeft ? LeftN(w) : RightN(w), wall.Depth));

            // Corners: continue into the shared column and split it down the middle.
            foreach (var w in run)
            {
                if (outer.Contains(w))
                {
                    var col = Column(Down(w), wall.Depth);
                    if (col.Count == 0) continue;
                    piece.Cells.AddRange(col);
                    float x = IsoGrid.CellCenter(w).X;
                    // Left face keeps the left half (drawn first, hard); right face fades in.
                    piece.Clips.Add(new ClipZone(col, x, KeepLeft: isLeft, Ramp: !isLeft, wall.CornerBlend));
                }
                if (innerTop.TryGetValue(w, out var notch))
                {
                    var col = Column(notch, wall.Depth);
                    if (col.Count == 0) continue;
                    float x = IsoGrid.CellCenter(notch).X;
                    // Notch: the right face (from the up-left walkway) keeps the left half and fades;
                    // the left face (from the up-right walkway, drawn first) keeps the right half, hard.
                    piece.Clips.Add(new ClipZone(col, x, KeepLeft: !isLeft, Ramp: !isLeft, wall.CornerBlend));
                }
            }

            // End caps where the wall stops (next to 'no wall' cells).
            if (wall.EndCaps != EndCapStyle.None)
            {
                foreach (var (w, isStart) in new[] { (run[0], true), (run[^1], false) })
                {
                    if (!(isStart ? capStart : capEnd)) continue;
                    if (outer.Contains(w) || innerTop.ContainsKey(w)) continue;
                    var col = Column(isLeft ? LeftN(w) : RightN(w), wall.Depth);
                    if (col.Count == 0) continue;
                    // Which side of the piece is this end? Compare with the run's middle.
                    float cx = IsoGrid.CellCenter(col[0]).X;
                    bool endOnRight = cx >= piece.Lip.X;
                    if (wall.EndCaps == EndCapStyle.Soft)
                        piece.Clips.Add(new ClipZone(col, cx, KeepLeft: endOnRight, Ramp: true, wall.EndCapSoftness));
                    else if (!string.IsNullOrEmpty(wall.EndCapStencil))
                        foreach (var c in col) piece.Stencils.Add(new CellStencil { Cell = c, Stencil = wall.EndCapStencil, FlipX = !endOnRight });
                }
            }
            var distinct = piece.Cells.Distinct().ToList();
            piece.Cells.Clear();
            piece.Cells.AddRange(distinct);
            return piece.Cells.Count == 0 ? null : piece;
        }

        WallPiece MakeRim(WallDef wall, List<CellCoord> run, bool isLeft, WallPiece face)
        {
            var rim = new WallPiece { Wall = wall, RightFace = face.RightFace, Up = up, Skew = face.Skew, Mirror = face.Mirror, Lip = face.Lip, Rim = true };
            rim.Cells.AddRange(run); // over the walkway cells along the edge
            // Solid at the edge, fading out over RimHeight into the walkway.
            float cells = Math.Max(0.05f, wall.RimHeight / EdgeFade.CellPx);
            var angle = !wall.SkewFaces ? FadeAngle.Level : (isLeft ^ up ? FadeAngle.DownRight : FadeAngle.DownLeft);
            rim.Fade = new EdgeFade(Lower: up, 0, cells, angle);
            return rim;
        }

        EdgeFade? FadeFor(FloorWall entry, WallDef wall)
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
