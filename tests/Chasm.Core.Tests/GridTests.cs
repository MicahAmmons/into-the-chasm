using System.Numerics;
using Chasm.Core.Grid;

namespace Chasm.Core.Tests;

public class GridTests
{
    [Fact]
    public void Cell_centres_round_trip_through_pixels()
    {
        foreach (var c in IsoGrid.AllCells)
        {
            Assert.Equal(c, IsoGrid.CellAt(IsoGrid.CellCenter(c)));
            // A point just inside each diamond corner still maps to the same cell.
            var p = IsoGrid.CellCenter(c);
            Assert.Equal(c, IsoGrid.CellAt(p + new Vector2(IsoGrid.HalfW - 4, 0)));
            Assert.Equal(c, IsoGrid.CellAt(p + new Vector2(0, IsoGrid.HalfH - 3)));
        }
    }

    [Fact]
    public void Tile_matches_original_layout()
    {
        // Original game: doubled coords x 0..30, y 0..33/34 with x%2==y%2 inside 1920x1080.
        Assert.Contains(new CellCoord(0, 0), IsoGrid.AllCells);
        Assert.All(IsoGrid.AllCells, c => Assert.True(IsoGrid.InTile(c)));
        Assert.InRange(IsoGrid.AllCells.Count, 500, 560);
    }

    [Fact]
    public void Orthogonal_neighbours_are_diagonal_on_screen()
    {
        var a = IsoGrid.CellCenter(new CellCoord(5, 0));
        var b = IsoGrid.CellCenter(new CellCoord(6, 0));
        Assert.Equal(new Vector2(64, 32), b - a);
    }

    private static Func<CellCoord, bool> OpenExcept(params CellCoord[] walls)
    {
        var set = walls.ToHashSet();
        return c => !set.Contains(c);
    }

    [Fact]
    public void Free_path_across_open_ground_is_one_straight_segment()
    {
        var start = IsoGrid.CellCenter(new CellCoord(10, 0));
        var end = IsoGrid.CellCenter(new CellCoord(20, 5)) + new Vector2(5, 3);
        var r = FreePathfinder.FindPath(start, end, _ => true);
        Assert.True(r.ReachedTarget);
        Assert.Single(r.Waypoints);
        Assert.Equal(end, r.Waypoints[0]);
    }

    [Fact]
    public void Free_path_goes_around_a_wall()
    {
        // Wall of cells with Q=15 for R in -3..8 blocks a straight walk along Q.
        var walls = Enumerable.Range(-3, 12).Select(r => new CellCoord(15, r)).ToArray();
        var open = OpenExcept(walls);
        var start = IsoGrid.CellCenter(new CellCoord(12, 2));
        var end = IsoGrid.CellCenter(new CellCoord(18, 2));
        var r = FreePathfinder.FindPath(start, end, open);
        Assert.True(r.ReachedTarget);
        Assert.True(r.Waypoints.Count >= 2);
        var prev = start;
        foreach (var w in r.Waypoints) { Assert.True(LineOfSight.Clear(prev, w, open)); prev = w; }
    }

    [Fact]
    public void Clicking_a_blocked_cell_walks_to_the_nearest_reachable_cell()
    {
        var target = new CellCoord(15, 2);
        var open = OpenExcept(target);
        var start = IsoGrid.CellCenter(new CellCoord(10, 2));
        var r = FreePathfinder.FindPath(start, IsoGrid.CellCenter(target), open);
        Assert.False(r.ReachedTarget);
        Assert.NotEqual(target, r.EndCell);
        Assert.True(Math.Max(Math.Abs(r.EndCell.Q - target.Q), Math.Abs(r.EndCell.R - target.R)) == 1,
            "should stop right next to the blocked cell");
    }

    [Fact]
    public void Enclosed_target_falls_back_to_closest_point_on_our_side()
    {
        // Ring of walls around (20,5).
        var ring = new List<CellCoord>();
        for (int dq = -2; dq <= 2; dq++) for (int dr = -2; dr <= 2; dr++)
            if (Math.Max(Math.Abs(dq), Math.Abs(dr)) == 2) ring.Add(new CellCoord(20 + dq, 5 + dr));
        var open = OpenExcept(ring.ToArray());
        var r = FreePathfinder.FindPath(IsoGrid.CellCenter(new CellCoord(8, 3)), IsoGrid.CellCenter(new CellCoord(20, 5)), open);
        Assert.False(r.ReachedTarget);
        Assert.Equal(3, Math.Max(Math.Abs(r.EndCell.Q - 20), Math.Abs(r.EndCell.R - 5)));
    }

    [Fact]
    public void Tactical_reachable_respects_mp_and_blockers()
    {
        var start = new CellCoord(10, 2);
        var reach = TacticalGrid.Reachable(start, 4, OpenExcept(new CellCoord(11, 2)));
        Assert.All(reach.Values, d => Assert.InRange(d, 0, 4));
        Assert.False(reach.ContainsKey(new CellCoord(11, 2)));
        Assert.Equal(4, reach[new CellCoord(12, 2)]); // 2 straight, +2 to step around the blocker
        Assert.False(TacticalGrid.Reachable(start, 3, OpenExcept(new CellCoord(11, 2))).ContainsKey(new CellCoord(12, 2)));
    }

    [Fact]
    public void Tactical_path_can_end_on_blocked_goal_when_allowed()
    {
        var goal = new CellCoord(14, 2);
        var path = TacticalGrid.Path(new CellCoord(10, 2), goal, OpenExcept(goal), allowBlockedGoal: true);
        Assert.NotNull(path);
        Assert.Equal(4, path!.Count);
    }
}
