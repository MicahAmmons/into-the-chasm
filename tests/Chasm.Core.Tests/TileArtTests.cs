using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Serialization;
using Chasm.Core.World;

namespace Chasm.Core.Tests;

public class TileArtTests
{
    [Fact]
    public void Tile_ids_come_from_floor_and_coordinates()
    {
        Assert.Equal("f4_3_m1", TileDef.IdFor(4, 3, -1));
        Assert.Equal("f1_m2_0", TileDef.IdFor(1, -2, 0));
    }

    [Fact]
    public void Cells_serialize_compactly_and_read_old_formats()
    {
        var region = new PaintRegion { Id = "r1", Material = "cliff", Cells = { new(12, 3), new(13, -4) } };
        string json = ChasmJson.Serialize(region);
        Assert.Contains("\"12,3 13,-4\"", json);
        Assert.Equal(region.Cells, ChasmJson.Deserialize<PaintRegion>(json)!.Cells);
        var old = ChasmJson.Deserialize<TileDef>("{\"Id\":\"t\",\"Spawn\":{\"Q\":5,\"R\":-2},\"PlayerCombatStarts\":[{\"Q\":1,\"R\":2}]}")!;
        Assert.Equal(new CellCoord(5, -2), old.Spawn);
        Assert.Equal(new CellCoord(1, 2), old.PlayerCombatStarts[0]);
    }

    [Fact]
    public void Cloud_fade_lower_and_upper()
    {
        var lower = new CloudLayerDef { Line = 0.5f, FadeLength = 0.2f };
        Assert.Equal(1f, lower.FadeAt(0.3f, lower: true));
        Assert.Equal(0.5f, lower.FadeAt(0.6f, lower: true), 3);
        Assert.Equal(0f, lower.FadeAt(0.8f, lower: true));
        var upper = new CloudLayerDef { Line = 0.2f, FadeLength = 0.1f };
        Assert.Equal(1f, upper.FadeAt(0.5f, lower: false));
        Assert.Equal(0.5f, upper.FadeAt(0.15f, lower: false), 3);
        Assert.Equal(0f, upper.FadeAt(0.05f, lower: false));
    }

    [Fact]
    public void Neighbours_link_automatically_and_manual_exits_win()
    {
        var defs = new DefRegistry();
        var a = new TileDef { Id = "a", Floor = 4, X = 0, Y = 0 };
        var east = new TileDef { Id = "east", Floor = 4, X = 1, Y = 0 };
        var south = new TileDef { Id = "south", Floor = 4, X = 0, Y = 1 };
        var other = new TileDef { Id = "otherfloor", Floor = 5, X = 0, Y = -1 };
        foreach (var t in new[] { a, east, south, other }) defs.Put(t);
        a.Exits.Add(new TileExitDef { Edge = Edge.South, TargetTile = "otherfloor" });
        var exits = TileLinks.ExitsFor(defs, a);
        Assert.Contains(exits, e => e.Edge == Edge.East && e.TargetTile == "east");
        Assert.Contains(exits, e => e.Edge == Edge.South && e.TargetTile == "otherfloor");
        Assert.DoesNotContain(exits, e => e.Edge == Edge.North);
    }

    [Fact]
    public void Materials_resolve_from_floor_or_local_and_duplicate_coordinates_are_reported()
    {
        var defs = new DefRegistry();
        defs.Put(new FloorDef { Id = "floor_4", Number = 4, Materials = { new MaterialDef { Id = "cliff", Name = "Cliff", Texture = "x.png" } } });
        var t = new TileDef { Id = "a", Floor = 4, X = 0, Y = 0, LocalMaterials = { new MaterialDef { Id = "moss", Name = "Moss", Texture = "m.png" } } };
        t.PaintRegions.Add(new PaintRegion { Id = "r1", Material = "cliff" });
        t.PaintRegions.Add(new PaintRegion { Id = "r2", Material = "local:moss" });
        t.PaintRegions.Add(new PaintRegion { Id = "r3", Material = "nope" });
        defs.Put(t);
        defs.Put(new TileDef { Id = "b", Floor = 4, X = 0, Y = 0 });
        var floor = TileLinks.FloorOf(defs, t);
        Assert.NotNull(t.FindMaterial("cliff", floor));
        Assert.NotNull(t.FindMaterial("local:moss", floor));
        var problems = t.Validate(defs).ToList();
        Assert.Contains(problems, p => p.Contains("missing material 'nope'"));
        Assert.Contains(problems, p => p.Contains("already sits at floor 4 (0,0)"));
    }
}

public class RegionMaskTests
{
    private static float At(float[] m, float canvasX, float canvasY) =>
        m[(int)(canvasY / 2) * RegionMask.Width + (int)(canvasX / 2)];

    [Fact]
    public void Inside_is_solid_outside_is_empty_and_feather_softens_the_edge()
    {
        var cell = new CellCoord(15, 0);
        var c = IsoGrid.CellCenter(cell);
        var hard = RegionMask.Build(new[] { cell }, 0, null);
        Assert.Equal(1f, At(hard, c.X, c.Y));
        Assert.Equal(0f, At(hard, c.X + 200, c.Y));
        var soft = RegionMask.Build(new[] { cell }, 16, null);
        float edge = At(soft, c.X + IsoGrid.HalfW - 2, c.Y);
        Assert.InRange(edge, 0.05f, 0.95f);
    }

    /// <summary>A vertical stack of cells: (q, r), (q+1, r+1), ... each 64 px lower on screen.</summary>
    private static List<CellCoord> Stack(int q, int r, int n) => Enumerable.Range(0, n).Select(i => new CellCoord(q + i, r + i)).ToList();

    [Fact]
    public void Fade_is_measured_from_the_regions_own_top_edge_not_the_screen()
    {
        var fade = new EdgeFade(true, 1.5f, 1.5f, FadeAngle.Level);
        foreach (var (q, r) in new[] { (3, 1), (10, 2) }) // same shape, different places on screen
        {
            var cells = Stack(q, r, 4);
            var m = RegionMask.Build(cells, 0, fade);
            var top = IsoGrid.CellCenter(cells[0]);
            float lip = top.Y - IsoGrid.HalfH;
            Assert.Equal(1f, At(m, top.X, lip + 64), 2);          // 1 cell down: still solid
            float mid = At(m, top.X, lip + 64 * 2.25f);           // halfway through the fade
            Assert.InRange(mid, 0.35f, 0.65f);
            Assert.Equal(0f, At(m, top.X, lip + 64 * 3.4f), 2);   // past start + length: gone
        }
    }

    [Fact]
    public void Fade_continues_across_cells_as_one_gradient()
    {
        var cells = Stack(5, 1, 4);
        var m = RegionMask.Build(cells, 0, new EdgeFade(true, 1f, 2f, FadeAngle.Level));
        var x = IsoGrid.CellCenter(cells[0]).X;
        float prev = 2;
        for (float y = IsoGrid.CellCenter(cells[0]).Y; y < IsoGrid.CellCenter(cells[3]).Y + 30; y += 8)
        {
            float v = At(m, x, y);
            Assert.True(v <= prev + 0.02f, $"fade went back up at y={y}");
            prev = v;
        }
    }

    [Fact]
    public void Diagonal_cliff_top_is_straightened_along_the_cell_edge()
    {
        // A row of cells along +q runs down-right at the cell-edge angle; its top is a saw-tooth.
        var row = Enumerable.Range(2, 8).Select(q => new CellCoord(q, 3)).ToList();
        var inside = RegionMask.Inside(row);
        var edge = RegionMask.Edge(inside, new EdgeFade(true, 1, 1, FadeAngle.Auto));
        // Straightened: the edge follows a single line of slope 0.5 (no zig-zag) across the middle.
        int x0 = (int)(IsoGrid.CellCenter(row[2]).X / 2), x1 = (int)(IsoGrid.CellCenter(row[5]).X / 2);
        for (int x = x0; x <= x1; x++)
            Assert.Equal(edge[x0] + 0.5f * (x - x0), edge[x], 1);
    }

    [Fact]
    public void Upper_fade_is_measured_up_from_the_bottom_edge()
    {
        var cells = Stack(4, 2, 4);
        var m = RegionMask.Build(cells, 0, new EdgeFade(false, 1f, 1f, FadeAngle.Level));
        var bottom = IsoGrid.CellCenter(cells[^1]);
        Assert.Equal(1f, At(m, bottom.X, bottom.Y), 2);
        Assert.Equal(0f, At(m, bottom.X, bottom.Y - 64 * 2.4f), 2);
    }
}
