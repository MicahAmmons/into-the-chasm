using System.Numerics;
using Chasm.Core.Animation;
using Chasm.Core.Combat;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.Progression;
using Chasm.Core.Rules;
using Chasm.Core.World;
using Chasm.Game.Animation;
using Chasm.Game.Combat;
using Chasm.Game.Rendering;
using Chasm.Game.Ui;
using Microsoft.Xna.Framework.Graphics;
using XColor = Microsoft.Xna.Framework.Color;
using XRect = Microsoft.Xna.Framework.Rectangle;
using XVec = Microsoft.Xna.Framework.Vector2;

namespace Chasm.Game.World;

/// <summary>World gameplay beyond walking: monster groups, fights, the Figure, the quick bar.</summary>
public partial class WorldScene
{
    // ------------------------------------------------------------------ monster groups

    private sealed class GroupRuntime(MonsterGroupPlacement p, AnimationPlayer anim)
    {
        public MonsterGroupPlacement P { get; } = p;
        public AnimationPlayer Anim { get; } = anim;
        public Vector2 Pos { get; set; } = IsoGrid.CellCenter(p.Cell);
        public List<Vector2> Path { get; } = new();
        public int Index { get; set; }
        public int Dir { get; set; } = 1;
        public float Wait { get; set; } = p.PauseSeconds;
        public Facing Facing { get; set; } = Facing.Left;
    }

    private readonly Dictionary<string, GroupRuntime> _groups = new();
    private string? _contactSuppressed; // group just fled from: no contact until the player steps away

    private void ResetGroups() { _groups.Clear(); _contactSuppressed = null; }

    private GroupRuntime Group(MonsterGroupPlacement p)
    {
        if (_groups.TryGetValue(p.Id, out var g) && g.P == p) return g;
        string? anim = null;
        if (Session.Defs.TryGet<EncounterDef>(p.Encounter, out var enc) && enc.Monsters.FirstOrDefault() is { } first &&
            Session.Defs.TryGet<MonsterDef>(first.Monster, out var m)) anim = m.Animation;
        Session.Defs.TryGet<AnimationDef>(anim, out var def);
        g = new GroupRuntime(p, new AnimationPlayer(def) { PlaceholderSize = new(90, 70) });
        _groups[p.Id] = g;
        return g;
    }

    public Vector2 PositionOf(Placement p) => p is MonsterGroupPlacement g ? Group(g).Pos : IsoGrid.CellCenter(p.Cell);
    public CellCoord CellOf(Placement p) => p is MonsterGroupPlacement g ? IsoGrid.CellAt(Group(g).Pos) : p.Cell;

    private string GroupLabel(MonsterGroupPlacement g)
    {
        if (!Session.Defs.TryGet<EncounterDef>(g.Encounter, out var enc)) return "Monsters";
        return string.Join(", ", enc.Monsters.Select(s => (Session.Defs.TryGet<MonsterDef>(s.Monster, out var m) ? m.DisplayName : s.Monster) + (s.Count > 1 ? $" x{s.Count}" : "")));
    }

    private bool DefeatedToday(Placement p)
    {
        int day = Session.World.Get(WorldState.PlacementKey(Tile.Id, p.Id, "defeated_day"));
        return day != 0 && day >= Session.Clock.Day;
    }

    private void UpdateGroups(float dt)
    {
        foreach (var p in ActivePlacements.OfType<MonsterGroupPlacement>().ToList())
        {
            var g = Group(p);
            g.Anim.Update(dt);
            var points = new List<CellCoord> { p.Cell };
            points.AddRange(p.PatrolPoints);
            if (p.Patrol == PatrolMode.Stay || points.Count < 2) { g.Anim.Play(AnimState.Idle); continue; }

            if (g.Path.Count == 0)
            {
                g.Anim.Play(AnimState.Idle);
                g.Wait -= dt;
                if (g.Wait > 0) continue;
                g.Wait = p.PauseSeconds;
                int next = g.Index + g.Dir;
                if (p.Patrol == PatrolMode.Loop) next = (g.Index + 1) % points.Count;
                else if (next < 0 || next >= points.Count) { g.Dir = -g.Dir; next = g.Index + g.Dir; }
                g.Index = Math.Clamp(next, 0, points.Count - 1);
                g.Path.AddRange(FreePathfinder.FindPath(g.Pos, IsoGrid.CellCenter(points[g.Index]), Tile.IsWalkable).Waypoints);
            }
            float budget = p.WalkSpeed * dt;
            while (budget > 0 && g.Path.Count > 0)
            {
                var to = g.Path[0] - g.Pos;
                float len = to.Length();
                if (len > 0.5f)
                {
                    if (MathF.Abs(to.X) > 0.5f) g.Facing = to.X < 0 ? Facing.Left : Facing.Right;
                    g.Anim.Play(to.Y < 0 ? AnimState.WalkUp : AnimState.WalkDown);
                }
                if (len <= budget) { g.Pos = g.Path[0]; g.Path.RemoveAt(0); budget -= len; }
                else { g.Pos += to / len * budget; budget = 0; }
            }
        }
    }

    private void CheckGroupContact()
    {
        var cell = Player.Cell;
        if (_contactSuppressed is not null && ActivePlacements.OfType<MonsterGroupPlacement>().FirstOrDefault(g => g.Id == _contactSuppressed) is { } sup
            && CellOf(sup).ManhattanTo(cell) > 1) _contactSuppressed = null;
        foreach (var p in ActivePlacements.OfType<MonsterGroupPlacement>())
            if (p.Aggressive && p.Id != _contactSuppressed && CellOf(p).ManhattanTo(cell) <= 0)
            {
                Player.Stop();
                StartFight(p.Encounter, p);
                return;
            }
    }

    private void AskToFight(MonsterGroupPlacement p)
    {
        var prog = Session.Progress;
        bool anyReady = prog.Summons.Values.Any(s => s.UsesLeft > 0);
        string q = $"Fight {GroupLabel(p)}?";
        if (!anyReady) q += prog.Summons.Count == 0 ? "\n(You have no summons!)" : "\n(None of your summons have uses left. Rest first?)";
        Confirm(q, yes => { if (yes) StartFight(p.Encounter, p); });
    }

    public void StartEncounter(string encounterId) => StartFight(encounterId, null);

    private void StartFight(string encounterId, MonsterGroupPlacement? group)
    {
        if (!Session.Defs.TryGet<EncounterDef>(encounterId, out var enc)) { ShowMessage(new[] { $"Encounter '{encounterId}' doesn't exist." }, null); return; }
        var cfg = Session.Config;
        var prog = Session.Progress;
        var policy = new ProgressSummonPolicy(prog, Session.Defs);
        SaveNow(fightInProgress: encounterId); // quitting mid-fight counts as a loss on next load
        Services.Scenes.Push(new CombatScene(new CombatRequest
        {
            Tile = Tile,
            Enemies = enc.Monsters.SelectMany(s => Enumerable.Repeat(s.Monster, s.Count)).ToList(),
            PlayerMaxHp = prog.MaxHp(cfg), PlayerHp = prog.Hp_(cfg), PlayerAp = prog.MaxAp(cfg), PlayerMp = prog.MaxMp(cfg),
            PlayerInitiative = cfg.PlayerInitiative, Summons = policy, CanFlee = enc.CanFlee,
            Items = Session.Inventory.Items
                .Select(kv => Session.Defs.TryGet<ItemDef>(kv.Key, out var it) && !string.IsNullOrEmpty(it.CombatAbility)
                    ? new CombatItem(it.Id, it.CombatAbility, kv.Value, it.ConsumedInCombat) : null)
                .Where(i => i is not null).Cast<CombatItem>().ToList(),
            PlayerWorldCell = Player.Cell, Stats = Session.Stats,
            OnFinished = (outcome, engine) => AfterFight(enc, group, outcome, engine),
        }));
    }

    private void AfterFight(EncounterDef enc, MonsterGroupPlacement? group, CombatOutcome outcome, CombatEngine engine)
    {
        var msgs = CombatRewards.Apply(Session, engine, outcome);
        var ctx = Context(group);
        switch (outcome)
        {
            case CombatOutcome.Victory:
                if (group is not null)
                {
                    if (enc.Respawn == RespawnRule.Respawns)
                        Session.World.Set(WorldState.PlacementKey(Tile.Id, group.Id, "defeated_day"), Session.Clock.Day);
                    else Session.World.Set(WorldState.PlacementKey(Tile.Id, group.Id, "gone"), 1);
                }
                foreach (var o in enc.OnVictory) o.Execute(ctx);
                break;
            case CombatOutcome.Defeat:
                foreach (var o in enc.OnDefeat) o.Execute(ctx);
                Session.Rest();
                msgs.Insert(0, "Everything goes dark...");
                var prog = Session.Progress;
                if (!string.IsNullOrEmpty(prog.RespawnTile))
                {
                    msgs.Add("You wake beside the Figure.");
                    BeginChangeTo(prog.RespawnTile, prog.RespawnCell);
                }
                else
                {
                    msgs.Add("You wake up, sore but alive.");
                    BeginChangeTo(Tile.Id, Tile.Spawn);
                }
                break;
            case CombatOutcome.Fled:
                if (group is not null) _contactSuppressed = group.Id;
                break;
        }
        if (msgs.Count > 0) ShowMessage(msgs, null);
        SaveNow();
    }

    /// <summary>Called on load when the save says a fight was in progress: it's a loss.</summary>
    public void ResolveAbandonedFight()
    {
        Session.Stats.Add(Chasm.Core.Stats.StatKeys.CombatsLost);
        Session.Rest();
        var prog = Session.Progress;
        var msgs = new List<string> { "You left in the middle of a fight...", "Everything went dark." };
        if (!string.IsNullOrEmpty(prog.RespawnTile)) { msgs.Add("You wake beside the Figure."); BeginChangeTo(prog.RespawnTile, prog.RespawnCell); }
        ShowMessage(msgs, null);
    }

    private void BeginChangeTo(string tile, CellCoord cell) => ChangeTile(tile, cell);

    // ------------------------------------------------------------------ the Figure

    private void CheckFigures()
    {
        foreach (var f in ActivePlacements.OfType<FigurePlacement>())
        {
            if (f.Cell.ManhattanTo(Player.Cell) > f.EncounterRange) continue;
            var prog = Session.Progress;
            string key = $"{Tile.Id}/{f.Id}";
            if (!prog.KnownFigures.Contains(key)) prog.KnownFigures.Add(key);
            prog.RespawnTile = Tile.Id;
            prog.RespawnCell = NearestWalkable(f.Cell);
        }
    }

    private CellCoord NearestWalkable(CellCoord c) =>
        IsoGrid.AllCells.Where(Walkable).OrderBy(x => x.ManhattanTo(c)).ThenBy(x => x == c ? 1 : 0).FirstOrDefault(c);

    private void TalkToFigure(FigurePlacement f)
    {
        var options = new List<(string, Action)>
        {
            ("Talk", () =>
            {
                if (Rules.RunFirst(f.TalkRules, Context(f)) is null) ShowMessage(f.Greeting, f.Name);
            }),
        };
        if (f.OffersRest) options.Add(("Rest", AskRest));
        if (f.OffersFastTravel)
        {
            var others = Session.Progress.KnownFigures.Where(k => k != $"{Tile.Id}/{f.Id}").ToList();
            if (others.Count > 0) options.Add(("Travel to another Figure", () => TravelMenu(others)));
        }
        Services.Scenes.Push(new ChoiceScene(f.Name, options));
    }

    private void TravelMenu(List<string> figures)
    {
        var options = new List<(string, Action)>();
        foreach (var key in figures)
        {
            var parts = key.Split('/');
            if (!Session.Defs.TryGet<TileDef>(parts[0], out var t) || t.FindPlacement(parts[1]) is not FigurePlacement fig) continue;
            options.Add(($"Floor {t.Floor}: {t.DisplayName}", () => BeginChangeTo(t.Id, NearestWalkableIn(t, fig.Cell))));
        }
        Services.Scenes.Push(new ChoiceScene("Travel where?", options));
    }

    private static CellCoord NearestWalkableIn(TileDef t, CellCoord c) =>
        IsoGrid.AllCells.Where(t.IsWalkable).Where(x => x != c).OrderBy(x => x.ManhattanTo(c)).FirstOrDefault(c);

    // ------------------------------------------------------------------ quick bar

    private sealed record QuickSlot(string Label, string Icon, string Tooltip, bool Active, Action Use);

    private List<QuickSlot> QuickSlots()
    {
        var slots = new List<QuickSlot>();
        var prog = Session.Progress;
        foreach (var id in prog.Aspects)
        {
            if (!Session.Defs.TryGet<AspectDef>(id, out var a) || !a.Toggleable) continue;
            slots.Add(new QuickSlot(a.DisplayName, a.Icon, a.Description, prog.ActiveAspect == id, () => ToggleAspect(a)));
        }
        foreach (var (id, count) in Session.Inventory.Items)
        {
            if (!Session.Defs.TryGet<ItemDef>(id, out var item) || !item.ShowInQuickBar) continue;
            slots.Add(new QuickSlot(item.DisplayName + (count > 1 ? $" x{count}" : ""), item.Icon, item.Description, false, () => UseItem(item)));
        }
        return slots.Take(10).ToList();
    }

    private void ToggleAspect(AspectDef a)
    {
        var prog = Session.Progress;
        if (prog.ActiveAspect == a.Id)
        {
            prog.ActiveAspect = null;
            if (!string.IsNullOrWhiteSpace(a.OffMessage)) ShowMessage(new[] { a.OffMessage }, null);
            return;
        }
        prog.ActiveAspect = a.Id;
        if (!string.IsNullOrWhiteSpace(a.OnMessage)) ShowMessage(new[] { a.OnMessage }, null);
        Rules.RunFirst(a.OnActivate, Context());
        // Hidden spots that react to an active aspect re-check when it's switched on.
        foreach (var p in ActivePlacements.OfType<InteractablePlacement>().Where(p => p.Trigger == InteractTrigger.StepOn && p.Cell == Player.Cell).ToList())
            Interact(p);
    }

    private void UseItem(ItemDef item)
    {
        Session.Events.Publish(new ItemUsedEvent(item.Id));
        if (Rules.RunFirst(item.UseRules, Context()) is null) ShowMessage(new[] { item.NoUseMessage }, null);
    }

    private static XRect SlotRect(int i, int count) => new(960 - count * 37 + i * 74, 1000, 66, 66);

    private bool QuickBarClick(Vector2 point)
    {
        var slots = QuickSlots();
        for (int i = 0; i < slots.Count; i++)
            if (SlotRect(i, slots.Count).Contains((int)point.X, (int)point.Y)) { slots[i].Use(); return true; }
        return false;
    }

    private void UpdateQuickBarKeys()
    {
        var input = Services.Input;
        var slots = QuickSlots();
        for (int i = 0; i < slots.Count; i++)
            if (input.Pressed(Chasm.Game.Input.InputAction.Quick1 + i)) { slots[i].Use(); return; }
    }

    private void DrawQuickBar(SpriteBatch b)
    {
        var d = Services.Draw;
        var slots = QuickSlots();
        var mouse = Services.Input.MouseCanvas.ToPoint();
        for (int i = 0; i < slots.Count; i++)
        {
            var r = SlotRect(i, slots.Count);
            var s = slots[i];
            d.Rect(b, r, XColor.Black * 0.6f);
            var tex = Services.Textures.Get(s.Icon);
            if (Services.Textures.IsPlaceholder(tex)) d.Placeholder(b, new XRect(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6), s.Label);
            else b.Draw(tex, new XRect(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10), XColor.White);
            d.RectOutline(b, r, s.Active ? XColor.Gold : XColor.White * 0.4f, s.Active ? 3 : 1);
            d.Text(b, $"{(i + 1) % 10}", new XVec(r.X + 4, r.Y + 2), XColor.LightGray, 14);
            if (r.Contains(mouse))
            {
                d.TextCentered(b, s.Label, new XVec(r.Center.X, r.Y - 40), XColor.White, 20, bold: true);
                if (s.Tooltip.Length > 0) d.TextCentered(b, s.Tooltip, new XVec(r.Center.X, r.Y - 16), XColor.LightGray, 16);
            }
        }
    }

    private void DrawPlayerHud(SpriteBatch b)
    {
        var d = Services.Draw;
        var cfg = Session.Config;
        var prog = Session.Progress;
        int max = prog.MaxHp(cfg), hp = prog.Hp_(cfg);
        var bar = new XRect(20, 20, 260, 20);
        d.Rect(b, bar, XColor.Black * 0.6f);
        d.Rect(b, new XRect(bar.X, bar.Y, (int)(bar.Width * hp / (float)Math.Max(1, max)), bar.Height), new XColor(90, 200, 120));
        d.RectOutline(b, bar, XColor.White * 0.5f);
        d.TextShadowed(b, $"HP {hp}/{max}   AP {prog.MaxAp(cfg)}   MP {prog.MaxMp(cfg)}", new XVec(24, 44), XColor.White, 18);
        int y = 70;
        foreach (var (id, sp) in prog.Summons)
        {
            if (!Session.Defs.TryGet<MonsterDef>(id, out var m)) continue;
            d.TextShadowed(b, $"{m.DisplayName}: {sp.UsesLeft}/{prog.DailyUses(m)} uses  (affinity {PlayerProgress.AffinityLevel(m, sp.AffinityXp)})", new XVec(24, y), new XColor(170, 210, 255), 16);
            y += 22;
        }
        if (prog.ActiveAspect is { } act && Session.Defs.TryGet<AspectDef>(act, out var asp))
            d.TextShadowed(b, $"Aspect active: {asp.DisplayName}", new XVec(24, y), XColor.Gold, 16);
    }

    // ------------------------------------------------------------------ drawing groups + figures

    private void DrawGroupsAndFigures()
    {
        var s = Services;
        foreach (var p in ActivePlacements)
        {
            switch (p)
            {
                case MonsterGroupPlacement g:
                    var rt = Group(g);
                    rt.Anim.Draw(_queue, s.Textures, s.Draw, rt.Pos, rt.Facing, p == Hovered ? new XColor(255, 220, 220) : XColor.White, GroupLabel(g));
                    if (Session.Defs.TryGet<EncounterDef>(g.Encounter, out var enc) && enc.Monsters.Sum(m => m.Count) > 1)
                    {
                        var pos = rt.Pos;
                        int n = enc.Monsters.Sum(m => m.Count);
                        _queue.Add(RenderLayer.WorldUi, 0, b => s.Draw.TextCentered(b, $"x{n}", new XVec(pos.X + 40, pos.Y - 10), new XColor(255, 150, 140), 20, bold: true));
                    }
                    if (p == Hovered)
                    {
                        var pos = rt.Pos;
                        string label = "Fight: " + GroupLabel(g);
                        _queue.Add(RenderLayer.WorldUi, 1, b => s.Draw.TextCentered(b, label, new XVec(pos.X, pos.Y - 110), new XColor(255, 170, 160), 22, bold: true));
                    }
                    break;
                case InteractablePlacement { Invisible: false } ip when !string.IsNullOrEmpty(ip.Animation):
                    s.Defs.TryGet<AnimationDef>(ip.Animation, out var ia);
                    if (!_figureAnims.TryGetValue(ip.Id, out var ipl) || ipl.Def != ia)
                        _figureAnims[ip.Id] = ipl = new AnimationPlayer(ia) { PlaceholderSize = new(90, 80) };
                    ipl.Update(1 / 60f);
                    ipl.Draw(_queue, s.Textures, s.Draw, IsoGrid.CellCenter(ip.Cell), Facing.Left, p == Hovered ? new XColor(255, 255, 220) : XColor.White, ip.Label);
                    if (p == Hovered)
                    {
                        var pos = IsoGrid.CellCenter(ip.Cell);
                        _queue.Add(RenderLayer.WorldUi, 1, b => s.Draw.TextCentered(b, ip.Label, new XVec(pos.X, pos.Y - 100), XColor.White, 22, bold: true));
                    }
                    break;
                case FigurePlacement f:
                    s.Defs.TryGet<AnimationDef>(f.Animation, out var fa);
                    if (!_figureAnims.TryGetValue(f.Id, out var player) || player.Def != fa)
                        _figureAnims[f.Id] = player = new AnimationPlayer(fa) { PlaceholderSize = new(70, 120) };
                    player.Update(1 / 60f);
                    player.Draw(_queue, s.Textures, s.Draw, IsoGrid.CellCenter(f.Cell), Facing.Left, XColor.White, f.Name);
                    if (p == Hovered)
                    {
                        var pos = IsoGrid.CellCenter(f.Cell);
                        _queue.Add(RenderLayer.WorldUi, 1, b => s.Draw.TextCentered(b, f.Name, new XVec(pos.X, pos.Y - 140), XColor.White, 22, bold: true));
                    }
                    break;
            }
        }
    }

    private readonly Dictionary<string, AnimationPlayer> _figureAnims = new();
}
