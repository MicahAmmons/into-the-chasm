using System.Numerics;
using Chasm.Core.Content;
using Chasm.Core.Grid;

namespace Chasm.Core.World;

/// <summary>A floor's kit for automatic cliffs (or walls going up): which prefab goes where.</summary>
public sealed class CliffKit
{
    [Ref(typeof(PrefabDef)), Tooltip("Faces under the lower-left edges (walls up: upper-left). Required.")]
    public string LeftFace { get; set; } = "";
    [Ref(typeof(PrefabDef)), Tooltip("Faces under the lower-right edges. Empty = the left face, mirrored.")]
    public string RightFace { get; set; } = "";
    [Ref(typeof(PrefabDef)), Tooltip("Where a left and right face meet at a point sticking out. Optional.")]
    public string OuterCorner { get; set; } = "";
    [Ref(typeof(PrefabDef)), Tooltip("Where faces meet in a notch going in. Optional.")]
    public string InnerCorner { get; set; } = "";
    [Range(1, 10), Tooltip("How many cells deep each face reaches (the fade usually hides the bottom).")]
    public int Depth { get; set; } = 4;
    [Range(1, 24), Tooltip("Longest run of edge cells one piece covers before a new piece starts.")]
    public int MaxRun { get; set; } = 6;
    [Tooltip("Slant faces to follow the cell edge, keeping vertical lines vertical (isometric look).")]
    public bool SkewFaces { get; set; } = true;
}

/// <summary>
/// Places cliff (or wall) prefabs along the edges of a tile's walkable area.
///
/// Iso directions: (q+1, r) is down-right, (q, r+1) is down-left, (q+1, r+1) is straight down.
/// A walkable cell whose down-left neighbour isn't walkable has an open lower-left edge: a left
/// face hangs from it, covering the empty cells straight below for Depth cells. Same on the right.
/// A cell open on both lower edges gets an outer corner under its bottom point; an empty cell with
/// walkable cells up-left and up-right of it is a notch and gets an inner corner. Walls going up
/// mirror all of this upward. Runs of faces along an edge become one piece (up to MaxRun cells).
/// </summary>
public static class AutoCliffs
{
    public const string TagDown = "down", TagUp = "up";
    private const float Slope = 0.5f;

    public static List<PaintRegion> Generate(TileDef tile, CliffKit kit, bool up, Func<string, PrefabDef?> prefabs, Func<IEnumerable<string>, string> newId)
    {
        var result = new List<PaintRegion>();
        var left = prefabs(kit.LeftFace);
        if (left is null) return result;
        var right = string.IsNullOrEmpty(kit.RightFace) ? null : prefabs(kit.RightFace);
        var outer = string.IsNullOrEmpty(kit.OuterCorner) ? null : prefabs(kit.OuterCorner);
        var inner = string.IsNullOrEmpty(kit.InnerCorner) ? null : prefabs(kit.InnerCorner);
        int s = up ? -1 : 1; // which way "below" is
        string tag = up ? TagUp : TagDown;
        bool Walk(CellCoord c) => tile.IsWalkable(c);
        bool Void(CellCoord c) => IsoGrid.InTile(c) && !tile.Walkable[c];

        // Neighbours across the two lower (or upper) edges, and straight down (or up).
        CellCoord LeftN(CellCoord c) => up ? new(c.Q - 1, c.R) : new(c.Q, c.R + 1);
        CellCoord RightN(CellCoord c) => up ? new(c.Q, c.R - 1) : new(c.Q + 1, c.R);
        CellCoord Down(CellCoord c) => new(c.Q + s, c.R + s);

        List<CellCoord> Column(CellCoord start)
        {
            var cells = new List<CellCoord>();
            var c = start;
            for (int i = 0; i < kit.Depth && Void(c); i++, c = Down(c)) cells.Add(c);
            return cells;
        }

        var walk = IsoGrid.AllCells.Where(Walk).ToList();
        var leftEdges = walk.Where(c => Void(LeftN(c))).ToHashSet();
        var rightEdges = walk.Where(c => Void(RightN(c))).ToHashSet();
        var usedByCorner = new HashSet<CellCoord>();

        // Inner corners: an empty cell whose two upper (or lower) neighbours are walkable.
        if (inner is not null)
            foreach (var v in IsoGrid.AllCells.Where(Void))
            {
                var a = up ? new CellCoord(v.Q + 1, v.R) : new CellCoord(v.Q - 1, v.R);
                var b = up ? new CellCoord(v.Q, v.R + 1) : new CellCoord(v.Q, v.R - 1);
                if (!Walk(a) || !Walk(b)) continue;
                var cells = Column(v);
                if (cells.Count == 0) continue;
                foreach (var c in cells) usedByCorner.Add(c);
                var point = up ? IsoGrid.CellCenter(v) + new Vector2(0, IsoGrid.HalfH) : IsoGrid.CellCenter(v) - new Vector2(0, IsoGrid.HalfH);
                result.Add(Piece(inner, cells, point, 0, false, tag, newId, result));
            }

        // Outer corners: a walkable cell open on both lower (upper) edges.
        if (outer is not null)
            foreach (var w in walk.Where(c => leftEdges.Contains(c) && rightEdges.Contains(c)))
            {
                var cells = Column(Down(w)).Where(c => !usedByCorner.Contains(c)).ToList();
                if (cells.Count == 0) continue;
                foreach (var c in cells) usedByCorner.Add(c);
                var point = IsoGrid.CellCenter(w) + new Vector2(0, s * IsoGrid.HalfH);
                result.Add(Piece(outer, cells, point, 0, false, tag, newId, result));
            }

        // Faces: runs of open edges along the edge's line.
        //   left edges of (q, r) and (q+1, r) are one line (down: slope +0.5 on screen)
        //   right edges of (q, r) and (q, r+1) are one line (down: slope -0.5)
        foreach (bool isLeft in new[] { true, false })
        {
            var edges = isLeft ? leftEdges : rightEdges;
            var prefab = isLeft ? left : right ?? left;
            bool mirror = !isLeft && right is null;
            // Direction along the line, and the key that stays constant on it.
            Func<CellCoord, int> key = isLeft ? (c => c.R) : (c => c.Q);
            Func<CellCoord, int> pos = isLeft ? (c => c.Q) : (c => c.R);
            foreach (var line in edges.GroupBy(key))
            {
                var sorted = line.OrderBy(pos).ToList();
                var run = new List<CellCoord>();
                void Flush()
                {
                    if (run.Count == 0) return;
                    var cells = run.SelectMany(w => Column(isLeft ? LeftN(w) : RightN(w))
                            // No outer corner piece: the two faces share the cell under the point between them.
                            .Concat(outer is null && leftEdges.Contains(w) && rightEdges.Contains(w) ? Column(Down(w)) : Enumerable.Empty<CellCoord>()))
                        .Where(c => !usedByCorner.Contains(c)).Distinct().ToList();
                    if (cells.Count > 0)
                    {
                        // Middle of the run's open edge: average of each edge's midpoint.
                        var mid = run.Select(w => EdgeMid(w, isLeft, up)).Aggregate(Vector2.Zero, (a, b) => a + b) / run.Count;
                        // Down: left edge slopes +0.5, right -0.5. Up: the reverse.
                        float skew = !kit.SkewFaces ? 0 : (isLeft ^ up ? Slope : -Slope);
                        // A mirrored left face: skew as the left face, then the mirror turns it.
                        if (mirror) skew = -skew;
                        result.Add(Piece(prefab, cells, mid, skew, mirror, tag, newId, result));
                    }
                    run.Clear();
                }
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (run.Count > 0 && (pos(sorted[i]) != pos(run[^1]) + 1 || run.Count >= kit.MaxRun)) Flush();
                    run.Add(sorted[i]);
                }
                Flush();
            }
        }
        return result;
    }

    /// <summary>Midpoint of a cell's lower-left / lower-right (or upper) edge.</summary>
    private static Vector2 EdgeMid(CellCoord c, bool left, bool up)
    {
        var p = IsoGrid.CellCenter(c);
        float dx = left ? -IsoGrid.HalfW / 2 : IsoGrid.HalfW / 2;
        float dy = (up ? -1 : 1) * IsoGrid.HalfH / 2;
        return p + new Vector2(dx, dy);
    }

    /// <summary>A placement whose lip (anchor) lands exactly on <paramref name="lipPoint"/>.</summary>
    private static PaintRegion Piece(PrefabDef prefab, List<CellCoord> cells, Vector2 lipPoint, float skew, bool mirror, string tag,
        Func<IEnumerable<string>, string> newId, List<PaintRegion> made)
    {
        var r = new PaintRegion { Id = newId(made.Select(x => x.Id)), Prefab = prefab.Id, Material = "", Cells = cells, Skew = skew, MirrorX = mirror, Auto = tag };
        var anchor = PrefabPlacement.Anchor(r, prefab); // where it would snap with no nudge
        r.NudgeX = MathF.Round(lipPoint.X - anchor.X, 1);
        r.NudgeY = MathF.Round(lipPoint.Y - anchor.Y, 1);
        return r;
    }
}
