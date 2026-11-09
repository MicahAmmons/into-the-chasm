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
    [Range(0, 9999), Tooltip("Which floor this Tile belongs to.")]
    public int Floor { get; set; } = 1;

    [Tooltip("Drawn in order; first is furthest back.")]
    public List<TileLayerDef> Layers { get; set; } = new();

    [Tooltip("Colour behind all layers, as #RRGGBB.")]
    public string BackgroundColor { get; set; } = "#1a1d2e";

    public List<TileExitDef> Exits { get; set; } = new();

    [Tooltip("Where the player appears when arriving by teleport / new game.")]
    public CellCoord Spawn { get; set; } = new(16, 1);

    public List<CellCoord> PlayerCombatStarts { get; set; } = new();
    public List<CellCoord> EnemyCombatStarts { get; set; } = new();

    /// <summary>
    /// Walkability as an ASCII picture of the screen: one string per cell row (top to bottom),
    /// '.' walkable, '#' blocked, ' ' between cells. Readable in a text editor and in git diffs.
    /// </summary>
    [Hidden]
    public List<string> WalkMap { get; set; } = new();

    [Tooltip("Objects placed on cells: items, interactables, NPCs, monster groups...")]
    public List<Placement> Placements { get; set; } = new();

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
    }
}

/// <summary>A numbered floor: groups Tiles and holds floor-wide look and feel.</summary>
[DefFolder("floors", "Floors")]
[Tooltip("A numbered floor of the chasm.")]
public sealed class FloorDef : Def
{
    [Range(0, 9999)] public int Number { get; set; } = 1;
    [Ref(typeof(TileDef)), Tooltip("Where you arrive on this floor by default.")]
    public string EntryTile { get; set; } = "";
    [Tooltip("Tint of the cloud layer separating floors, #RRGGBB.")]
    public string CloudColor { get; set; } = "#c8d2e6";
    [Multiline] public string Theme { get; set; } = "";
}
