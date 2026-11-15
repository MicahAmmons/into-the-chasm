using System.Text;
using System.Text.Json.Serialization;
using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Rules;

namespace Chasm.Core.World;

public enum TileLayerKind
{
    [Label("Background (behind everything)")] Background,
    [Label("Foreground (in front of characters)")] Foreground,
    [Label("Fog / cloud overlay")] Fog,
}

public sealed class TileLayerDef
{
    [Texture, Required] public string Texture { get; set; } = "";
    public TileLayerKind Kind { get; set; } = TileLayerKind.Background;
    [Range(0, 1)] public float Opacity { get; set; } = 1f;
    public override string ToString() => $"{Kind}: {Texture}";
}

/// <summary>Leaving the screen through an edge. The player is asked "Advance?" first.</summary>
public sealed class TileExitDef
{
    public Edge Edge { get; set; } = Edge.East;
    [Ref(typeof(TileDef)), Required] public string TargetTile { get; set; } = "";
    [Tooltip("Extra conditions. If they fail, BlockedMessage is shown instead.")]
    public List<Condition> Conditions { get; set; } = new();
    public string BlockedMessage { get; set; } = "";
    public string Prompt { get; set; } = "Continue to the next area?";
    public override string ToString() => $"{Edge} -> {TargetTile}";
}

public enum CombatStart { None, Player, Enemy }

/// <summary>
/// One screen of a Floor: art layers, which Cells are walkable, combat start cells, exits and
/// placed objects. Saved as content/defs/tiles/&lt;id&gt;.json and edited in the Map Editor.
/// </summary>
[DefFolder("tiles", "Tiles")]
[Tooltip("One screen of a floor.")]
public sealed class TileDef : Def
{
    [Range(0, 9999), Tooltip("Which floor this Tile belongs to. Empty = a sketch tile (no floor yet).")]
    public int? Floor { get; set; } = 1;

    [Tooltip("Position on the floor's map. Neighbours by X/Y connect automatically. Empty = not on the map.")]
    public int? X { get; set; }
    public int? Y { get; set; }

    [Tooltip("Full-screen images drawn behind everything, in order (hand-painted tile art).")]
    public List<TileLayerDef> Layers { get; set; } = new();

    [Hidden, Tooltip("Materials painted onto cells. List order = stacking order.")]
    public List<PaintRegion> PaintRegions { get; set; } = new();

    [Tooltip("Materials only this tile uses (and everything a sketch tile uses). Referenced as 'local:<id>'.")]
    public List<MaterialDef> LocalMaterials { get; set; } = new();

    [Hidden] public List<PropDef> Props { get; set; } = new();
    [Hidden, Tooltip("This tile's own settings for some of its floor's cloud layers.")]
    public List<TileCloudOverride> CloudOverrides { get; set; } = new();
    [Hidden, Tooltip("Cells painted with a wall other than the floor's default (or with no wall).")]
    public List<TileWallPaint> WallPaint { get; set; } = new();
    [Hidden, Tooltip("Cells painted with a ground other than the automatic one (walkable), or with a ground on unwalkable cells.")]
    public List<TileGroundPaint> GroundPaint { get; set; } = new();
    [Hidden, Tooltip("Corners / ends on this tile swapped to another variant of their wall.")]
    public List<TileWallVariant> WallVariants { get; set; } = new();
    [Hidden, Tooltip("Kept in the Archive section of lists (the original game's content).")]
    public bool Archived { get; set; }

    [Tooltip("Colour behind all layers, as #RRGGBB.")]
    public string BackgroundColor { get; set; } = "#1a1d2e";

    [Tooltip("Extra / special exits. Tiles with X and Y connect to their neighbours automatically; use this for anything else.")]
    public List<TileExitDef> Exits { get; set; } = new();

    [Tooltip("Where the player appears when arriving by teleport / new game.")]
    public CellCoord Spawn { get; set; } = new(16, 1);

    [Hidden] public List<CellCoord> PlayerCombatStarts { get; set; } = new();
    [Hidden, Tooltip("Cells where enemies are placed at random when a fight starts.")]
    public List<CellCoord> EnemyCombatStarts { get; set; } = new();

    /// <summary>
    /// Walkability as an ASCII picture of the screen: one string per cell row (top to bottom),
    /// '.' walkable, '#' blocked, ' ' between cells. Readable in a text editor and in git diffs.
    /// </summary>
    [Hidden]
    public List<string> WalkMap { get; set; } = new();

    [Hidden, Tooltip("Objects placed on cells: items, interactables, NPCs, monster groups...")]
    public List<Placement> Placements { get; set; } = new();

    /// <summary>The id a tile with this floor/position gets, e.g. floor 4, (3,-1) -> "f4_3_m1".</summary>
    public static string IdFor(int floor, int x, int y) => $"f{floor}_{N(x)}_{N(y)}";
    private static string N(int v) => v < 0 ? $"m{-v}" : v.ToString();

    public bool OnMap => Floor is not null && X is not null && Y is not null;

    /// <summary>Finds a material by region reference: floor library id, or "local:id".</summary>
    public MaterialDef? FindMaterial(string reference, FloorDef? floor)
    {
        if (reference.StartsWith("local:", StringComparison.Ordinal))
            return LocalMaterials.FirstOrDefault(m => m.Id == reference[6..]);
        return floor?.Materials.FirstOrDefault(m => m.Id == reference);
    }

    // ---- walkability helpers (not saved) ----

    private CellMap<bool>? _walk;

    [JsonIgnore] public CellMap<bool> Walkable => _walk ??= ParseWalkMap();

    public bool IsWalkable(CellCoord c) => IsoGrid.InTile(c) && Walkable[c];

    public void SetWalkable(CellCoord c, bool walkable)
    {
        if (!IsoGrid.InTile(c)) return;
        Walkable[c] = walkable;
        WalkMap = BuildWalkMap(Walkable);
    }

    /// <summary>Call after bulk edits to Walkable to write them back into <see cref="WalkMap"/>.</summary>
    public void CommitWalkable() => WalkMap = BuildWalkMap(Walkable);

    public void InvalidateCaches() => _walk = null;

    private CellMap<bool> ParseWalkMap()
    {
        var map = new CellMap<bool>(false);
        for (int b = 0; b < WalkMap.Count; b++)
        {
            string row = WalkMap[b];
            for (int a = 0; a < row.Length; a++)
            {
                if (((a + b) & 1) != 0 || row[a] != '.') continue;
                map[new CellCoord((a + b) / 2, (b - a) / 2)] = true;
            }
        }
        return map;
    }

    public static List<string> BuildWalkMap(CellMap<bool> walk)
    {
        int maxA = 0, maxB = 0;
        foreach (var c in IsoGrid.AllCells) { maxA = Math.Max(maxA, c.Q - c.R); maxB = Math.Max(maxB, c.Q + c.R); }
        var rows = new List<string>();
        for (int b = 0; b <= maxB; b++)
        {
            var sb = new StringBuilder(maxA + 1);
            for (int a = 0; a <= maxA; a++)
            {
                if (((a + b) & 1) != 0) { sb.Append(' '); continue; }
                var c = new CellCoord((a + b) / 2, (b - a) / 2);
                sb.Append(!IsoGrid.InTile(c) ? ' ' : walk[c] ? '.' : '#');
            }
            rows.Add(sb.ToString().TrimEnd());
        }
        return rows;
    }

    public Placement? FindPlacement(string id) => Placements.FirstOrDefault(p => p.Id == id);

    /// <summary>Next free placement id like "item_3".</summary>
    public string NewPlacementId(string prefix)
    {
        for (int n = 1; ; n++)
        {
            string id = $"{prefix}_{n}";
            if (Placements.All(p => p.Id != id)) return id;
        }
    }

    public override IEnumerable<string> Validate(DefRegistry registry)
    {
        foreach (var dup in Placements.GroupBy(p => p.Id).Where(g => g.Count() > 1))
            yield return $"Placement id '{dup.Key}' is used {dup.Count()} times; ids must be unique within a tile.";
        foreach (var p in Placements)
            if (!IsoGrid.InTile(p.Cell)) yield return $"Placement '{p.Id}' is outside the tile.";
        if (!IsWalkable(Spawn)) yield return $"Spawn cell {Spawn} is not walkable.";
        FloorDef? floor = Floor is int f ? registry.All<FloorDef>().FirstOrDefault(x => x.Number == f) : null;
        foreach (var r in PaintRegions)
        {
            if (r.Prefab is { } pf) { if (!registry.Exists(typeof(PrefabDef), pf)) yield return $"Placement '{r.Id}' uses missing prefab '{pf}'."; }
            else if (FindMaterial(r.Material, floor) is null) yield return $"Paint region '{r.Id}' uses missing material '{r.Material}'.";
        }
        if (OnMap)
        {
            var me = this;
            var clash = registry.All<TileDef>().FirstOrDefault(t => t.Id != me.Id && t.Floor == me.Floor && t.X == me.X && t.Y == me.Y);
            if (clash is not null) yield return $"Tile '{clash.Id}' already sits at floor {Floor} ({X},{Y}).";
        }
    }
}

/// <summary>A numbered floor: groups Tiles and holds floor-wide look and feel.</summary>
[DefFolder("floors", "Floors")]
[Tooltip("A numbered floor of the chasm.")]
public sealed class FloorDef : Def, System.Text.Json.Serialization.IJsonOnDeserialized
{
    /// <summary>
    /// Older files kept bottom shapes on each wall entry. They now belong to the fade: a wall with its
    /// own fade takes them into it; a wall on the floor default gets its own fade (a copy of the
    /// default) carrying them, so it looks the same as before.
    /// </summary>
    public void OnDeserialized()
    {
        foreach (var (list, def) in new[] { (WallsDown, WallDownFade), (WallsUp, WallUpFade) })
            foreach (var e in list)
            {
                if (e.BottomEdges is not { Count: > 0 } old) { e.BottomEdges = null; continue; }
                if (e.FadeMode == WallFadeMode.Default)
                {
                    e.FadeMode = WallFadeMode.Own;
                    e.Fade = new WallFade { Start = def.Start, Length = def.Length, Angle = def.Angle, Curve = def.Curve, Haze = def.Haze, HazeColor = def.HazeColor };
                }
                if (e.Fade.BottomEdges.Count == 0) e.Fade.BottomEdges.AddRange(old);
                e.BottomEdges = null;
            }
    }

    [Range(0, 9999)] public int Number { get; set; } = 1;
    [Ref(typeof(TileDef)), Tooltip("Where you arrive on this floor by default.")]
    public string EntryTile { get; set; } = "";
    [Multiline] public string Theme { get; set; } = "";

    [Hidden, Tooltip("Bottom clouds: animated layers behind every tile on this floor (cliffs fade down into them). First = furthest back.")]
    public List<CloudLayer> Clouds
    {
        get => _clouds;
        set { _clouds = value ?? new(); _cloudsAreDefault = false; }
    }
    private List<CloudLayer> _clouds = new() { new CloudLayer { Id = "cloud_1", Name = "Cloud sea", Texture = CloudStack.DefaultTexture, Mirror = true } };
    private bool _cloudsAreDefault = true;

    [Hidden, Tooltip("Top clouds: drawn after (in front of) the bottom clouds, still behind the tile art. Walls fade up into them.")]
    public List<CloudLayer> TopClouds { get; set; } = new();

    // Old floor files had two cloud bands; they become cloud layers when loaded (and vanish on save).
    [Hidden, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CloudLayerDef? LowerClouds { get => null; set => MigrateLegacy(value, "Lower clouds"); }
    [Hidden, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CloudLayerDef? UpperClouds { get => null; set => MigrateLegacy(value, "Upper clouds", top: true); }

    private void MigrateLegacy(CloudLayerDef? old, string name, bool top = false)
    {
        if (old is null) return;
        if (!top && _cloudsAreDefault) { _clouds.Clear(); _cloudsAreDefault = false; }
        if (!old.Enabled) return;
        (top ? TopClouds : _clouds).Add(new CloudLayer
        {
            Id = (top ? "top_cloud_" : "cloud_") + ((top ? TopClouds : _clouds).Count + 1), Name = name, Texture = old.Texture, Tint = old.Tint,
            Opacity = old.Opacity, Speed = old.Speed, Direction = 0, Mirror = true,
        });
    }

    [Group("Look"), Range(0, 64), Tooltip("Default soft edge (pixels) for painted regions.")]
    public int DefaultFeather { get; set; } = 6;
    [Group("Look")] public string BackgroundColor { get; set; } = "#1a1d2e";
    [Group("Look"), Tooltip("Not wired up yet.")] public string AmbientTint { get; set; } = "#ffffff";
    [Group("Look"), Tooltip("Not wired up yet.")] public string FogColor { get; set; } = "#000000";
    [Group("Look"), Tooltip("Not wired up yet.")] public string Music { get; set; } = "";
    [Group("Look"), Ref(typeof(Combat.EncounterDef)), Tooltip("Not wired up yet.")]
    public List<string> DefaultEncounters { get; set; } = new();

    [Tooltip("The floor's texture library. Every tile on this floor can paint with these.")]
    public List<MaterialDef> Materials { get; set; } = new();

    [Hidden, Ref(typeof(PrefabDef)), Tooltip("Prefabs this floor uses (its shortlist on the Walls page).")]
    public List<string> Prefabs { get; set; } = new();
    [Hidden, Tooltip("How walls going down fade into the bottom clouds on this floor (placements can override).")]
    public WallFade WallDownFade { get; set; } = new();
    [Hidden, Tooltip("How walls going up fade into the top clouds on this floor (placements can override).")]
    public WallFade WallUpFade { get; set; } = new();
    [Hidden, Tooltip("Walls going down (cliffs) on this floor. The first is drawn automatically along every edge; the others are painted onto cells.")]
    public List<FloorWall> WallsDown { get; set; } = new();
    [Hidden, Tooltip("Walls going up on this floor. The first is drawn automatically; the others are painted onto cells.")]
    public List<FloorWall> WallsUp { get; set; } = new();
    [Hidden, Tooltip("Ground on walkable cells: the first covers every walkable cell automatically; others are painted onto cells.")]
    public List<FloorGround> GroundWalkable { get; set; } = new();
    [Hidden, Tooltip("Ground on unwalkable cells: only where painted.")]
    public List<FloorGround> GroundUnwalkable { get; set; } = new();
    [Hidden, Tooltip("Kept in the Archive section of lists (the original game's content).")]
    public bool Archived { get; set; }
}
