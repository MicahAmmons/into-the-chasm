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

    public const int StripW = 512, StripH = 128;

    /// <summary>
    /// Bottom-edge strips (art/stencils/bottom edges): 512x128, repeat seamlessly left to right.
    /// Opaque (top) = wall kept, transparent (bottom) = cut. The shape is where the wall ends
    /// into the clouds. '_template_bottom_edge' is a flat edge with a guide line to draw your own.
    /// </summary>
    public static IEnumerable<string> GenerateBottomEdges(string artRoot)
    {
        string dir = Path.Combine(artRoot, "stencils", "bottom edges");
        Directory.CreateDirectory(dir);
        const float Tau = MathF.PI * 2;
        // Each returns how far down (0..1 of the strip) the wall reaches at u (0..1, periodic).
        var shapes = new (string name, Func<float, float> reach)[]
        {
            ("_template_bottom_edge", _ => 0.5f),
            ("jagged_rocks", u => 0.55f + 0.22f * Tri(u * 7) + 0.08f * MathF.Sin(u * Tau * 3)),
            ("crumbled", u => 0.5f + 0.12f * Rough(u * 4) + 0.06f * MathF.Sin(u * Tau * 11)),
            ("stalactites", u => 0.35f + 0.55f * MathF.Pow(MathF.Max(0, MathF.Sin(u * Tau * 5)), 6) + 0.25f * MathF.Pow(MathF.Max(0, MathF.Sin(u * Tau * 3 + 1.1f)), 10)),
            ("rounded_bumps", u => 0.42f + 0.3f * MathF.Abs(MathF.Sin(u * Tau * 2.5f))),
            ("sawtooth", u => 0.4f + 0.4f * (u * 8 - MathF.Floor(u * 8))),
            ("roots", u => 0.45f + 0.08f * Rough(u * 3) + 0.45f * (MathF.Abs(MathF.Sin(u * Tau * 9)) > 0.985f ? 1 : 0)),
            ("broken_teeth", u => 0.4f + 0.12f * MathF.Floor(3 * (0.5f + 0.5f * MathF.Sin(MathF.Floor(u * 10) * 2.7f))) / 2f + 0.1f),
            ("wavy", u => 0.5f + 0.18f * MathF.Sin(u * Tau * 2) + 0.06f * MathF.Sin(u * Tau * 5)),
            ("stepped_blocks", u => 0.35f + 0.15f * MathF.Floor(4 * (0.5f + 0.5f * MathF.Sin(u * Tau * 2))) / 1.5f * 0.6f),
            ("sparse_spikes", u => 0.4f + 0.5f * MathF.Pow(MathF.Max(0, MathF.Cos(u * Tau * 3)), 40)),
        };
        foreach (var (name, reach) in shapes)
        {
            var px = new byte[StripW * StripH * 4];
            for (int x = 0; x < StripW; x++)
            {
                float r = Math.Clamp(reach((x + 0.5f) / StripW), 0.05f, 0.98f);
                for (int y = 0; y < StripH; y++)
                {
                    float v = (y + 0.5f) / StripH;
                    float a = Math.Clamp((r - v) * StripH + 0.5f, 0, 1); // 1 px soft edge
                    bool guide = name.StartsWith('_') && Math.Abs(v - 0.5f) < 0.006f;
                    int i = (y * StripW + x) * 4;
                    byte c = guide ? (byte)90 : (byte)230;
                    px[i] = c; px[i + 1] = c; px[i + 2] = guide ? (byte)200 : (byte)230; px[i + 3] = guide ? (byte)255 : (byte)(a * 255);
                }
            }
            string path = Path.Combine(dir, name + ".png");
            PngWriter.Write(path, StripW, StripH, px, alpha: true);
            yield return path;
        }
    }

    /// <summary>
    /// End-cap masks (art/stencils/end caps): one cell (256x128 = 128x64 at 2x), drawn for a wall
    /// that ENDS ON THE RIGHT (mirrored automatically for left ends). Opaque keeps, transparent cuts.
    /// taper_1..3 cut deeper and deeper, for stacking down a column (row 1 gentle, row 2 deeper...).
    /// </summary>
    public static IEnumerable<string> GenerateEndCaps(string artRoot)
    {
        string dir = Path.Combine(artRoot, "stencils", "end caps");
        Directory.CreateDirectory(dir);
        var shapes = new (string name, Func<float, float, bool> keep, bool guides)[]
        {
            ("_template_end_cap", (_, _) => true, true),
            ("taper_1", (u, v) => u < 0.88f - 0.22f * v * v, false),
            ("taper_2", (u, v) => u < 0.72f - 0.3f * MathF.Pow(v, 1.4f), false),
            ("taper_3", (u, v) => u < 0.55f - 0.35f * v, false),
            ("round_end", (u, v) => ((u - 0.25f) / 0.62f) * ((u - 0.25f) / 0.62f) + ((v - 0.1f) / 0.95f) * ((v - 0.1f) / 0.95f) < 1, false),
            ("jagged_end", (u, v) => u < 0.68f + 0.1f * Tri(v * 5) - 0.1f * v, false),
            ("crumble_end", (u, v) => u < 0.66f + 0.07f * Rough(v * 2.5f) - 0.15f * v, false),
            ("overhang_end", (u, v) => u < 0.95f - 0.75f * MathF.Pow(v, 0.6f), false),
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
                    if (!inDiamond || !keep(u, v)) continue;
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
