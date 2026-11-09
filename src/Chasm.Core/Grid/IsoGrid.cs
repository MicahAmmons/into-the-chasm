using System.Numerics;

namespace Chasm.Core.Grid;

/// <summary>
/// Converts between canvas pixels and Cells for a Tile. Every conversion is plain arithmetic
/// (no searching), so it's cheap enough to call anywhere.
///
/// Cells are 128x64 diamonds (matching the original art). Cell (0,0) is centred on canvas (0,0);
/// the screen centre of (Q,R) is ((Q-R)*64, (Q+R)*32).
///
/// A Tile contains every cell whose centre lies inside the 1920x1080 canvas; edge cells are cut in
/// half by the screen border, exactly like the original game.
/// </summary>
public static class IsoGrid
{
    public const int CellWidth = 128;
    public const int CellHeight = 64;
    public const float HalfW = CellWidth / 2f;
    public const float HalfH = CellHeight / 2f;

    public static Vector2 CellCenter(CellCoord c) => new((c.Q - c.R) * HalfW, (c.Q + c.R) * HalfH);

    /// <summary>Continuous cell-space position: cell (q,r) spans q±0.5, r±0.5.</summary>
    public static Vector2 ToCellSpace(Vector2 p)
    {
        float a = p.X / HalfW, b = p.Y / HalfH;   // a = Q-R, b = Q+R
        return new Vector2((a + b) / 2f, (b - a) / 2f);
    }

    public static Vector2 FromCellSpace(Vector2 qr) => new((qr.X - qr.Y) * HalfW, (qr.X + qr.Y) * HalfH);

    /// <summary>The cell containing a canvas point (may be outside the Tile; check <see cref="InTile"/>).</summary>
    public static CellCoord CellAt(Vector2 p)
    {
        var qr = ToCellSpace(p);
        return new CellCoord((int)MathF.Floor(qr.X + 0.5f), (int)MathF.Floor(qr.Y + 0.5f));
    }

    public static bool InTile(CellCoord c)
    {
        var p = CellCenter(c);
        return p.X >= 0 && p.X <= GameInfo.CanvasWidth && p.Y >= 0 && p.Y <= GameInfo.CanvasHeight;
    }

    /// <summary>Every cell in a Tile, top to bottom, left to right.</summary>
    public static IReadOnlyList<CellCoord> AllCells { get; } = BuildAll();

    // Bounds of Q and R that can appear in a Tile (for dense array storage).
    public static int MinQ { get; private set; }
    public static int MaxQ { get; private set; }
    public static int MinR { get; private set; }
    public static int MaxR { get; private set; }

    private static List<CellCoord> BuildAll()
    {
        var list = new List<CellCoord>();
        int maxA = GameInfo.CanvasWidth / (int)HalfW;   // Q-R range 0..30
        int maxB = GameInfo.CanvasHeight / (int)HalfH;  // Q+R range 0..33
        for (int b = 0; b <= maxB; b++)
            for (int a = 0; a <= maxA; a++)
            {
                if (((a + b) & 1) != 0) continue;       // only coordinates that land on a cell centre
                var c = new CellCoord((a + b) / 2, (b - a) / 2);
                if (InTile(c)) list.Add(c);
            }
        MinQ = list.Min(c => c.Q); MaxQ = list.Max(c => c.Q);
        MinR = list.Min(c => c.R); MaxR = list.Max(c => c.R);
        return list;
    }

    /// <summary>Which screen edge(s) a cell touches, used for Tile exits.</summary>
    public static Edge EdgesOf(CellCoord c)
    {
        var p = CellCenter(c);
        Edge e = Edge.None;
        if (p.X - HalfW < 0) e |= Edge.West;
        if (p.X + HalfW > GameInfo.CanvasWidth) e |= Edge.East;
        if (p.Y - HalfH < 0) e |= Edge.North;
        if (p.Y + HalfH > GameInfo.CanvasHeight) e |= Edge.South;
        return e;
    }
}

[Flags]
public enum Edge { None = 0, North = 1, East = 2, South = 4, West = 8 }

public static class EdgeExtensions
{
    public static Edge Opposite(this Edge e) => e switch
    {
        Edge.North => Edge.South, Edge.South => Edge.North,
        Edge.East => Edge.West, Edge.West => Edge.East, _ => Edge.None,
    };
}
