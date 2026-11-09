namespace Chasm.Core.Grid;

/// <summary>
/// Combat movement and range: 4-directional steps, one MP per step, occupied cells block.
/// </summary>
public static class TacticalGrid
{
    /// <summary>Every cell reachable within <paramref name="maxSteps"/>, with the step count to reach it.</summary>
    public static Dictionary<CellCoord, int> Reachable(CellCoord start, int maxSteps, Func<CellCoord, bool> passable)
    {
        var dist = new Dictionary<CellCoord, int> { [start] = 0 };
        var queue = new Queue<CellCoord>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            int d = dist[c];
            if (d >= maxSteps) continue;
            foreach (var n in c.OrthogonalNeighbours)
            {
                if (dist.ContainsKey(n) || !IsoGrid.InTile(n) || !passable(n)) continue;
                dist[n] = d + 1;
                queue.Enqueue(n);
            }
        }
        return dist;
    }

    /// <summary>Shortest 4-directional path (A*), excluding the start cell. Null if none.
    /// The goal itself may be impassable when <paramref name="allowBlockedGoal"/> (e.g. walking up to an enemy).</summary>
    public static List<CellCoord>? Path(CellCoord start, CellCoord goal, Func<CellCoord, bool> passable, bool allowBlockedGoal = false)
    {
        if (start == goal) return new List<CellCoord>();
        var parent = new Dictionary<CellCoord, CellCoord>();
        var g = new Dictionary<CellCoord, int> { [start] = 0 };
        var queue = new PriorityQueue<CellCoord, int>();
        queue.Enqueue(start, start.ManhattanTo(goal));
        while (queue.TryDequeue(out var c, out _))
        {
            if (c == goal)
            {
                var path = new List<CellCoord>();
                while (c != start) { path.Add(c); c = parent[c]; }
                path.Reverse();
                return path;
            }
            foreach (var n in c.OrthogonalNeighbours)
            {
                if (!IsoGrid.InTile(n)) continue;
                if (!passable(n) && !(allowBlockedGoal && n == goal)) continue;
                int cost = g[c] + 1;
                if (g.TryGetValue(n, out int old) && cost >= old) continue;
                g[n] = cost;
                parent[n] = c;
                queue.Enqueue(n, cost + n.ManhattanTo(goal));
            }
        }
        return null;
    }

    /// <summary>Cells whose combat distance from <paramref name="origin"/> is within [min, max].</summary>
    public static IEnumerable<CellCoord> InRange(CellCoord origin, int min, int max) =>
        IsoGrid.AllCells.Where(c => { int d = c.ManhattanTo(origin); return d >= min && d <= max; });
}
