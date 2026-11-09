using System.Text.Json.Serialization;

namespace Chasm.Core.Grid;

/// <summary>
/// A Cell's position inside a Tile, in isometric axes.
///
/// Q runs down-right on screen, R runs down-left. Stepping Q+1 or R+1 moves to an edge-sharing
/// neighbour (what combat movement uses), so combat distance is simply |dQ| + |dR|.
/// </summary>
public readonly record struct CellCoord(int Q, int R)
{
    /// <summary>The four edge-sharing neighbours (combat movement directions).</summary>
    public static readonly CellCoord[] Orthogonal = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>The four corner-sharing neighbours (straight up/down/left/right on screen).</summary>
    public static readonly CellCoord[] Diagonal = { new(1, 1), new(-1, -1), new(1, -1), new(-1, 1) };

    public static CellCoord operator +(CellCoord a, CellCoord b) => new(a.Q + b.Q, a.R + b.R);
    public static CellCoord operator -(CellCoord a, CellCoord b) => new(a.Q - b.Q, a.R - b.R);

    /// <summary>Steps needed in combat (4-directional movement).</summary>
    public int ManhattanTo(CellCoord o) => Math.Abs(Q - o.Q) + Math.Abs(R - o.R);

    [JsonIgnore]
    public IEnumerable<CellCoord> OrthogonalNeighbours
    {
        get { var self = this; return Orthogonal.Select(d => self + d); }
    }

    public override string ToString() => $"({Q},{R})";
}
