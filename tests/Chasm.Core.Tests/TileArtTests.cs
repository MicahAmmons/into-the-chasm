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
        var hard = RegionMask.Build(new[] { cell }, 0, null, true);
        Assert.Equal(1f, At(hard, c.X, c.Y));
        Assert.Equal(0f, At(hard, c.X + 200, c.Y));
        var soft = RegionMask.Build(new[] { cell }, 16, null, true);
        float edge = At(soft, c.X + IsoGrid.HalfW - 2, c.Y);
        Assert.InRange(edge, 0.05f, 0.95f);
    }

    [Fact]
    public void Lower_cloud_fade_hides_cells_below_the_line()
    {
        var cells = IsoGrid.AllCells.ToList();
        var cloud = new CloudLayerDef { Line = 0.5f, FadeLength = 0.1f };
        var m = RegionMask.Build(cells, 0, cloud, lower: true);
        Assert.Equal(1f, At(m, 960, 300));      // above the line
        Assert.Equal(0f, At(m, 960, 800));      // well below line + fade
        var up = RegionMask.Build(cells, 0, new CloudLayerDef { Line = 0.5f, FadeLength = 0.1f }, lower: false);
        Assert.Equal(0f, At(up, 960, 300));     // walls fade going up
        Assert.Equal(1f, At(up, 960, 800));
    }
}
