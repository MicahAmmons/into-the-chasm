using Chasm.Core.Content;
using Chasm.Core.Events;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.Stats;
using Chasm.Core.World;

namespace Chasm.Core.Rules;

/// <summary>
/// Things only the running game can do (show a message box, move to another Tile, start a fight).
/// Core rules call these; the World scene implements them; tests use a recording fake.
/// </summary>
public interface IRuleHost
{
    void ShowMessage(IReadOnlyList<string> lines, string? speaker);
    void ChangeTile(string tileId, CellCoord? arrivalCell);
    void StartEncounter(string encounterId);
    void StartDialogue(string dialogueId);
    void Rest();
    /// <summary>Asks the player a yes/no question; the answer arrives later.</summary>
    void Confirm(string question, Action<bool> answered);
}

/// <summary>Everything a condition can read and an outcome can change.</summary>
public sealed class RuleContext
{
    public required DefRegistry Defs { get; init; }
    public required WorldState World { get; init; }
    public required Inventory Inventory { get; init; }
    public required StatsTracker Stats { get; init; }
    public required EventBus Events { get; init; }
    public required Session.DayClock Clock { get; init; }
    public required Progression.PlayerProgress Progress { get; init; }
    public IRuleHost? Host { get; init; }

    /// <summary>Where the player is right now (for "player is in tile X" / "near cell Y").</summary>
    public string CurrentTileId { get; set; } = "";
    public CellCoord PlayerCell { get; set; }

    /// <summary>The tile placement that triggered this rule, if any (lets outcomes say "this one").</summary>
    public string? SourcePlacementId { get; set; }

    public bool InCombat { get; set; }
}
