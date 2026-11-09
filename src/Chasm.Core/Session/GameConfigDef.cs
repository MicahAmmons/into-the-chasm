using Chasm.Core.Animation;
using Chasm.Core.Content;
using Chasm.Core.World;

namespace Chasm.Core.Session;

/// <summary>Global tuning knobs. There is one, with id "game".</summary>
[DefFolder("config", "Game Config")]
[Tooltip("Global settings. Keep a single entry with id 'game'.")]
public sealed class GameConfigDef : Def
{
    public const string MainId = "game";

    [Ref(typeof(TileDef)), Tooltip("Where a new game begins.")]
    public string StartTile { get; set; } = "";

    [Ref(typeof(AnimationDef)), Tooltip("The player's animations.")]
    public string PlayerAnimation { get; set; } = "";

    [Range(10, 600), Tooltip("Out-of-combat walking speed, pixels per second.")]
    public float PlayerWalkSpeed { get; set; } = 220;

    [Range(0.5, 120), Tooltip("Minutes of real time per in-game day.")]
    public float DayLengthMinutes { get; set; } = 10;

    [Range(0, 1), Tooltip("Fraction of the day when night begins (0.65 = last 35% is night).")]
    public float NightStartsAt { get; set; } = 0.65f;

    [Range(0, 5), Tooltip("How many cells away the player can interact with objects.")]
    public int InteractRange { get; set; } = 1;
}
