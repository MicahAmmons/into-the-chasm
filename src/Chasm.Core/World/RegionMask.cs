using Chasm.Core.Grid;

namespace Chasm.Core.World;

/// <summary>
/// The alpha mask for a paint region at half resolution (960x540): 1 inside the region's cells,
/// softened by the feather (two box-blur passes, close to a gaussian), times the cloud fade.
/// Pure maths, so it's unit-tested; the game uploads it as a texture.
/// </summary>
public static class RegionMask
{
    public const int Width = 960, Height = 540;

    public static float[] Build(IReadOnlyCollection<CellCoord> cells, int featherPx, CloudLayerDef? cloud, bool lower)
    {
        var set = cells as HashSet<CellCoord> ?? cells.ToHashSet();
        var a = new float[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (set.Contains(IsoGrid.CellAt(new System.Numerics.Vector2(x * 2 + 1, y * 2 + 1)))) a[y * Width + x] = 1f;

        int r = Math.Max(0, featherPx / 2);
        if (r > 0)
        {
            int pass = Math.Max(1, (int)MathF.Ceiling(r / 1.7f));
            var tmp = new float[a.Length];
            for (int i = 0; i < 2; i++) { BoxBlur(a, tmp, pass, horizontal: true); BoxBlur(tmp, a, pass, horizontal: false); }
        }
        if (cloud is not null)
            for (int y = 0; y < Height; y++)
            {
                float fade = cloud.FadeAt((y + 0.5f) / Height, lower);
                for (int x = 0; x < Width; x++) a[y * Width + x] *= fade;
            }
        return a;
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
