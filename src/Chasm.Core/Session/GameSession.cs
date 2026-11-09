using System.Numerics;
using Chasm.Core.Content;
using Chasm.Core.Events;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.Rules;
using Chasm.Core.Stats;
using Chasm.Core.World;

namespace Chasm.Core.Session;

/// <summary>
/// One playthrough's live state: world flags, inventory, stats, clock, where the player is.
/// Sandboxes create throwaway sessions; the real game loads one from a save slot.
/// </summary>
public sealed class GameSession
{
    public DefRegistry Defs { get; }
    public EventBus Events { get; } = new();
    public WorldState World { get; } = new();
    public Inventory Inventory { get; } = new();
    public StatsTracker Stats { get; } = new();
    public DayClock Clock { get; } = new();
    public Progression.PlayerProgress Progress { get; private set; } = new();

    public string CurrentTile { get; set; } = "";
    public Vector2 PlayerPosition { get; set; }

    public GameConfigDef Config =>
        Defs.TryGet<GameConfigDef>(GameConfigDef.MainId, out var c) ? c : new GameConfigDef { Id = GameConfigDef.MainId };

    public GameSession(DefRegistry defs)
    {
        Defs = defs;
        Stats.Attach(Events);
        ApplyConfig();
    }

    public void ApplyConfig()
    {
        Clock.DayLengthSeconds = Config.DayLengthMinutes * 60f;
        Clock.NightStartsAt = Config.NightStartsAt;
    }

    public RuleContext CreateContext(IRuleHost? host) => new()
    {
        Defs = Defs, World = World, Inventory = Inventory, Stats = Stats, Events = Events, Clock = Clock, Progress = Progress, Host = host,
        CurrentTileId = CurrentTile, PlayerCell = IsoGrid.CellAt(PlayerPosition),
    };

    /// <summary>Half a day passes; summons' uses and the player's HP refill.</summary>
    public void Rest()
    {
        Clock.Rest();
        Progress.Rest(Defs);
        Stats.Add(StatKeys.Rests);
    }

    public SaveData ToSave() => new()
    {
        Progress = Progress,
        Tile = CurrentTile, PlayerX = PlayerPosition.X, PlayerY = PlayerPosition.Y,
        Flags = World.Snapshot(), Items = Inventory.Snapshot(), Gold = Inventory.Gold,
        Stats = Stats.Snapshot(), Day = Clock.Day, TimeOfDay = Clock.Time,
    };

    public void LoadSave(SaveData s)
    {
        CurrentTile = s.Tile;
        PlayerPosition = new Vector2(s.PlayerX, s.PlayerY);
        World.Load(s.Flags);
        Inventory.Load(s.Items, s.Gold);
        Stats.Load(s.Stats);
        Clock.Load(s.Day, s.TimeOfDay);
        Progress = s.Progress ?? new();
    }
}

/// <summary>Everything written to a save slot. Grows as systems are added (summons, quests...).</summary>
public sealed class SaveData
{
    public int Version { get; set; } = 1;
    public string Tile { get; set; } = "";
    public float PlayerX { get; set; }
    public float PlayerY { get; set; }
    public Dictionary<string, int> Flags { get; set; } = new();
    public Dictionary<string, int> Items { get; set; } = new();
    public int Gold { get; set; }
    public Dictionary<string, double> Stats { get; set; } = new();
    public int Day { get; set; } = 1;
    public float TimeOfDay { get; set; }
    public Progression.PlayerProgress? Progress { get; set; }
}
