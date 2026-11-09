using Chasm.Core.Combat;
using Chasm.Core.Stats;
using Chasm.Core.World;
using Chasm.Game.Combat;
using Chasm.Game.Scenes;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Num = System.Numerics;

namespace Chasm.Tools.Scenes;

/// <summary>
/// Set up any fight and play it: choose the tile, the enemies (or load an encounter), your stats
/// and summons. Watch AI vs AI at high speed to balance monsters. Results and stats are shown after.
/// </summary>
public sealed class CombatSandboxScene : Scene
{
    private string _tile = "";
    private string _encounter = "";
    private readonly List<string> _enemies = new() { "frost_ooze", "fire_ooze" };
    private readonly List<string> _summons = new() { "turtle" };
    private int _hp = 20, _ap = 6, _mp = 3, _init = 10, _maxPerType = 1, _seed;
    private bool _randomSeed = true, _canFlee = true, _aiVsAi;
    private float _speed = 1f;
    private readonly StatsTracker _stats = new();
    private string _lastResult = "";
    private List<string> _lastLog = new();
    private string _addEnemy = "", _addSummon = "";
    private bool _autoStartPending;

    public CombatSandboxScene(string? startArg = null)
    {
        // "--mode combat --arg fight" starts at once; "--arg aifight" starts AI vs AI at 3x.
        _autoStartPending = startArg is "fight" or "aifight";
        if (startArg == "aifight") { _aiVsAi = true; _speed = 3f; }
    }

    public override void OnEnter()
    {
        ToolContext.EnsureInitialized(Services);
        _tile = Services.Defs.All<TileDef>().FirstOrDefault(t => t.PlayerCombatStarts.Count > 0)?.Id ?? "";
    }

    public override void Update(float dt)
    {
        if (!IsTop) return;
        if (_autoStartPending) { _autoStartPending = false; Start(); }
        ToolContext.MenuBar(Services, "Combat Sandbox");
        var insp = ToolContext.Wizards.Inspector;
        ImGui.SetNextWindowPos(new Num.Vector2(20, 40), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Num.Vector2(560, 880), ImGuiCond.FirstUseEver);
        ImGui.Begin("Combat setup");

        ImGui.SeparatorText("Battlefield");
        ImGui.Text("Tile"); ImGui.SameLine(120); ImGui.SetNextItemWidth(-1);
        insp.RefPicker(typeof(TileDef), ref _tile);
        if (Services.Defs.TryGet<TileDef>(_tile, out var tile) && tile.PlayerCombatStarts.Count == 0)
            ImGui.TextColored(new Num.Vector4(1, 0.7f, 0.4f, 1), "No combat start cells painted; positions will be picked automatically.");

        ImGui.SeparatorText("Enemies");
        ImGui.Text("Encounter"); ImGui.SameLine(120); ImGui.SetNextItemWidth(-80);
        insp.RefPicker(typeof(EncounterDef), ref _encounter);
        ImGui.SameLine();
        if (ImGui.Button("Load") && Services.Defs.TryGet<EncounterDef>(_encounter, out var enc))
        {
            _enemies.Clear();
            foreach (var slot in enc.Monsters) for (int i = 0; i < slot.Count; i++) _enemies.Add(slot.Monster);
            _canFlee = enc.CanFlee;
        }
        MonsterList(_enemies, ref _addEnemy, "enemies");

        ImGui.SeparatorText("You");
        ImGui.SetNextItemWidth(120); ImGui.InputInt("HP", ref _hp); ImGui.SameLine();
        ImGui.SetNextItemWidth(120); ImGui.InputInt("Initiative", ref _init);
        ImGui.SetNextItemWidth(120); ImGui.InputInt("AP", ref _ap); ImGui.SameLine();
        ImGui.SetNextItemWidth(120); ImGui.InputInt("MP", ref _mp);
        ImGui.Text("Summons you can use:");
        MonsterList(_summons, ref _addSummon, "summons");
        ImGui.SetNextItemWidth(120); ImGui.InputInt("Max of each summon on the field", ref _maxPerType);
        ImGui.Checkbox("Can flee", ref _canFlee);

        ImGui.SeparatorText("Run");
        ImGui.Checkbox("AI plays my side too (AI vs AI)", ref _aiVsAi);
        ImGui.SetNextItemWidth(200); ImGui.SliderFloat("Speed", ref _speed, 0.25f, 8f, "%.2fx");
        ImGui.Checkbox("Random seed", ref _randomSeed);
        if (!_randomSeed) { ImGui.SameLine(); ImGui.SetNextItemWidth(120); ImGui.InputInt("Seed", ref _seed); }
        bool canStart = tile is not null && _enemies.Count > 0;
        if (!canStart) ImGui.BeginDisabled();
        if (ImGui.Button("FIGHT!", new Num.Vector2(-1, 44))) Start();
        if (!canStart) ImGui.EndDisabled();

        if (_lastResult.Length > 0)
        {
            ImGui.SeparatorText("Last fight");
            ImGui.TextWrapped(_lastResult);
            if (ImGui.CollapsingHeader("Event log"))
            {
                ImGui.BeginChild("log", new Num.Vector2(0, 220), ImGuiChildFlags.Borders);
                foreach (var line in _lastLog) ImGui.TextUnformatted(line);
                ImGui.EndChild();
            }
            if (ImGui.CollapsingHeader("Stats from sandbox fights"))
                foreach (var (k, v) in _stats.Values) ImGui.Text($"{k}: {v:0.##}");
        }
        ImGui.End();
        ToolContext.Wizards.Draw();
    }

    private void MonsterList(List<string> list, ref string add, string id)
    {
        ImGui.PushID(id);
        int remove = -1;
        foreach (var group in list.Select((m, i) => (m, i)).GroupBy(x => x.m))
        {
            string name = Services.Defs.TryGet<MonsterDef>(group.Key, out var m) ? m.DisplayName : group.Key + " (missing!)";
            ImGui.PushID(group.Key);
            if (ImGui.SmallButton("-")) remove = group.Last().i;
            ImGui.SameLine();
            if (ImGui.SmallButton("+")) { list.Add(group.Key); ImGui.PopID(); break; }
            ImGui.SameLine();
            ImGui.Text($"{group.Count()}x {name}");
            if (m is not null) { ImGui.SameLine(); if (ImGui.SmallButton("edit")) ToolContext.Wizards.OpenEdit(m); }
            ImGui.PopID();
        }
        if (remove >= 0) list.RemoveAt(remove);
        ImGui.SetNextItemWidth(-60);
        ToolContext.Wizards.Inspector.RefPicker(typeof(MonsterDef), ref add);
        ImGui.SameLine();
        if (ImGui.Button("Add") && add.Length > 0) list.Add(add);
        ImGui.PopID();
    }

    private void Start()
    {
        if (!Services.Defs.TryGet<TileDef>(_tile, out var tile)) return;
        var scene = new CombatScene(new CombatRequest
        {
            Tile = tile, Enemies = _enemies.Where(e => Services.Defs.Exists(typeof(MonsterDef), e)).ToList(),
            PlayerHp = _hp, PlayerMaxHp = _hp, PlayerAp = _ap, PlayerMp = _mp, PlayerInitiative = _init,
            Summons = new UnlimitedSummons(_summons.Distinct(), Math.Max(1, _maxPerType)), CanFlee = _canFlee,
            Seed = _randomSeed ? null : _seed, Stats = _stats,
            OnFinished = (outcome, engine) =>
            {
                _lastResult = $"{outcome} after {engine.Round} round(s). You ended with {engine.Player.Hp}/{engine.Player.MaxHp} HP.";
                _lastLog = engine.Log.Select(e => Describe(engine, e)).Where(s => s.Length > 0).ToList();
            },
        })
        {
            AutoPlayPlayerSide = _aiVsAi,
            Speed = _speed,
            DebugGui = FightControls,
        };
        Services.Scenes.Push(scene);
    }

    private void FightControls(CombatScene scene)
    {
        ImGui.SetNextWindowPos(new Num.Vector2(ImGui.GetIO().DisplaySize.X - 300, 120), ImGuiCond.FirstUseEver);
        ImGui.Begin("Fight controls", ImGuiWindowFlags.AlwaysAutoResize);
        float sp = scene.Speed;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderFloat("Speed", ref sp, 0.25f, 8f, "%.2fx")) scene.Speed = sp;
        bool auto = scene.AutoPlayPlayerSide;
        if (ImGui.Checkbox("AI plays my side", ref auto)) scene.AutoPlayPlayerSide = auto;
        if (scene.Engine is { } e)
        {
            ImGui.Text($"Round {e.Round}");
            if (ImGui.Button("Give everyone on my side full HP"))
                foreach (var u in e.Alive.Where(u => u.Team == Team.Player)) u.Hp = u.MaxHp;
        }
        if (ImGui.Button("Abort fight")) Services.Scenes.Pop();
        ImGui.End();
    }

    private static string Describe(CombatEngine e, CombatEvent ev)
    {
        string N(int id) => e.Unit(id).Name;
        return ev switch
        {
            TurnStartedEvent t => $"-- Round {t.Round}: {N(t.Unit)}'s turn",
            MovedEvent m => $"{N(m.Unit)} moves {m.Path.Count} cell(s)",
            AbilityUsedEvent a => $"{N(a.Unit)} uses {a.Ability}",
            DamagedEvent d => $"   {N(d.Unit)} takes {d.Amount} {d.Element} damage ({d.HpAfter} left)",
            HealedEvent h => $"   {N(h.Unit)} heals {h.Amount}",
            StatusAppliedEvent s => $"   {N(s.Unit)} gains {s.Status} ({s.Turns})",
            StatusExpiredEvent x => $"   {N(x.Unit)} loses {x.Status}",
            DiedEvent dd => $"   {N(dd.Unit)} is defeated!",
            SummonedEvent sm => $"{N(sm.Summoner)} summons {N(sm.Unit)}",
            CombatEndedEvent c => $"== {c.Outcome} ==",
            _ => "",
        };
    }

    public override void Draw(SpriteBatch batch)
    {
        batch.Begin();
        Services.Draw.TextCentered(batch, "Combat Sandbox", new Vector2(1200, 500), Color.White * 0.3f, 60, bold: true);
        batch.End();
    }
}
