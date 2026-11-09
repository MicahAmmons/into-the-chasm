using Chasm.Core.Content;
using Chasm.Core.Grid;

namespace Chasm.Core.World;

/// <summary>
/// How tiles connect. Tiles with a floor and X/Y link to the tile next to them automatically
/// (east = X+1, west = X-1, north = Y-1, south = Y+1). A manual exit on the same edge wins.
/// </summary>
public static class TileLinks
{
    public static (int dx, int dy) Offset(Edge e) => e switch
    {
        Edge.East => (1, 0), Edge.West => (-1, 0), Edge.North => (0, -1), Edge.South => (0, 1), _ => (0, 0),
    };

    public static TileDef? At(DefRegistry defs, int floor, int x, int y) =>
        defs.All<TileDef>().FirstOrDefault(t => t.Floor == floor && t.X == x && t.Y == y);

    public static TileDef? Neighbour(DefRegistry defs, TileDef tile, Edge edge)
    {
        if (!tile.OnMap) return null;
        var (dx, dy) = Offset(edge);
        return At(defs, tile.Floor!.Value, tile.X!.Value + dx, tile.Y!.Value + dy);
    }

    /// <summary>All exits that work for this tile: manual ones, plus automatic neighbour links.</summary>
    public static List<TileExitDef> ExitsFor(DefRegistry defs, TileDef tile)
    {
        var list = new List<TileExitDef>(tile.Exits);
        foreach (var edge in new[] { Edge.North, Edge.East, Edge.South, Edge.West })
        {
            if (list.Any(e => e.Edge == edge)) continue;
            if (Neighbour(defs, tile, edge) is { } n)
                list.Add(new TileExitDef { Edge = edge, TargetTile = n.Id });
        }
        return list;
    }

    public static FloorDef? FloorOf(DefRegistry defs, TileDef tile) =>
        tile.Floor is int f ? defs.All<FloorDef>().FirstOrDefault(x => x.Number == f) : null;
}
