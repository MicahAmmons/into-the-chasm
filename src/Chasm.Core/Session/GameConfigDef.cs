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

    [Range(0, 600), Tooltip("Placeholder: in-game minutes that pass per fight (real-time minutes of the day cycle).")]
    public float MinutesPerFight { get; set; } = 1;

    [Range(0, 1), Tooltip("Fraction of the day when night begins (0.65 = last 35% is night).")]
    public float NightStartsAt { get; set; } = 0.65f;

    [Tooltip("Runs once when a new game starts (opening message, starting items...).")]
    public List<Rules.Outcome> NewGameOutcomes { get; set; } = new();

    [Range(0, 5), Tooltip("How many cells away the player can interact with objects.")]
    public int InteractRange { get; set; } = 1;

    [Group("Player starting stats"), Range(1, 9999)] public int PlayerBaseHp { get; set; } = 20;
    [Group("Player starting stats"), Range(0, 99)] public int PlayerBaseAp { get; set; } = 6;
    [Group("Player starting stats"), Range(0, 99)] public int PlayerBaseMp { get; set; } = 3;
    [Group("Player starting stats"), Range(0, 999)] public int PlayerInitiative { get; set; } = 10;

    [Group("Growth"), Range(1, 100000), Tooltip("XP for the first level of HP / AP / MP.")] public int XpForFirstLevel { get; set; } = 20;
    [Group("Growth"), Range(1, 10), Tooltip("Each next level needs this many times more XP.")] public float XpGrowth { get; set; } = 1.5f;
    [Group("Growth"), Range(1, 100)] public int HpPerLevel { get; set; } = 2;
    [Group("Growth"), Tooltip("HP XP per point of damage taken in a fight you WIN.")] public float HpXpPerDamage { get; set; } = 1;
    [Group("Growth"), Tooltip("AP XP per AP spent in combat.")] public float ApXpPerPoint { get; set; } = 1;
    [Group("Growth"), Tooltip("MP XP per cell walked in combat.")] public float MpXpPerCell { get; set; } = 1;
}
