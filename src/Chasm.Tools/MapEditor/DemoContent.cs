using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.World;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// Builds the clean test content (--make-demo): floor 2 with a placeholder rock wall as its
/// automatic cliff, and tile f2_0_0 with a walkway (plateau, paths, a notch, a step) to show it off.
/// </summary>
public static class DemoContent
{
    public static IEnumerable<string> Build(DefRegistry reg)
    {
        var wall = new WallDef
        {
            Id = "placeholder_rock", Name = "Placeholder rock", Category = "Cliffs", Kind = MaterialKind.WallDown,
            Texture = "placeholder/textures/rock_strata.png", Scale = 0.8f, LipLine = 0f, Depth = 4, MaxRun = 12,
            Notes = "Starter wall from the placeholder art. Swap the texture for real cliff art (one left-facing strip).",
        };
        reg.Save(wall);
        yield return "  walls/placeholder_rock.json";

        var floor = reg.All<FloorDef>().FirstOrDefault(f => f.Number == 2) ?? new FloorDef { Id = "floor_2", Number = 2 };
        floor.Name = "Test floor";
        floor.EntryTile = "f2_0_0";
        floor.WallsDown = new() { new FloorWall { Wall = wall.Id } };
        floor.WallsUp = new();
        floor.WallDownFade = new WallFade { Start = 1f, Length = 1.5f, Angle = FadeAngle.Auto };
        floor.Materials = new()
        {
            new MaterialDef { Id = "grass", Name = "Grass", Texture = "placeholder/textures/grass.png", Kind = MaterialKind.General },
            new MaterialDef { Id = "dirt", Name = "Dirt path", Texture = "placeholder/textures/dirt.png", Kind = MaterialKind.General },
        };
        reg.Save(floor);
        yield return $"  floors/{floor.Id}.json";

        var tile = new TileDef { Id = "f2_0_0", Name = "Demo tile", Floor = 2, X = 0, Y = 0,
            Notes = "Demo of live walls: paint walkable cells and the cliffs follow." };
        foreach (var c in IsoGrid.AllCells) tile.SetWalkable(c, false);
        var walk = new HashSet<CellCoord>();
        void Blob(CellCoord centre, int r) { foreach (var c in IsoGrid.AllCells) if (Math.Abs(c.Q - centre.Q) + Math.Abs(c.R - centre.R) <= r) walk.Add(c); }
        Blob(new CellCoord(14, 0), 3);                                     // main plateau
        for (int q = 17; q <= 23; q++) walk.Add(new CellCoord(q, 0));      // path down-right
        for (int q = 17; q <= 23; q++) walk.Add(new CellCoord(q, -1));
        for (int r = 3; r <= 7; r++) walk.Add(new CellCoord(13, r));       // path down-left
        walk.Remove(new CellCoord(15, 2));                                 // a notch (inner corner)
        Blob(new CellCoord(24, -4), 1);                                    // a step at the end of the path
        foreach (var c in walk.Where(IsoGrid.InTile)) tile.SetWalkable(c, true);
        tile.Spawn = new CellCoord(14, 0);
        tile.CommitWalkable();
        var grass = walk.Where(IsoGrid.InTile).Where(c => !(c.R == 0 && c.Q >= 17) && !(c.Q == 13 && c.R >= 3)).ToList();
        var dirt = walk.Where(IsoGrid.InTile).Except(grass).ToList();
        tile.PaintRegions.Add(new PaintRegion { Id = "r1", Material = "grass", Cells = grass });
        tile.PaintRegions.Add(new PaintRegion { Id = "r2", Material = "dirt", Cells = dirt });
        reg.Save(tile);
        yield return "  tiles/f2_0_0.json";
    }
}
