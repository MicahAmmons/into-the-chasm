using Chasm.Core.Grid;

namespace Chasm.Core.World;

/// <summary>
/// The alpha mask for a paint region at half resolution (960x540): 1 inside the region's cells,
/// softened by the feather (two box-blur passes, close to a gaussian), times the edge fade.
/// Pure maths, so it's unit-tested; the game uploads it as a texture.
///
/// Edge fade (cliffs into the lower clouds): for every column the region's top edge is found,
/// then straightened along the fade angle (the iso cell-edge angle, so the diamond points don't
/// make a zig-zag). The fade is measured downward from that line: solid until FadeStart cells,
/// gone FadeLength cells later, as one gradient across all the region's cells. Upper clouds mirror
/// it: measured upward from the region's bottom edge.
/// </summary>
public static class RegionMask
{
    public const int Width = 960, Height = 540;
    private const float Slope = 0.5f;   // iso cell edges: 1 down per 2 across (same at half resolution)
    private const int Window = 32;      // straightening window each side, half-res px (one cell wide in total)

    public static float[] Build(IReadOnlyCollection<CellCoord> cells, int featherPx, EdgeFade? fade,
        IReadOnlyList<CellStencil>? stencils = null, Func<string, StencilImage?>? loadStencil = null)
    {
        var inside = Inside(cells);
        var a = (float[])inside.Clone();
        // Carve first, so the soft edge and fade apply to the carved shape. The fade itself is
        // still measured from the uncarved edge, so carving a lip doesn't move the fade.
        if (stencils is { Count: > 0 } && loadStencil is not null) Carve(a, cells, stencils, loadStencil);
        int r = Math.Max(0, featherPx / 2);
        if (r > 0)
        {
            int pass = Math.Max(1, (int)MathF.Ceiling(r / 1.7f));
            var tmp = new float[a.Length];
            for (int i = 0; i < 2; i++) { BoxBlur(a, tmp, pass, horizontal: true); BoxBlur(tmp, a, pass, horizontal: false); }
        }
        if (fade is { } f)
        {
            var edge = Edge(inside, f);
            float start = f.StartCells * EdgeFade.CellPx / 2, len = Math.Max(0.5f, f.LengthCells * EdgeFade.CellPx / 2);
            for (int x = 0; x < Width; x++)
            {
                if (float.IsNaN(edge[x])) continue;
                for (int y = 0; y < Height; y++)
                {
                    float depth = f.Lower ? (y + 0.5f) - edge[x] : edge[x] - (y + 0.5f);
                    if (depth <= start) continue;
                    a[y * Width + x] *= Math.Clamp(1f - (depth - start) / len, 0f, 1f);
                }
            }
        }
        return a;
    }

    /// <summary>Multiplies each stencilled cell's pixels by its stencil (stretched over the cell's box).</summary>
    public static void Carve(float[] a, IReadOnlyCollection<CellCoord> cells, IReadOnlyList<CellStencil> stencils, Func<string, StencilImage?> loadStencil)
    {
        var set = cells as HashSet<CellCoord> ?? cells.ToHashSet();
        foreach (var s in stencils)
        {
            if (!set.Contains(s.Cell) || loadStencil(s.Stencil) is not { } img) continue;
            var centre = IsoGrid.CellCenter(s.Cell);
            float left = centre.X - IsoGrid.HalfW, top = centre.Y - IsoGrid.HalfH;
            int x0 = Math.Max(0, (int)(left / 2) - 1), x1 = Math.Min(Width - 1, (int)((centre.X + IsoGrid.HalfW) / 2) + 1);
            int y0 = Math.Max(0, (int)(top / 2) - 1), y1 = Math.Min(Height - 1, (int)((centre.Y + IsoGrid.HalfH) / 2) + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new System.Numerics.Vector2(x * 2 + 1, y * 2 + 1);
                    if (IsoGrid.CellAt(p) != s.Cell) continue;
                    float u = (p.X - left) / IsoGrid.CellWidth, v = (p.Y - top) / IsoGrid.CellHeight;
                    if (s.FlipX) u = 1 - u;
                    if (s.FlipY) v = 1 - v;
                    a[y * Width + x] *= img.Sample(u, v);
                }
        }
    }

    /// <summary>1 where a pixel's centre is in one of the cells.</summary>
    public static float[] Inside(IReadOnlyCollection<CellCoord> cells)
    {
        var set = cells as HashSet<CellCoord> ?? cells.ToHashSet();
        var a = new float[Width * Height];
        foreach (var c in set)
        {
            var centre = IsoGrid.CellCenter(c);
            int x0 = Math.Max(0, (int)((centre.X - IsoGrid.HalfW) / 2) - 1), x1 = Math.Min(Width - 1, (int)((centre.X + IsoGrid.HalfW) / 2) + 1);
            int y0 = Math.Max(0, (int)((centre.Y - IsoGrid.HalfH) / 2) - 1), y1 = Math.Min(Height - 1, (int)((centre.Y + IsoGrid.HalfH) / 2) + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (IsoGrid.CellAt(new System.Numerics.Vector2(x * 2 + 1, y * 2 + 1)) == c) a[y * Width + x] = 1f;
        }
        return a;
    }

    /// <summary>
    /// The straightened edge the fade is measured from, per column (half-res y; NaN = no cells in
    /// that column). Lower: the top edge. Upper: the bottom edge.
    /// </summary>
    public static float[] Edge(float[] inside, EdgeFade fade)
    {
        var raw = new float[Width];
        for (int x = 0; x < Width; x++)
        {
            raw[x] = float.NaN;
            if (fade.Lower) { for (int y = 0; y < Height; y++) if (inside[y * Width + x] > 0) { raw[x] = y; break; } }
            else { for (int y = Height - 1; y >= 0; y--) if (inside[y * Width + x] > 0) { raw[x] = y + 1; break; } }
        }
        if (fade.Angle != FadeAngle.Auto) return Straighten(raw, SlopeOf(fade.Angle), fade.Lower, out _);
        // Auto: whichever cell-edge angle leaves the least zig-zag (ties go level).
        var right = Straighten(raw, Slope, fade.Lower, out float rightError);
        var left = Straighten(raw, -Slope, fade.Lower, out float leftError);
        var level = Straighten(raw, 0, fade.Lower, out float levelError);
        float best = Math.Min(rightError, leftError);
        if (levelError <= best * 1.05f) return level;
        return rightError <= leftError ? right : left;
    }

    public static float SlopeOf(FadeAngle angle) => angle switch
    {
        FadeAngle.DownRight => Slope,
        FadeAngle.DownLeft => -Slope,
        _ => 0f,
    };

    /// <summary>
    /// Lines the edge up with the given slope: in each neighbourhood the outermost point wins
    /// (highest for a top edge, lowest for a bottom edge), so a row of diamond points becomes
    /// one straight line along the cells' edge.
    /// </summary>
    private static float[] Straighten(float[] raw, float slope, bool top, out float error)
    {
        var k = new float[Width];
        for (int x = 0; x < Width; x++) k[x] = raw[x] - slope * x;
        var result = new float[Width];
        error = 0;
        for (int x = 0; x < Width; x++)
        {
            if (float.IsNaN(raw[x])) { result[x] = float.NaN; continue; }
            float best = k[x];
            for (int i = Math.Max(0, x - Window); i <= Math.Min(Width - 1, x + Window); i++)
            {
                if (float.IsNaN(k[i])) continue;
                best = top ? Math.Min(best, k[i]) : Math.Max(best, k[i]);
            }
            result[x] = best + slope * x;
            error += Math.Abs(k[x] - best);
        }
        return result;
    }

    private static void BoxBlur(float[] src, float[] dst, int r, bool horizontal)
    {
        int lines = horizontal ? Height : Width, len = horizontal ? Width : Height;
        float inv = 1f / (2 * r + 1);
        for (int l = 0; l < lines; l++)
        {
            int Idx(int i) => horizontal ? l * Width + i : i * Width + l;
            float sum = 0;
            for (int i = -r; i <= r; i++) sum += src[Idx(Math.Clamp(i, 0, len - 1))];
            for (int i = 0; i < len; i++)
            {
                dst[Idx(i)] = sum * inv;
                sum += src[Idx(Math.Min(len - 1, i + r + 1))] - src[Idx(Math.Max(0, i - r))];
            }
        }
    }
}
