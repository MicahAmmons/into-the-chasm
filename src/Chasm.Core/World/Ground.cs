using Chasm.Core.Content;
using Chasm.Core.Grid;

namespace Chasm.Core.World;

/// <summary>
/// One texture layer of a ground: drawn across the screen (like a painted texture) and, with
/// <see cref="Patches"/>, only in natural-looking blotches (dirt through grass, moss on stone).
/// </summary>
public sealed class GroundLayer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Layer";
    public bool Enabled { get; set; } = true;
    [Texture] public string Texture { get; set; } = "";
    public string Tint { get; set; } = "#ffffff";
    [Range(0, 1)] public float Opacity { get; set; } = 1f;
    [Range(0.05f, 8)] public float Scale { get; set; } = 1f;
    [Range(0, 360)] public float Rotation { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public bool FlipX { get; set; }

    [Tooltip("Show this layer only in patches (blotches), not everywhere.")] public bool Patches { get; set; }
    [Range(16, 1200), Tooltip("Patches: how big the blotches are (pixels).")] public float PatchSize { get; set; } = 180f;
    [Range(0, 1), Tooltip("Patches: how much of the ground they cover.")] public float PatchAmount { get; set; } = 0.4f;
    [Range(0, 1), Tooltip("Patches: soft (1) or crisp (0) blotch edges.")] public float PatchSoftness { get; set; } = 0.35f;
    [Tooltip("Patches: a different number gives a different arrangement.")] public int PatchSeed { get; set; } = 1;

    public GroundLayer Copy() => (GroundLayer)MemberwiseClone();

    /// <summary>The layer as a material, for the shared screen-texture drawing.</summary>
    public MaterialDef AsMaterial() => new()
    {
        Id = Id, Name = Name, Texture = Texture, Tint = Tint, Scale = Scale, Rotation = Rotation,
        OffsetX = OffsetX, OffsetY = OffsetY, FlipX = FlipX,
    };

    public string PatchKey => Patches ? $"p{PatchSize:0}/{PatchAmount:0.###}/{PatchSoftness:0.###}/{PatchSeed}" : "";
}

/// <summary>
/// A reusable ground look ("Mossy grass"): a stack of texture layers, back to front, plus how it
/// ends at a cliff edge and how it blends where it meets another ground. Made in the ground
/// builder; floors use it on walkable cells (automatically) or on unwalkable cells (painted).
/// </summary>
[DefFolder("groundprefabs", "Ground prefabs")]
[Tooltip("A reusable ground look made of texture layers.")]
public sealed class GroundPrefabDef : Def
{
    public string Category { get; set; } = "Ground";
    public List<GroundLayer> Layers { get; set; } = new();

    [Range(0, 64), Tooltip("How soft the ground's edge is where it ends (at a cliff or the void), in pixels.")]
    public int EdgeSoftness { get; set; } = 6;
    [Range(0, 1), Tooltip("Darkens the ground near its edge (worn / shaded rim). 0 = off.")]
    public float EdgeShade { get; set; }
    [Range(4, 160), Tooltip("How far in from the edge the darkening reaches (pixels).")]
    public float EdgeShadeWidth { get; set; } = 28f;
    [Range(0, 128), Tooltip("Where this ground is painted over another, how wide the blend between them is (pixels).")]
    public int Transition { get; set; } = 24;

    /// <summary>Steps of the ground builder that have been approved.</summary>
    [Hidden] public List<string> Approved { get; set; } = new();

    public string NewLayerId()
    {
        for (int n = 1; ; n++)
            if (Layers.All(l => l.Id != "layer_" + n)) return "layer_" + n;
    }
}

/// <summary>A ground in a floor's Walkable / Unwalkable list.</summary>
public sealed class FloorGround
{
    [Ref(typeof(GroundPrefabDef))] public string Prefab { get; set; } = "";
}

/// <summary>Cells on a tile painted with a ground: "walk" side (over the automatic one, "-" = none) or "void" side (unwalkable).</summary>
public sealed class TileGroundPaint
{
    public const string NoGround = "-";
    [Tooltip("\"walk\" or \"void\".")] public string Side { get; set; } = "walk";
    [Tooltip("A ground prefab id from the floor's list, or '-' for no ground (walkable side).")] public string Ground { get; set; } = "";
    public List<CellCoord> Cells { get; set; } = new();
}

/// <summary>A patch of cells drawn with one ground.</summary>
/// <param name="Base">The automatic ground under all walkable cells (others are drawn over it).</param>
public sealed record GroundRegion(bool Walkable, string Prefab, IReadOnlyList<CellCoord> Cells, bool Base);

/// <summary>Works out which ground goes where on a tile, live.</summary>
public static class GroundMap
{
    /// <summary>
    /// Back to front: unwalkable grounds (painted only), then the walkable base (the floor's first
    /// walkable ground on every walkable cell, except cells painted 'no ground'), then walkable
    /// grounds painted over it.
    /// </summary>
    public static List<GroundRegion> Build(TileDef tile, FloorDef? floor)
    {
        var result = new List<GroundRegion>();
        if (floor is null) return result;
        var walkIds = floor.GroundWalkable.Select(g => g.Prefab).Where(p => p.Length > 0).ToList();
        var voidIds = floor.GroundUnwalkable.Select(g => g.Prefab).Where(p => p.Length > 0).ToList();

        // Unwalkable: only where painted, only on unwalkable cells, in list order.
        foreach (var id in voidIds)
        {
            var cells = tile.GroundPaint.Where(p => p.Side == "void" && p.Ground == id)
                .SelectMany(p => p.Cells).Where(c => IsoGrid.InTile(c) && !tile.IsWalkable(c)).Distinct().ToList();
            if (cells.Count > 0) result.Add(new GroundRegion(false, id, cells, false));
        }

        if (walkIds.Count == 0) return result;
        var walk = IsoGrid.AllCells.Where(tile.IsWalkable).ToList();
        var paint = new Dictionary<CellCoord, string>();
        foreach (var p in tile.GroundPaint.Where(p => p.Side == "walk"))
            foreach (var c in p.Cells) paint[c] = p.Ground;
        var baseCells = walk.Where(c => !(paint.TryGetValue(c, out var g) && g == TileGroundPaint.NoGround)).ToList();
        if (baseCells.Count > 0) result.Add(new GroundRegion(true, walkIds[0], baseCells, true));
        foreach (var id in walkIds.Skip(1))
        {
            var cells = walk.Where(c => paint.TryGetValue(c, out var g) && g == id).ToList();
            if (cells.Count > 0) result.Add(new GroundRegion(true, id, cells, false));
        }
        return result;
    }
}

/// <summary>Soft natural blotches for ground layers (value noise, two octaves).</summary>
public static class GroundNoise
{
    /// <summary>Patch amount at a screen point, 0..1.</summary>
    public static float Patch(float x, float y, float size, float amount, float softness, int seed)
    {
        float n = 0.65f * Value(x / size, y / size, seed) + 0.35f * Value(x / (size * 0.45f), y / (size * 0.45f), seed * 7919 + 13);
        float t = 1f - Math.Clamp(amount, 0f, 1f);
        float w = Math.Max(0.01f, softness * 0.5f);
        return SmoothStep(t - w, t + w, n);
    }

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private static float Value(float x, float y, int seed)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed), c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
