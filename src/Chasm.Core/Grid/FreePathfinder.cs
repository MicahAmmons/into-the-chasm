using System.Numerics;

namespace Chasm.Core.Grid;

/// <summary>
/// Out-of-combat movement: "walk where I clicked by the shortest route".
///
/// How it works (Theta*, an any-angle relative of A*):
///  1. Search outward over open cells like A*, 8 directions (diagonals can't squeeze between
///     two blocked cells).
///  2. Whenever a cell can be seen in a straight line from its parent's parent, connect them
///     directly. Open ground collapses into one straight segment, and routes around walls
///     hug the corners, so there's no zig-zag.
///  3. If the clicked spot is blocked or unreachable, walk to the reachable cell nearest the click.
///
/// The result is a list of canvas-pixel waypoints, starting after the current position.
/// </summary>
public static class FreePathfinder
{
    public sealed record Result(List<Vector2> Waypoints, bool ReachedTarget, CellCoord EndCell);

    public static Result FindPath(Vector2 start, Vector2 target, Func<CellCoord, bool> walkable)
    {
        var startCell = IsoGrid.CellAt(start);
        var targetCell = IsoGrid.CellAt(target);

        // Never stuck: if we're standing somewhere invalid, treat the current cell as open.
        bool Open(CellCoord c) => c == startCell || (IsoGrid.InTile(c) && walkable(c));

        bool reached = Open(targetCell) && targetCell != startCell
            ? Search(start, startCell, targetCell, Open, out var cells)
            : TryTrivial(startCell, targetCell, out cells);

        Vector2 endPoint = target;
        CellCoord endCell = targetCell;
        if (!reached)
        {
            endCell = NearestReachable(startCell, target, Open);
            endPoint = IsoGrid.CellCenter(endCell);
            if (endCell == startCell) return new Result(new List<Vector2>(), false, startCell);
            Search(start, startCell, endCell, Open, out cells);
        }

        // Turn cell chain into pixel waypoints: start at actual position, end at the exact click.
        var points = new List<Vector2> { start };
        foreach (var c in cells.Skip(1)) points.Add(IsoGrid.CellCenter(c));
        if (points.Count == 1) points.Add(endPoint);
        else points[^1] = endPoint;
        Smooth(points, Open);
        points.RemoveAt(0);
        return new Result(points, reached, endCell);
    }

    private static bool TryTrivial(CellCoord startCell, CellCoord targetCell, out List<CellCoord> cells)
    {
        cells = new List<CellCoord> { startCell };
        return startCell == targetCell;
    }

    /// <summary>Removes waypoints that can be skipped in a straight line (final string-pull pass).</summary>
    private static void Smooth(List<Vector2> pts, Func<CellCoord, bool> open)
    {
        int i = 0;
        while (i < pts.Count - 2)
        {
            if (LineOfSight.Clear(pts[i], pts[i + 2], open)) pts.RemoveAt(i + 1);
            else i++;
        }
    }

    private static readonly CellCoord[] Dirs8 =
        CellCoord.Orthogonal.Concat(CellCoord.Diagonal).ToArray();

    private static IEnumerable<CellCoord> Neighbours(CellCoord c, Func<CellCoord, bool> open)
    {
        foreach (var d in Dirs8)
        {
            var n = c + d;
            if (!open(n)) continue;
            if (d.Q != 0 && d.R != 0 && (!open(new CellCoord(c.Q + d.Q, c.R)) || !open(new CellCoord(c.Q, c.R + d.R))))
                continue; // no corner cutting
            yield return n;
        }
    }

    private static bool Search(Vector2 startPx, CellCoord start, CellCoord goal, Func<CellCoord, bool> open,
        out List<CellCoord> cells)
    {
        var parent = new Dictionary<CellCoord, CellCoord>();
        var g = new Dictionary<CellCoord, float> { [start] = 0 };
        var closed = new HashSet<CellCoord>();
        var queue = new PriorityQueue<CellCoord, float>();
        Vector2 Pos(CellCoord c) => c == start ? startPx : IsoGrid.CellCenter(c);
        float H(CellCoord c) => Vector2.Distance(Pos(c), IsoGrid.CellCenter(goal));

        parent[start] = start;
        queue.Enqueue(start, H(start));
        while (queue.TryDequeue(out var s, out _))
        {
            if (s == goal)
            {
                cells = new List<CellCoord> { s };
                while (parent[s] != s) { s = parent[s]; cells.Add(s); }
                cells.Reverse();
                return true;
            }
            if (!closed.Add(s)) continue;

            foreach (var n in Neighbours(s, open))
            {
                if (closed.Contains(n)) continue;
                var p = parent[s];
                // Theta*: try connecting straight from the grandparent.
                CellCoord from = LineOfSight.Clear(Pos(p), Pos(n), open) ? p : s;
                float cost = g[from] + Vector2.Distance(Pos(from), Pos(n));
                if (!g.TryGetValue(n, out float old) || cost < old - 0.001f)
                {
                    g[n] = cost;
                    parent[n] = from;
                    queue.Enqueue(n, cost + H(n));
                }
            }
        }
        cells = new List<CellCoord> { start };
        return false;
    }

    /// <summary>Flood fill from the start; pick the reachable cell closest to the target point.</summary>
    public static CellCoord NearestReachable(CellCoord start, Vector2 target, Func<CellCoord, bool> open)
    {
        var seen = new HashSet<CellCoord> { start };
        var queue = new Queue<CellCoord>();
        queue.Enqueue(start);
        CellCoord best = start;
        float bestDist = Vector2.DistanceSquared(IsoGrid.CellCenter(start), target);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            float d = Vector2.DistanceSquared(IsoGrid.CellCenter(c), target);
            if (d < bestDist - 0.01f) { best = c; bestDist = d; }
            foreach (var n in Neighbours(c, open))
                if (seen.Add(n)) queue.Enqueue(n);
        }
        return best;
    }
}
