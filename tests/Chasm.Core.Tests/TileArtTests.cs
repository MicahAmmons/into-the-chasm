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
    public void Tiles_use_floor_clouds_unless_overridden()
    {
        var floor = new FloorDef { Id = "f", Number = 1 };
        floor.Clouds = new() { new CloudLayer { Id = "a", Texture = "a.png", Speed = 10 }, new CloudLayer { Id = "b", Texture = "b.png" } };
        var plain = new TileDef { Id = "t1" };
        Assert.Equal(new[] { "a", "b" }, CloudStack.Resolve(floor, plain).Select(c => c.Id));
        var custom = new TileDef { Id = "t2" };
        custom.CloudOverrides.Add(new TileCloudOverride { Layer = "a", Settings = new CloudLayer { Id = "a", Texture = "a.png", Speed = 99 } });
        custom.CloudOverrides.Add(new TileCloudOverride { Layer = "b", Settings = new CloudLayer { Id = "b", Texture = "b.png", Enabled = false } });
        var r = CloudStack.Resolve(floor, custom);
        Assert.Single(r);
        Assert.Equal(99, r[0].Speed);
    }

    [Fact]
    public void Old_two_band_cloud_floors_become_cloud_layers()
    {
        string json = """{ "Id": "old", "Number": 3, "LowerClouds": { "Enabled": true, "Texture": "x.png", "Speed": 33 }, "UpperClouds": { "Enabled": false } }""";
        var f = Chasm.Core.Serialization.ChasmJson.Deserialize<FloorDef>(json)!;
        Assert.Single(f.Clouds);
        Assert.Equal("x.png", f.Clouds[0].Texture);
        Assert.Equal(33, f.Clouds[0].Speed);
        Assert.DoesNotContain("LowerClouds", Chasm.Core.Serialization.ChasmJson.Serialize(f));
        Assert.Single(new FloorDef().Clouds); // new floors start with one default layer
        Assert.Empty(f.TopClouds);             // upper band was off
    }

    [Fact]
    public void Top_clouds_draw_after_bottom_clouds_and_walls_sort_by_kind()
    {
        var floor = new FloorDef { Id = "f" };
        floor.Clouds = new() { new CloudLayer { Id = "b1", Texture = "b.png" } };
        floor.TopClouds.Add(new CloudLayer { Id = "t1", Texture = "t.png" });
        Assert.Equal(new[] { "b1", "t1" }, CloudStack.Resolve(floor, new TileDef { Id = "x" }).Select(c => c.Id));
        Assert.Equal("cloud_1", CloudStack.NewId(floor));
        Assert.Equal(MaterialKind.WallDown, new MaterialDef { Fade = CloudFade.IntoLowerClouds }.EffectiveKind);
        Assert.Equal(MaterialKind.WallUp, new MaterialDef { Fade = CloudFade.IntoUpperClouds }.EffectiveKind);
        Assert.Equal(MaterialKind.General, new MaterialDef().EffectiveKind);
        Assert.Equal(MaterialKind.General, new MaterialDef { Kind = MaterialKind.General, Fade = CloudFade.IntoLowerClouds }.EffectiveKind);
    }

    [Fact]
    public void Cloud_patterns_move_on_top_of_the_scroll()
    {
        var c = new CloudLayer { Speed = 0, Direction = 0, PatternAmount = 50, PatternCycle = 4 };
        c.Pattern = CloudPattern.Wave;    // across the direction (0 deg = right, so it moves up/down)
        var q = c.MotionAt(1).Offset;     // quarter cycle: full swing
        Assert.Equal(0, q.X, 2);
        Assert.Equal(50, q.Y, 2);
        c.Pattern = CloudPattern.Swirl;   // circle of radius 50
        Assert.Equal(50, c.MotionAt(1.3).Offset.Length(), 2);
        c.Pattern = CloudPattern.Breathe; // swells up to +50%
        Assert.Equal(1.5f, c.MotionAt(1).ScaleMul, 3);
        c.Pattern = CloudPattern.Shimmer; // dims down to 50%
        Assert.Equal(0.5f, c.MotionAt(1).OpacityMul, 3);
        c.Pattern = CloudPattern.Ripple;
        Assert.NotEqual(c.RippleShiftAt(0, 0), c.RippleShiftAt(0, 80));
        c.Pattern = CloudPattern.Scroll;
        c.Speed = 10;
        Assert.Equal(new System.Numerics.Vector2(20, 0), c.MotionAt(2).Offset);
    }

    [Fact]
    public void Corner_pin_maps_square_corners_and_foreshortens()
    {
        var p0 = new System.Numerics.Vector2(0, 0); var p1 = new System.Numerics.Vector2(400, 0);
        var p2 = new System.Numerics.Vector2(300, 200); var p3 = new System.Numerics.Vector2(100, 200);
        var h = Homography.SquareToQuad(p0, p1, p2, p3);
        Assert.Equal(p0, h.Map(0, 0)); Assert.True((h.Map(1, 0) - p1).Length() < 0.01f);
        Assert.True((h.Map(1, 1) - p2).Length() < 0.01f); Assert.True((h.Map(0, 1) - p3).Length() < 0.01f);
        // Perspective: the middle of the texture lands past the middle of the shape (far half shrinks).
        Assert.True(h.Map(0.5f, 0.5f).Y > 100.5f);
    }

    [Fact]
    public void Material_sheet_corners_follow_offset_scale_rotation_and_warp()
    {
        var m = new MaterialDef { OffsetX = 100, OffsetY = -20, Scale = 0.5f };
        var c = MaterialSheet.Corners(m, 400, 200);
        Assert.Equal(new System.Numerics.Vector2(960, 470), c[0]);     // 1060-100, 520-50
        Assert.Equal(new System.Numerics.Vector2(1160, 570), c[2]);
        m.Warp = MaterialWarp.Taper(0.5f, 0, 200, 100);                 // bottom edge pinched by half
        c = MaterialSheet.Corners(m, 400, 200);
        Assert.Equal(1110, c[2].X, 2); Assert.Equal(1010, c[3].X, 2);
        m.Warp = null; m.Rotation = 90;
        Assert.True((MaterialSheet.Corners(m, 400, 200)[0] - new System.Numerics.Vector2(1110, 420)).Length() < 0.01f);
    }

    [Fact]
    public void Prefab_anchors_its_lip_to_the_top_of_its_cells_and_fades_by_floor()
    {
        var prefab = new PrefabDef { Id = "p", Kind = MaterialKind.WallDown, LipLine = 0.25f, Scale = 1 };
        var region = new PaintRegion { Id = "r1", Prefab = "p", Cells = new() { new(10, 2), new(11, 3) } };
        var a = PrefabPlacement.Anchor(region, prefab);
        Assert.Equal(IsoGrid.CellCenter(new(10, 2)).Y - IsoGrid.HalfH, a.Y, 2);         // top of the higher cell
        var c = PrefabPlacement.Corners(region, prefab, 400, 200);
        Assert.Equal(a.Y - 50, c[0].Y, 2);                                                 // lip 25% down a 200 px picture
        Assert.Equal(a.X - 200, c[0].X, 2);
        region.NudgeX = 30;
        Assert.Equal(a.X + 30, PrefabPlacement.Anchor(region, prefab).X, 2);
        var floor = new FloorDef { Id = "f" };
        floor.WallDownFade.Start = 2.5f;
        Assert.Equal(2.5f, PrefabPlacement.FadeFor(region, prefab, floor)!.Value.StartCells);   // floor default
        region.FadeStart = 0.5f;
        Assert.Equal(0.5f, PrefabPlacement.FadeFor(region, prefab, floor)!.Value.StartCells);   // placement override
        region.FadeEnabled = false;
        Assert.Null(PrefabPlacement.FadeFor(region, prefab, floor));
        prefab.Kind = MaterialKind.WallUp; region.FadeEnabled = null;
        Assert.False(PrefabPlacement.FadeFor(region, prefab, floor)!.Value.Lower);
    }

    private static TileDef WalkableTile(params CellCoord[] walk)
    {
        var t = new TileDef { Id = "auto" };
        foreach (var c in IsoGrid.AllCells) t.SetWalkable(c, false);
        foreach (var c in walk) t.SetWalkable(c, true);
        return t;
    }

    [Fact]
    public void Auto_cliffs_hang_faces_and_corners_under_open_edges()
    {
        // A short diagonal walkway: (10,2) (11,2) (12,2) runs down-right.
        var t = WalkableTile(new(10, 2), new(11, 2), new(12, 2));
        var face = new PrefabDef { Id = "face", Kind = MaterialKind.WallDown };
        var corner = new PrefabDef { Id = "corner", Kind = MaterialKind.WallDown };
        var kit = new CliffKit { LeftFace = "face", OuterCorner = "corner", Depth = 2, MaxRun = 10 };
        int n = 0;
        var pieces = AutoCliffs.Generate(t, kit, up: false, id => id == "face" ? face : id == "corner" ? corner : null, _ => "a" + n++);
        Assert.All(pieces, p => Assert.Equal(AutoCliffs.TagDown, p.Auto));
        // Left faces: one run of 3 cells along r = 2; their columns start at (q, 3).
        var leftFace = pieces.Single(p => p.Prefab == "face" && !p.MirrorX);
        Assert.Contains(new CellCoord(10, 3), leftFace.Cells);
        Assert.Contains(new CellCoord(11, 4), leftFace.Cells);   // straight below (10,3)
        Assert.Equal(0.5f, leftFace.Skew);
        // Right faces: only (12,2) is open down-right; no right prefab, so the left face mirrored.
        Assert.Single(pieces, p => p.Prefab == "face" && p.MirrorX);
        // Outer corner only under the end cell (12,2): the others have a walkway neighbour down-right.
        Assert.Single(pieces, p => p.Prefab == "corner");
        Assert.Contains(new CellCoord(13, 3), pieces.Single(p => p.Prefab == "corner").Cells);
        // No piece covers a walkable cell, and the lip lands on the open edge.
        Assert.All(pieces, p => Assert.DoesNotContain(p.Cells, c => t.IsWalkable(c)));
        var anchor = PrefabPlacement.Anchor(leftFace, face);
        var expected = (IsoGrid.CellCenter(new(11, 2)) + new System.Numerics.Vector2(-IsoGrid.HalfW / 2, IsoGrid.HalfH / 2));
        Assert.True((anchor - expected).Length() < 0.2f);
    }

    [Fact]
    public void Auto_cliffs_split_long_runs_and_need_a_left_face()
    {
        var t = WalkableTile(Enumerable.Range(4, 8).Select(q => new CellCoord(q, 3)).ToArray());
        var face = new PrefabDef { Id = "face" };
        int n = 0;
        var kit = new CliffKit { LeftFace = "face", MaxRun = 3, Depth = 1 };
        var pieces = AutoCliffs.Generate(t, kit, false, id => id == "face" ? face : null, _ => "a" + n++);
        Assert.Equal(3, pieces.Count(p => !p.MirrorX && p.Cells.All(c => c.R == 4))); // 8 cells -> 3 + 3 + 2
        Assert.Empty(AutoCliffs.Generate(t, new CliffKit(), false, _ => null, _ => "x"));
        // Without an outer corner prefab, the cell under the end point is still covered by the faces.
        var single = WalkableTile(new CellCoord(10, 2));
        var faces = AutoCliffs.Generate(single, new CliffKit { LeftFace = "face", Depth = 1 }, false, id => id == "face" ? face : null, _ => "b" + n++);
        Assert.Contains(faces, p => p.Cells.Contains(new CellCoord(11, 3)));
    }

    [Fact]
    public void Cloud_direction_sets_scroll_offset()
    {
        var c = new CloudLayer { Speed = 10, Direction = 90 };
        var o = c.OffsetAt(2);
        Assert.Equal(0, o.X, 3);
        Assert.Equal(20, o.Y, 3);
        c.Direction = 180;
        Assert.Equal(-20, c.OffsetAt(2).X, 3);
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
    public void Stencil_cuts_away_part_of_its_cell_only()
    {
        var cells = new List<CellCoord> { new(10, 2), new(11, 2) };
        // 2x2 stencil: top row kept, bottom row cut.
        var img = new StencilImage(2, 2, new[] { 1f, 1f, 0f, 0f });
        var stencils = new List<CellStencil> { new() { Cell = cells[0], Stencil = "s.png" } };
        var m = RegionMask.Build(cells, 0, null, stencils, _ => img);
        var c0 = IsoGrid.CellCenter(cells[0]);
        Assert.Equal(1f, At(m, c0.X, c0.Y - 20), 2);   // top of the stencilled cell kept
        Assert.Equal(0f, At(m, c0.X, c0.Y + 24), 2);   // bottom cut
        var c1 = IsoGrid.CellCenter(cells[1]);
        Assert.Equal(1f, At(m, c1.X, c1.Y + 24), 2);   // neighbour untouched
        // Flipped vertically: the other half is cut instead.
        stencils[0].FlipY = true;
        var f = RegionMask.Build(cells, 0, null, stencils, _ => img);
        Assert.Equal(0f, At(f, c0.X, c0.Y - 20), 2);
        Assert.Equal(1f, At(f, c0.X, c0.Y + 24), 2);
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
