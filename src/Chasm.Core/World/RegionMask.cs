using Chasm.Core.Grid;

namespace Chasm.Core.World;

/// <summary>A rectangle of the half-resolution mask grid (960x540) that a mask is worked out in.</summary>
public readonly record struct MaskArea(int X, int Y, int W, int H)
{
    public static readonly MaskArea Full = new(0, 0, RegionMask.Width, RegionMask.Height);
    public int Length => W * H;
    public bool IsEmpty => W <= 0 || H <= 0;
}

/// <summary>
/// The alpha mask for a paint region / wall piece at half resolution (960x540): 1 inside the cells,
/// softened by the feather (two box-blur passes, close to a gaussian), cut by clip lines (corner
/// seams, trims, end caps), times the edge fade. Pure maths, so it's unit-tested.
///
/// Speed: masks are worked out only in the rectangle around their cells (plus room for the soft
/// edge) - usually a small fraction of the screen - and the game uploads just that rectangle.
///
/// Edge fade (cliffs into the lower clouds): for every column the region's top edge is found, then
/// straightened along the fade angle (the iso cell-edge angle, so the diamond points don't make a
/// zig-zag). The fade is measured downward from that line: solid until FadeStart cells, gone
/// FadeLength cells later, as one gradient across all the region's cells. Upper clouds mirror it.
/// </summary>
public static class RegionMask
{
    public const int Width = 960, Height = 540;
    private const float Slope = 0.5f;   // iso cell edges: 1 down per 2 across (same at half resolution)
    private const int Window = 32;      // straightening window each side, half-res px (one cell wide in total)

    // ------------------------------------------------------------------ full-screen helpers (tests, overlays)

    public static float[] Build(IReadOnlyCollection<CellCoord> cells, int featherPx, EdgeFade? fade,
        IReadOnlyList<CellStencil>? stencils = null, Func<string, StencilImage?>? loadStencil = null)
    {
        var (inside, a) = BuildBase(cells, featherPx, stencils, loadStencil);
        if (fade is { } f) ApplyFade(a, Edge(inside, f), f);
        return a;
    }

    /// <summary>The fade-independent part of a mask, as full-screen arrays (built in a small area, then placed).</summary>
    public static (float[] inside, float[] shape) BuildBase(IReadOnlyCollection<CellCoord> cells, int featherPx,
        IReadOnlyList<CellStencil>? stencils = null, Func<string, StencilImage?>? loadStencil = null, IReadOnlyList<ClipZone>? clips = null)
    {
        var area = AreaFor(cells, featherPx);
        var (inside, shape) = BuildBaseIn(area, cells, featherPx, stencils, loadStencil, clips);
        return (Expand(inside, area), Expand(shape, area));
    }

    public static float[] Inside(IReadOnlyCollection<CellCoord> cells) => InsideIn(MaskArea.Full, cells);
    public static float[] Edge(float[] inside, EdgeFade fade) => EdgeIn(MaskArea.Full, inside, fade);
    public static void ApplyFade(float[] a, float[] edge, EdgeFade f) => ApplyFadeIn(MaskArea.Full, a, edge, f);
    public static void ApplyClips(float[] a, IReadOnlyList<ClipZone> clips) => ApplyClipsIn(MaskArea.Full, a, clips);

    /// <summary>Copies an area's values into a full-screen array (zeros elsewhere).</summary>
    public static float[] Expand(float[] data, MaskArea area)
    {
        var full = new float[Width * Height];
        for (int y = 0; y < area.H; y++) Array.Copy(data, y * area.W, full, (area.Y + y) * Width + area.X, area.W);
        return full;
    }

    // ------------------------------------------------------------------ area versions (what the game uses)

    /// <summary>The rectangle around the cells, with room for the soft edge to spread.</summary>
    public static MaskArea AreaFor(IReadOnlyCollection<CellCoord> cells, int featherPx)
    {
        if (cells.Count == 0) return new MaskArea(0, 0, 0, 0);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in cells)
        {
            var p = IsoGrid.CellCenter(c);
            minX = Math.Min(minX, p.X - IsoGrid.HalfW); maxX = Math.Max(maxX, p.X + IsoGrid.HalfW);
            minY = Math.Min(minY, p.Y - IsoGrid.HalfH); maxY = Math.Max(maxY, p.Y + IsoGrid.HalfH);
        }
        int r = Math.Max(0, featherPx / 2);
        int pass = r > 0 ? Math.Max(1, (int)MathF.Ceiling(r / 1.7f)) : 0;
        int margin = 2 * pass + 2;
        int x0 = Math.Max(0, (int)(minX / 2) - margin), y0 = Math.Max(0, (int)(minY / 2) - margin);
        int x1 = Math.Min(Width, (int)MathF.Ceiling(maxX / 2) + margin), y1 = Math.Min(Height, (int)MathF.Ceiling(maxY / 2) + margin);
        return x1 <= x0 || y1 <= y0 ? new MaskArea(0, 0, 0, 0) : new MaskArea(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// The part of a mask that doesn't depend on the fade, in an area: which pixels are in the cells
    /// (for the edge), and the carved + softened + clipped shape. Cached by the game so fade
    /// sliders only redo the cheap last step.
    /// </summary>
    public static (float[] inside, float[] shape) BuildBaseIn(MaskArea area, IReadOnlyCollection<CellCoord> cells, int featherPx,
        IReadOnlyList<CellStencil>? stencils = null, Func<string, StencilImage?>? loadStencil = null, IReadOnlyList<ClipZone>? clips = null)
    {
        var inside = InsideIn(area, cells);
        var a = (float[])inside.Clone();
        // Carve first, so the soft edge and fade apply to the carved shape. The fade itself is
        // still measured from the uncarved edge, so carving a lip doesn't move the fade.
        if (stencils is { Count: > 0 } && loadStencil is not null) CarveIn(area, a, cells, stencils, loadStencil);
        int r = Math.Max(0, featherPx / 2);
        if (r > 0)
        {
            int pass = Math.Max(1, (int)MathF.Ceiling(r / 1.7f));
            var tmp = new float[a.Length];
            for (int i = 0; i < 2; i++) { BoxBlur(area, a, tmp, pass, horizontal: true); BoxBlur(area, tmp, a, pass, horizontal: false); }
        }
        // Corner seams / trims / end caps: after the blur, so pieces sharing cells stay exactly complementary.
        if (clips is { Count: > 0 }) ApplyClipsIn(area, a, clips);
        return (inside, a);
    }

    /// <summary>Multiplies the mask by each clip's left/right factor (inside its cells, or everywhere).</summary>
    public static void ApplyClipsIn(MaskArea area, float[] a, IReadOnlyList<ClipZone> clips)
    {
        foreach (var clip in clips)
        {
            if (clip.Cells is null)
            {
                if (clip.Lean == 0)
                {
                    // Straight line: one factor per pixel column.
                    for (int x = 0; x < area.W; x++)
                    {
                        float f = clip.Factor((area.X + x) * 2 + 1);
                        if (f >= 1f) continue;
                        for (int y = 0; y < area.H; y++) a[y * area.W + x] *= f;
                    }
                }
                else
                    for (int y = 0; y < area.H; y++)
                        for (int x = 0; x < area.W; x++)
                            a[y * area.W + x] *= clip.Factor((area.X + x) * 2 + 1, (area.Y + y) * 2 + 1);
                continue;
            }
            foreach (var c in clip.Cells)
                ForEachPixelOf(area, c, (i, px, py) => a[i] *= clip.Factor(px, py));
        }
    }

    /// <summary>Multiplies a mask by the edge fade, measured from <paramref name="edge"/> (see <see cref="EdgeIn"/>).</summary>
    public static void ApplyFadeIn(MaskArea area, float[] a, float[] edge, EdgeFade f)
    {
        float start = f.StartCells * EdgeFade.CellPx / 2, len = Math.Max(0.5f, f.LengthCells * EdgeFade.CellPx / 2);
        for (int x = 0; x < area.W; x++)
        {
            float e = edge[x];
            if (float.IsNaN(e)) continue;
            for (int y = 0; y < area.H; y++)
            {
                float gy = area.Y + y + 0.5f;
                float depth = f.Lower ? gy - e : e - gy;
                if (depth <= start) continue;
                a[y * area.W + x] *= Math.Clamp(1f - (depth - start) / len, 0f, 1f);
            }
        }
    }

    /// <summary>Multiplies each stencilled cell's pixels by its stencil (stretched over the cell's box).</summary>
    public static void Carve(float[] a, IReadOnlyCollection<CellCoord> cells, IReadOnlyList<CellStencil> stencils, Func<string, StencilImage?> loadStencil) =>
        CarveIn(MaskArea.Full, a, cells, stencils, loadStencil);

    public static void CarveIn(MaskArea area, float[] a, IReadOnlyCollection<CellCoord> cells, IReadOnlyList<CellStencil> stencils, Func<string, StencilImage?> loadStencil)
    {
        var set = cells as HashSet<CellCoord> ?? cells.ToHashSet();
        foreach (var s in stencils)
        {
            if (!set.Contains(s.Cell)) continue;
            if (s.Hide) { ForEachPixelOf(area, s.Cell, (i, _, _) => a[i] = 0f); continue; }
            if (loadStencil(s.Stencil) is not { } img) continue;
            var centre = IsoGrid.CellCenter(s.Cell);
            float rad = -s.Rotation * MathF.PI / 180f, cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            float scale = Math.Max(0.05f, s.Scale);
            ForEachPixelOf(area, s.Cell, (i, px, py) =>
            {
                // Into the cell's own frame (mirrored first, so offsets and turns mirror too),
                // then undo the mask's move / turn / size to find where to read it.
                float lx = px - centre.X, ly = py - centre.Y;
                if (s.FlipX) lx = -lx;
                lx -= s.OffsetX; ly -= s.OffsetY;
                float rx = (lx * cos - ly * sin) / scale, ry = (lx * sin + ly * cos) / scale;
                float u = rx / IsoGrid.CellWidth + 0.5f, v = ry / IsoGrid.CellHeight + 0.5f;
                if (s.FlipY) v = 1 - v;
                if (u < 0 || u > 1 || v < 0 || v > 1) return; // outside the mask: keep
                a[i] *= img.Sample(u, v);
            });
        }
    }

    /// <summary>
    /// Bottom edges: along the wall, a strip mask replaces the smooth end of the fade with a shape
    /// (rock tips, crumbling...). The strip sits at the fade, so it moves with the cloud level.
    /// Above the strip: kept. In it: the strip's own picture (opaque keeps). Below it: cut.
    /// </summary>
    public static void ApplyBottomIn(MaskArea area, float[] a, float[] edge, EdgeFade f, IReadOnlyList<BottomSegment> segments, Func<string, StencilImage?> loadStencil)
    {
        for (int x = 0; x < area.W; x++)
        {
            float e = edge[x];
            if (float.IsNaN(e)) continue;
            float sx = (area.X + x) * 2 + 1;
            BottomSegment? seg = null;
            foreach (var sg in segments) if (sx >= sg.MinX && sx <= sg.MaxX) { seg = sg; break; }
            if (seg is null || loadStencil(seg.Stencil) is not { } img) continue;
            // Where the strip's bottom sits, measured from the wall's edge (half-res px).
            float bottom = (f.StartCells + f.LengthCells * 0.5f + seg.Offset) * EdgeFade.CellPx / 2;
            float h = Math.Max(1, seg.Height / 2);
            float u = sx / Math.Max(8, seg.Repeat);
            u -= MathF.Floor(u);
            if (seg.FlipX) u = 1 - u;
            for (int y = 0; y < area.H; y++)
            {
                float gy = area.Y + y + 0.5f;
                float depth = f.Lower ? gy - e : e - gy;
                if (depth <= bottom - h) continue;
                a[y * area.W + x] *= depth >= bottom ? 0f : img.Sample(u, (depth - (bottom - h)) / h);
            }
        }
    }

    /// <summary>1 where a pixel's centre is in one of the cells.</summary>
    public static float[] InsideIn(MaskArea area, IReadOnlyCollection<CellCoord> cells)
    {
        var a = new float[area.Length];
        foreach (var c in cells as HashSet<CellCoord> ?? cells.ToHashSet())
            ForEachPixelOf(area, c, (i, _, _) => a[i] = 1f);
        return a;
    }

    /// <summary>Calls back for each pixel of the area whose centre lies in the cell (index, full-res x, y).</summary>
    private static void ForEachPixelOf(MaskArea area, CellCoord c, Action<int, float, float> each)
    {
        var centre = IsoGrid.CellCenter(c);
        int x0 = Math.Max(area.X, (int)((centre.X - IsoGrid.HalfW) / 2) - 1), x1 = Math.Min(area.X + area.W - 1, (int)((centre.X + IsoGrid.HalfW) / 2) + 1);
        int y0 = Math.Max(area.Y, (int)((centre.Y - IsoGrid.HalfH) / 2) - 1), y1 = Math.Min(area.Y + area.H - 1, (int)((centre.Y + IsoGrid.HalfH) / 2) + 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new System.Numerics.Vector2(x * 2 + 1, y * 2 + 1);
                if (IsoGrid.CellAt(p) != c) continue;
                each((y - area.Y) * area.W + (x - area.X), p.X, p.Y);
            }
    }

    /// <summary>
    /// The straightened edge the fade is measured from, per column of the area (half-res screen y;
    /// NaN = no cells in that column). Lower: the top edge. Upper: the bottom edge.
    /// </summary>
    public static float[] EdgeIn(MaskArea area, float[] inside, EdgeFade fade)
    {
        var raw = new float[area.W];
        for (int x = 0; x < area.W; x++)
        {
            raw[x] = float.NaN;
            if (fade.Lower) { for (int y = 0; y < area.H; y++) if (inside[y * area.W + x] > 0) { raw[x] = area.Y + y; break; } }
            else { for (int y = area.H - 1; y >= 0; y--) if (inside[y * area.W + x] > 0) { raw[x] = area.Y + y + 1; break; } }
        }
        if (fade.Angle != FadeAngle.Auto) return Straighten(raw, area.X, SlopeOf(fade.Angle), fade.Lower, out _);
        // Auto: whichever cell-edge angle leaves the least zig-zag (ties go level).
        var right = Straighten(raw, area.X, Slope, fade.Lower, out float rightError);
        var left = Straighten(raw, area.X, -Slope, fade.Lower, out float leftError);
        var level = Straighten(raw, area.X, 0, fade.Lower, out float levelError);
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
    private static float[] Straighten(float[] raw, int x0, float slope, bool top, out float error)
    {
        int n = raw.Length;
        var k = new float[n];
        for (int x = 0; x < n; x++) k[x] = raw[x] - slope * (x0 + x);
        var result = new float[n];
        error = 0;
        for (int x = 0; x < n; x++)
        {
            if (float.IsNaN(raw[x])) { result[x] = float.NaN; continue; }
            float best = k[x];
            for (int i = Math.Max(0, x - Window); i <= Math.Min(n - 1, x + Window); i++)
            {
                if (float.IsNaN(k[i])) continue;
                best = top ? Math.Min(best, k[i]) : Math.Max(best, k[i]);
            }
            result[x] = best + slope * (x0 + x);
            error += Math.Abs(k[x] - best);
        }
        return result;
    }

    private static void BoxBlur(MaskArea area, float[] src, float[] dst, int r, bool horizontal)
    {
        int w = area.W, lines = horizontal ? area.H : area.W, len = horizontal ? area.W : area.H;
        float inv = 1f / (2 * r + 1);
        for (int l = 0; l < lines; l++)
        {
            int Idx(int i) => horizontal ? l * w + i : i * w + l;
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
