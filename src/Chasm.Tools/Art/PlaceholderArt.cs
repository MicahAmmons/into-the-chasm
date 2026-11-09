namespace Chasm.Tools.Art;

/// <summary>
/// Generates the placeholder art pack into content/art/placeholder/ (run: ChasmTools --generate-art).
///
/// Textures are 1024x1024 and seamless (every noise layer repeats exactly across the edges), so the
/// map engine can repeat them to cover the screen at any rotation. They're mid-tone and fairly
/// neutral so a material's tint can recolour them. Props are transparent PNGs whose base sits at
/// the bottom centre (where the engine anchors props).
///
/// Everything is deterministic: re-running produces identical files.
/// </summary>
public static class PlaceholderArt
{
    private const int TexSize = 1024;

    public static List<string> Generate(string artRoot)
    {
        string tex = Path.Combine(artRoot, "placeholder", "textures");
        string props = Path.Combine(artRoot, "placeholder", "props");
        var written = new List<string>();
        void Tex(string name, Func<float, float, (float r, float g, float b)> shade)
        {
            string p = Path.Combine(tex, name + ".png");
            WriteTexture(p, shade);
            written.Add(p);
        }
        Tex("rock_strata", RockStrata);
        Tex("grass", Grass);
        Tex("dirt", Dirt);
        Tex("moss", Moss);
        Tex("stone_wall", StoneWall);

        void Prop(string name, int w, int h, Func<float, float, (float r, float g, float b, float a)> shade)
        {
            string p = Path.Combine(props, name + ".png");
            WriteProp(p, w, h, shade);
            written.Add(p);
        }
        Prop("rock_small", 140, 100, (x, y) => Rock(x, y, 140, 100, seed: 3));
        Prop("rock_large", 260, 190, (x, y) => Rock(x, y, 260, 190, seed: 11));
        Prop("bush", 200, 170, (x, y) => Bush(x, y, 200, 170, seed: 5));
        Prop("bush_small", 130, 110, (x, y) => Bush(x, y, 130, 110, seed: 9));
        Prop("crystal", 140, 220, (x, y) => Crystal(x, y, 140, 220, seed: 2));
        Prop("crystal_cluster", 220, 200, (x, y) => Crystal(x, y, 220, 200, seed: 7));
        return written;
    }

    // ------------------------------------------------------------------ seamless noise

    private static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    private static float Rand(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / (float)0xFFFFFF;

    private static int Mod(int a, int m) => ((a % m) + m) % m;

    /// <summary>Value noise on a lattice that repeats every <paramref name="period"/> cells (0..1).</summary>
    private static float Noise(float x, float y, int period, int seed)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
        float a = Rand(x0, y0, seed), b = Rand(x1, y0, seed), c = Rand(x0, y1, seed), d = Rand(x1, y1, seed);
        return Lerp(Lerp(a, b, sx), Lerp(c, d, sx), sy);
    }

    /// <summary>Fractal noise over texture coords u,v in [0,1); repeats exactly at 1.</summary>
    private static float Fbm(float u, float v, int basePeriod, int octaves, int seed, float gain = 0.5f)
    {
        float sum = 0, amp = 1, norm = 0;
        int period = basePeriod;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Noise(u * period, v * period, period, seed + o * 101);
            norm += amp;
            amp *= gain;
            period *= 2;
        }
        return sum / norm;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
    private static float Smooth(float e0, float e1, float x) { float t = Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }

    private static (float, float, float) Mix((float r, float g, float b) a, (float r, float g, float b) b, float t) =>
        (Lerp(a.r, b.r, t), Lerp(a.g, b.g, t), Lerp(a.b, b.b, t));

    private static (float, float, float) Scale((float r, float g, float b) c, float k) => (c.r * k, c.g * k, c.b * k);

    // ------------------------------------------------------------------ textures

    private static (float, float, float) RockStrata(float u, float v)
    {
        // Wavy sedimentary layers: an integer number of bands so it repeats vertically.
        const int bands = 14;
        float warp = (Fbm(u, v, 3, 4, 10) - 0.5f) * 1.6f;
        float t = v * bands + warp;
        int layer = (int)MathF.Floor(t);
        float within = t - layer;
        float layerTone = Rand(Mod(layer, bands), 0, 77);
        var palette = new[] { (0.47f, 0.40f, 0.33f), (0.56f, 0.49f, 0.40f), (0.40f, 0.35f, 0.30f), (0.62f, 0.55f, 0.45f) };
        var baseC = Mix(palette[(int)(layerTone * 4) % 4], palette[(int)(layerTone * 7) % 4], 0.35f);
        float grit = Fbm(u, v, 32, 3, 20);
        float edge = Smooth(0.0f, 0.08f, within) * Smooth(1.0f, 0.9f, within); // darker seams between layers
        float crack = Smooth(0.012f, 0.0f, MathF.Abs(Fbm(u, v, 6, 4, 30) - 0.5f));
        float k = (0.78f + 0.3f * grit) * (0.75f + 0.25f * edge) * (1 - 0.45f * crack);
        return Scale(baseC, k);
    }

    private static (float, float, float) Grass(float u, float v)
    {
        float patch = Fbm(u, v, 4, 4, 40);
        float blades = Noise(u * 256, v * 64, 256, 41); // stretched: short vertical streaks
        float fine = Fbm(u, v, 64, 2, 42);
        var dark = (0.20f, 0.42f, 0.18f);
        var light = (0.42f, 0.66f, 0.28f);
        var c = Mix(dark, light, Clamp01(patch * 1.2f - 0.1f));
        float k = 0.8f + 0.25f * blades + 0.1f * fine;
        var dry = (0.55f, 0.58f, 0.30f);
        return Mix(Scale(c, k), dry, Smooth(0.68f, 0.82f, Fbm(u, v, 3, 3, 43)) * 0.4f);
    }

    private static (float, float, float) Dirt(float u, float v)
    {
        float tone = Fbm(u, v, 5, 5, 50);
        var c = Mix((0.36f, 0.26f, 0.18f), (0.52f, 0.40f, 0.28f), tone);
        float pebbles = Noise(u * 128, v * 128, 128, 51);
        float pebble = Smooth(0.82f, 0.9f, pebbles);
        float shade = 0.85f + 0.25f * Fbm(u, v, 48, 2, 52);
        return Mix(Scale(c, shade), (0.66f, 0.60f, 0.52f), pebble * 0.7f);
    }

    private static (float, float, float) Moss(float u, float v)
    {
        float blobs = Fbm(u, v, 6, 5, 60, 0.55f);
        float lumps = Fbm(u, v, 40, 3, 61);
        var c = Mix((0.12f, 0.26f, 0.14f), (0.33f, 0.52f, 0.22f), Smooth(0.35f, 0.75f, blobs));
        return Scale(c, 0.8f + 0.35f * lumps);
    }

    private static (float, float, float) StoneWall(float u, float v)
    {
        const int rows = 8, cols = 5;
        float y = v * rows;
        int row = (int)MathF.Floor(y);
        float x = u * cols + (row % 2 == 1 ? 0.5f : 0f);
        int col = (int)MathF.Floor(x);
        float fx = x - col, fy = y - row;
        // Slightly wobbly mortar lines.
        float wob = (Fbm(u, v, 8, 3, 70) - 0.5f) * 0.08f;
        float mortar = MathF.Min(MathF.Min(fx + wob, 1 - fx - wob), MathF.Min(fy - wob, 1 - fy + wob));
        float blockTone = Rand(Mod(col, cols), Mod(row, rows), 71);
        var stone = Mix((0.42f, 0.42f, 0.45f), (0.60f, 0.59f, 0.58f), blockTone);
        float surface = 0.82f + 0.3f * Fbm(u, v, 24, 3, 72);
        float bevel = Smooth(0.02f, 0.08f, mortar);
        var block = Scale(stone, surface * (0.7f + 0.3f * bevel));
        return Mix((0.16f, 0.15f, 0.15f), block, Smooth(0.0f, 0.025f, mortar));
    }

    private static void WriteTexture(string path, Func<float, float, (float r, float g, float b)> shade)
    {
        var px = new byte[TexSize * TexSize * 3];
        Parallel.For(0, TexSize, y =>
        {
            for (int x = 0; x < TexSize; x++)
            {
                var (r, g, b) = shade((x + 0.5f) / TexSize, (y + 0.5f) / TexSize);
                int i = (y * TexSize + x) * 3;
                px[i] = ToByte(r); px[i + 1] = ToByte(g); px[i + 2] = ToByte(b);
            }
        });
        PngWriter.Write(path, TexSize, TexSize, px, alpha: false);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);

    // ------------------------------------------------------------------ props

    private static (float, float, float, float) Rock(float px, float py, int w, int h, int seed)
    {
        // Lumpy ellipse sitting on the bottom edge; light from the top-left.
        float cx = w / 2f, cy = h * 0.58f, rx = w * 0.46f, ry = h * 0.40f;
        float dx = (px - cx) / rx, dy = (py - cy) / ry;
        float ang = MathF.Atan2(dy, dx);
        float lump = 1 + 0.12f * (Noise((ang + MathF.PI) / (2 * MathF.PI) * 8, 0.5f, 8, seed) - 0.5f) * 2;
        float d = MathF.Sqrt(dx * dx + dy * dy) / lump;
        float alpha = Smooth(1.0f, 0.96f, d);
        if (py > h - 2) alpha *= 0.6f;
        if (alpha <= 0) return (0, 0, 0, 0);
        float light = Clamp01(0.55f - dx * 0.35f - dy * 0.45f + (1 - d) * 0.25f);
        float grain = Noise(px / 6f, py / 6f, 997, seed + 1);
        var c = Mix((0.30f, 0.29f, 0.30f), (0.70f, 0.68f, 0.64f), light);
        c = Scale(c, 0.85f + 0.25f * grain);
        float rim = Smooth(0.86f, 0.98f, d); // dark outline
        c = Mix(c, (0.12f, 0.11f, 0.12f), rim * 0.8f);
        return (c.Item1, c.Item2, c.Item3, alpha);
    }

    private static (float, float, float, float) Bush(float px, float py, int w, int h, int seed)
    {
        // A cluster of overlapping circles; each circle shaded like a ball.
        var rnd = new Random(seed);
        float best = 0, bestLight = 0, bestDepth = 0;
        int n = 7;
        for (int i = 0; i < n; i++)
        {
            float r = (0.22f + 0.12f * (float)rnd.NextDouble()) * w;
            float cx = w * (0.25f + 0.5f * (float)rnd.NextDouble());
            float cy = h - r - (float)rnd.NextDouble() * h * 0.35f;
            float dx = (px - cx) / r, dy = (py - cy) / r;
            float d2 = dx * dx + dy * dy;
            if (d2 > 1) continue;
            float cover = Smooth(1f, 0.92f, MathF.Sqrt(d2));
            if (cover >= best)
            {
                best = cover;
                bestLight = Clamp01(0.6f - dx * 0.35f - dy * 0.5f);
                bestDepth = MathF.Sqrt(d2);
            }
        }
        if (best <= 0) return (0, 0, 0, 0);
        float leaves = Noise(px / 4f, py / 4f, 997, seed + 3);
        var c = Mix((0.10f, 0.28f, 0.12f), (0.40f, 0.68f, 0.30f), bestLight);
        c = Scale(c, 0.78f + 0.35f * leaves);
        c = Mix(c, (0.05f, 0.14f, 0.06f), Smooth(0.85f, 1f, bestDepth) * 0.7f);
        return (c.Item1, c.Item2, c.Item3, best);
    }

    private static (float, float, float, float) Crystal(float px, float py, int w, int h, int seed)
    {
        // Several pointed shards rising from the base; each has a lit and a shaded face.
        var rnd = new Random(seed);
        int n = w > 180 ? 5 : 3;
        float alpha = 0, r = 0, g = 0, b = 0;
        for (int i = 0; i < n; i++)
        {
            float bx = w * (0.2f + 0.6f * (float)rnd.NextDouble());
            float tall = h * (0.45f + 0.5f * (float)rnd.NextDouble());
            float half = w * (0.07f + 0.06f * (float)rnd.NextDouble());
            float lean = ((float)rnd.NextDouble() - 0.5f) * 0.6f;
            // Transform into the shard's frame: y from base (0) to tip (1).
            float t = (h - py) / tall;
            if (t < 0 || t > 1) continue;
            float centre = bx + lean * (h - py);
            float widthHere = half * (t < 0.75f ? 1f : (1 - t) / 0.25f);
            float off = (px - centre) / Math.Max(0.5f, widthHere);
            if (MathF.Abs(off) > 1) continue;
            float a = Smooth(1f, 0.85f, MathF.Abs(off));
            bool litFace = off < 0;
            var c = litFace ? (0.55f, 0.92f, 1.0f) : (0.18f, 0.48f, 0.72f);
            float glow = 0.85f + 0.3f * t;
            if (MathF.Abs(off) < 0.08f) c = (0.85f, 1f, 1f); // ridge highlight
            alpha = MathF.Max(alpha, a);
            r = c.Item1 * glow; g = c.Item2 * glow; b = c.Item3 * glow;
        }
        return (r, g, b, alpha);
    }

    private static void WriteProp(string path, int w, int h, Func<float, float, (float r, float g, float b, float a)> shade)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // 2x2 supersampling for smooth edges.
                float r = 0, g = 0, b = 0, a = 0;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        var s = shade(x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f);
                        r += s.r * s.a; g += s.g * s.a; b += s.b * s.a; a += s.a;
                    }
                int i = (y * w + x) * 4;
                if (a > 0) { px[i] = ToByte(r / a); px[i + 1] = ToByte(g / a); px[i + 2] = ToByte(b / a); }
                px[i + 3] = ToByte(a / 4f);
            }
        PngWriter.Write(path, w, h, px, alpha: true);
    }
}
