namespace Chasm.Tools.Art;

/// <summary>
/// Starter carve-out stencils (content/art/stencils). Each is 256x128 = one cell (128x64) at 2x,
/// so hand-drawn edges stay crisp. Opaque = keep the texture, transparent = cut it away.
/// "_template_cell.png" is a fully kept diamond with guide marks: copy it and erase what to cut.
/// </summary>
public static class StencilArt
{
    public const int W = 256, H = 128;

    public static IEnumerable<string> Generate(string artRoot)
    {
        string dir = Path.Combine(artRoot, "stencils");
        Directory.CreateDirectory(dir);
        var shapes = new (string name, Func<float, float, bool> keep, bool guides)[]
        {
            ("_template_cell", (_, _) => true, true),
            ("zigzag_bottom", (u, v) => v < 0.62f + 0.12f * Tri(u * 6), false),
            ("half_circle_bottom", (u, v) => Dist(u, v, 0.5f, 1.0f, 2f) > 0.42f, false),
            ("half_circle_top", (u, v) => Dist(u, v, 0.5f, 0.0f, 2f) > 0.42f, false),
            ("notch_left", (u, v) => !(u < 0.5f - Math.Abs(v - 0.5f) * 0.8f && u < 0.42f), false),
            ("notch_right", (u, v) => !(u > 0.5f + Math.Abs(v - 0.5f) * 0.8f && u > 0.58f), false),
            ("jagged_bottom", (u, v) => v < 0.58f + 0.10f * Rough(u), false),
            ("crumble_corner", (u, v) => Dist(u, v, 1.0f, 0.5f, 2f) > 0.30f + 0.06f * Rough(v * 3), false),
        };
        foreach (var (name, keep, guides) in shapes)
        {
            var px = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                    bool inDiamond = Math.Abs(u - 0.5f) * 2 + Math.Abs(v - 0.5f) * 2 <= 1.0f;
                    int i = (y * W + x) * 4;
                    if (!inDiamond || !keep(u, v)) continue; // transparent = cut
                    bool guide = guides && (Math.Abs(u - 0.5f) < 0.004f || Math.Abs(v - 0.5f) < 0.008f
                        || Math.Abs(Math.Abs(u - 0.5f) * 2 + Math.Abs(v - 0.5f) * 2 - 1f) < 0.012f);
                    byte c = guide ? (byte)90 : (byte)235;
                    px[i] = c; px[i + 1] = c; px[i + 2] = guide ? (byte)200 : (byte)235; px[i + 3] = 255;
                }
            string path = Path.Combine(dir, name + ".png");
            PngWriter.Write(path, W, H, px, alpha: true);
            yield return path;
        }
    }

    private static float Tri(float t) { t -= MathF.Floor(t); return MathF.Abs(t - 0.5f) * 2f - 0.5f; }

    private static float Dist(float u, float v, float cu, float cv, float aspect) =>
        MathF.Sqrt((u - cu) * (u - cu) + (v - cv) * (v - cv) / (aspect * aspect));

    private static float Rough(float t) =>
        0.5f * MathF.Sin(t * 23.1f) + 0.3f * MathF.Sin(t * 57.7f + 1.3f) + 0.2f * MathF.Sin(t * 131f + 0.4f);
}
