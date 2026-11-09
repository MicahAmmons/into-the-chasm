using System.Numerics;

namespace Chasm.Core.Grid;

/// <summary>
/// Exact "can I walk/see in a straight line from A to B" test. It visits every cell the segment
/// passes through (grid traversal in cell space), so no sampling gaps. When the segment passes
/// exactly through a cell corner, both cells beside the corner must be open (no squeezing
/// diagonally between two blocked cells).
/// </summary>
public static class LineOfSight
{
    public static bool Clear(Vector2 fromPixel, Vector2 toPixel, Func<CellCoord, bool> open)
    {
        foreach (var c in CellsAlong(fromPixel, toPixel))
            if (!open(c)) return false;
        return true;
    }

    /// <summary>All cells touched by the segment, in order (corner crossings include both side cells).</summary>
    public static IEnumerable<CellCoord> CellsAlong(Vector2 fromPixel, Vector2 toPixel)
    {
        // Shift so cell (q,r) spans [q, q+1) x [r, r+1).
        var a = IsoGrid.ToCellSpace(fromPixel) + new Vector2(0.5f);
        var b = IsoGrid.ToCellSpace(toPixel) + new Vector2(0.5f);
        int x = (int)MathF.Floor(a.X), y = (int)MathF.Floor(a.Y);
        int endX = (int)MathF.Floor(b.X), endY = (int)MathF.Floor(b.Y);
        yield return new CellCoord(x, y);

        var d = b - a;
        int stepX = Math.Sign(d.X), stepY = Math.Sign(d.Y);
        float tDeltaX = stepX != 0 ? MathF.Abs(1f / d.X) : float.PositiveInfinity;
        float tDeltaY = stepY != 0 ? MathF.Abs(1f / d.Y) : float.PositiveInfinity;
        float tMaxX = stepX > 0 ? (x + 1 - a.X) * tDeltaX : stepX < 0 ? (a.X - x) * tDeltaX : float.PositiveInfinity;
        float tMaxY = stepY > 0 ? (y + 1 - a.Y) * tDeltaY : stepY < 0 ? (a.Y - y) * tDeltaY : float.PositiveInfinity;

        const float eps = 1e-5f;
        int guard = 0;
        while ((x != endX || y != endY) && guard++ < 512)
        {
            if (MathF.Abs(tMaxX - tMaxY) < eps)
            {
                // Exactly through a corner: report both side cells, then the diagonal one.
                if (tMaxX > 1f) break;
                yield return new CellCoord(x + stepX, y);
                yield return new CellCoord(x, y + stepY);
                x += stepX; y += stepY;
                tMaxX += tDeltaX; tMaxY += tDeltaY;
            }
            else if (tMaxX < tMaxY)
            {
                if (tMaxX > 1f) break;
                x += stepX; tMaxX += tDeltaX;
            }
            else
            {
                if (tMaxY > 1f) break;
                y += stepY; tMaxY += tDeltaY;
            }
            yield return new CellCoord(x, y);
        }
    }
}
