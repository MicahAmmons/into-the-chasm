using System.Numerics;
using Chasm.Core.Animation;
using Chasm.Core.Combat;
using Chasm.Core.Grid;
using Chasm.Core.Stats;
using Chasm.Core.World;
using Chasm.Game.Animation;
using Chasm.Game.Input;
using Chasm.Game.Rendering;
using Chasm.Game.Scenes;
using Chasm.Game.World;
using Microsoft.Xna.Framework.Graphics;
using XColor = Microsoft.Xna.Framework.Color;
using XRect = Microsoft.Xna.Framework.Rectangle;
using XVec = Microsoft.Xna.Framework.Vector2;

namespace Chasm.Game.Combat;

/// <summary>What a fight needs from whoever starts it (the world, or the combat sandbox).</summary>
public sealed class CombatRequest
{
    public required TileDef Tile { get; init; }
    public required List<string> Enemies { get; init; }
    public int PlayerMaxHp { get; init; } = 20;
    public int PlayerHp { get; init; } = 20;
    public int PlayerAp { get; init; } = 6;
    public int PlayerMp { get; init; } = 3;
    public int PlayerInitiative { get; init; } = 10;
    public ISummonPolicy Summons { get; init; } = new UnlimitedSummons(Array.Empty<string>());
    public List<CombatItem> Items { get; init; } = new();
    public bool CanFlee { get; init; } = true;
    public int? Seed { get; init; }
    /// <summary>Where the player stood in the world (start cells nearest to it are offered first).</summary>
    public CellCoord? PlayerWorldCell { get; init; }
    public StatsTracker? Stats { get; init; }
    /// <summary>Called once the result screen is dismissed.</summary>
    public Action<CombatOutcome, CombatEngine>? OnFinished { get; init; }
}

/// <summary>
/// Turn-based isometric combat. The rules live in <see cref="CombatEngine"/>; this scene shows them:
/// it replays the engine's events as animations, runs AI turns, and turns clicks into actions.
/// </summary>
public sealed class CombatScene : Scene
{
    private enum Phase { Placement, Fighting, Result }

    private readonly CombatRequest _req;
    private readonly RenderQueue _queue = new();
    private readonly Dictionary<int, UnitView> _views = new();
    private readonly Queue<CombatEvent> _events = new();
    private readonly List<FloatText> _floats = new();
    private readonly List<CellCoord> _playerStarts;
    private readonly List<CellCoord> _enemyStarts;
    private Phase _phase = Phase.Placement;
    private CombatEngine? _engine;
    private Beat? _beat;
    private float _aiDelay;
    private CellCoord? _hover;
    private Selection? _selected;
    private string? _toast;
    private float _toastTime;
    private List<ActionButton> _buttons = new();

    /// <summary>Let the AI play the player's side too (sandbox "AI vs AI").</summary>
    public bool AutoPlayPlayerSide { get; set; }
    public float Speed { get; set; } = 1f;
    /// <summary>Extra ImGui drawn on top (sandbox controls). Called from DrawOverlayUi.</summary>
    public Action<CombatScene>? DebugGui { get; set; }
    public CombatEngine? Engine => _engine;

    public CombatScene(CombatRequest request)
    {
        _req = request;
        var tile = request.Tile;
        _playerStarts = tile.PlayerCombatStarts.Where(tile.IsWalkable).ToList();
        _enemyStarts = tile.EnemyCombatStarts.Where(tile.IsWalkable).ToList();
        if (_playerStarts.Count == 0 || _enemyStarts.Count < request.Enemies.Count) FallbackStarts();
    }

    /// <summary>No combat starts painted on this tile: pick sensible ones automatically.</summary>
    private void FallbackStarts()
    {
        var walk = IsoGrid.AllCells.Where(_req.Tile.IsWalkable).ToList();
        if (walk.Count == 0) walk = IsoGrid.AllCells.ToList();
        var anchor = _req.PlayerWorldCell ?? walk[walk.Count / 2];
        _playerStarts.Clear(); _enemyStarts.Clear();
        _playerStarts.AddRange(walk.OrderBy(c => c.ManhattanTo(anchor)).Take(4));
        _enemyStarts.AddRange(walk.Where(c => c.ManhattanTo(anchor) is >= 5 and <= 9).OrderBy(c => c.ManhattanTo(anchor)).Take(Math.Max(6, _req.Enemies.Count * 2)));
        if (_enemyStarts.Count < _req.Enemies.Count) _enemyStarts.AddRange(walk.Except(_playerStarts).OrderByDescending(c => c.ManhattanTo(anchor)).Take(_req.Enemies.Count));
    }

    private List<(string, CellCoord)>? _enemyPlacement;

    public override void OnEnter()
    {
        var rng = new Random(_req.Seed ?? Environment.TickCount);
        _enemyPlacement = _req.Enemies.Zip(_enemyStarts.OrderBy(_ => rng.Next())).Select(p => (p.First, p.Second)).ToList();
        if (AutoPlayPlayerSide) BeginFight(_playerStarts.OrderBy(c => c.ManhattanTo(_req.PlayerWorldCell ?? c)).First());
    }

    private void BeginFight(CellCoord playerCell)
    {
        _engine = new CombatEngine(new CombatSetup
        {
            Defs = Services.Defs, Walkable = _req.Tile.IsWalkable, PlayerCell = playerCell,
            PlayerMaxHp = _req.PlayerMaxHp, PlayerHp = _req.PlayerHp, PlayerAp = _req.PlayerAp, PlayerMp = _req.PlayerMp,
            PlayerInitiative = _req.PlayerInitiative, Enemies = _enemyPlacement!, Summons = _req.Summons, CanFlee = _req.CanFlee, Items = _req.Items,
            Seed = _req.Seed ?? Environment.TickCount,
        });
        foreach (var u in _engine.Units) _views[u.Id] = new UnitView(u, MakeAnim(u));
        _phase = Phase.Fighting;
        Enqueue(_engine.Start());
    }

    private AnimationPlayer MakeAnim(Combatant u)
    {
        string animId = u.Kind == CombatantKind.Player ? Services.Defs.TryGet<Core.Session.GameConfigDef>(Core.Session.GameConfigDef.MainId, out var cfg) ? cfg.PlayerAnimation : "" : u.Monster?.Animation ?? "";
        Services.Defs.TryGet<AnimationDef>(animId, out var def);
        return new AnimationPlayer(def) { PlaceholderSize = u.Kind == CombatantKind.Player ? new(56, 110) : new(80, 70) };
    }

    private void Enqueue(IEnumerable<CombatEvent> events)
    {
        foreach (var e in events)
        {
            _events.Enqueue(e);
            if (_req.Stats is { } stats && _engine is not null) CombatStats.Record(_engine, e, stats);
        }
    }

    // ------------------------------------------------------------------ update

    public override void Update(float dt)
    {
        float sdt = dt * Speed;
        _toastTime -= dt;
        foreach (var v in _views.Values) v.Update(sdt);
        foreach (var f in _floats) f.Age += sdt;
        _floats.RemoveAll(f => f.Age > 1.3f);

        var input = Services.Input;
        _hover = input.MouseInCanvas && !input.MouseCapturedByUi ? IsoGrid.CellAt(input.MouseCanvas.ToNumerics()) : null;

        switch (_phase)
        {
            case Phase.Placement:
                if (input.LeftClicked && _hover is { } c && _playerStarts.Contains(c)) BeginFight(c);
                if (input.Pressed(InputAction.Cancel)) Finish(CombatOutcome.Fled);
                return;
            case Phase.Result:
                if (input.LeftClicked || input.Pressed(InputAction.Confirm) || input.Pressed(InputAction.Interact)) Finish(_engine!.Outcome!.Value);
                return;
        }

        // Play queued events one "beat" at a time.
        if (_beat is not null)
        {
            if (_beat.Update(sdt)) _beat = null;
            else return;
        }
        while (_beat is null && _events.Count > 0) _beat = StartBeat(_events.Dequeue());
        if (_beat is not null || _events.Count > 0) return;

        var engine = _engine!;
        if (engine.Outcome is not null) { _phase = Phase.Result; return; }

        var current = engine.Current;
        bool ai = !current.PlayerControlled || (AutoPlayPlayerSide);
        if (ai)
        {
            _aiDelay += sdt;
            if (_aiDelay < 0.3f) return;
            _aiDelay = 0;
            var action = current.Kind == CombatantKind.Player ? PlayerAutoAction(engine) : CombatAi.NextAction(engine, current);
            Enqueue(engine.Apply(action).Events);
            return;
        }
        BuildButtons(engine, current);
        HandlePlayerInput(engine, current, input);
    }

    /// <summary>AI-vs-AI mode: the player summons whatever it can, then ends its turn.</summary>
    private static CombatAction PlayerAutoAction(CombatEngine e)
    {
        foreach (var id in e.SummonPolicy.Summonable)
            foreach (var cell in TacticalGrid.InRange(e.Player.Cell, 1, 2).OrderBy(c => c.ManhattanTo(e.Player.Cell)))
                if (e.CanSummon(e.Player, id, cell, out _)) return new SummonAction(id, cell);
        return new EndTurnAction();
    }

    private void HandlePlayerInput(CombatEngine e, Combatant me, InputState input)
    {
        if (input.Pressed(InputAction.Cancel)) _selected = null;
        for (int i = 0; i < _buttons.Count && i < 9; i++)
            if (input.Pressed(InputAction.Quick1 + i)) Press(e, _buttons[i]);

        if (!input.LeftClicked) return;
        var mouse = input.MouseCanvas.ToPoint();
        var button = _buttons.FirstOrDefault(b => b.Rect.Contains(mouse));
        if (button is not null) { Press(e, button); return; }
        if (_hover is not { } cell) return;

        CombatAction? action = _selected switch
        {
            { Ability: { } a } => new UseAbilityAction(a.Id, cell),
            { Summon: { } s } => new SummonAction(s, cell),
            _ => e.PathTo(me, cell) is { Count: > 0 } path ? new MoveAction(path) : null,
        };
        if (action is null) return;
        var result = e.Apply(action);
        if (!result.Ok) { Toast(result.Reason); return; }
        _selected = null;
        Enqueue(result.Events);
    }

    private void Press(CombatEngine e, ActionButton b)
    {
        if (!b.Enabled) { Toast(b.DisabledReason); return; }
        switch (b.Kind)
        {
            case ButtonKind.EndTurn: _selected = null; Enqueue(e.Apply(new EndTurnAction()).Events); break;
            case ButtonKind.Flee:
                var r = e.Apply(new FleeAction());
                if (!r.Ok) Toast(r.Reason); else Enqueue(r.Events);
                break;
            case ButtonKind.Ability: _selected = _selected?.Ability == b.Ability ? null : new Selection(b.Ability, null); break;
            case ButtonKind.Summon: _selected = _selected?.Summon == b.Summon ? null : new Selection(null, b.Summon); break;
        }
    }

    private void Toast(string text) { _toast = text; _toastTime = 2.5f; }

    private void Finish(CombatOutcome outcome)
    {
        Services.Scenes.Pop();
        if (_engine is not null) _req.OnFinished?.Invoke(outcome, _engine);
    }

    // ------------------------------------------------------------------ buttons

    private enum ButtonKind { Ability, Summon, EndTurn, Flee }
    private sealed record Selection(AbilityDef? Ability, string? Summon);
    private sealed record ActionButton(ButtonKind Kind, string Label, XRect Rect, bool Enabled, string DisabledReason, AbilityDef? Ability = null, string? Summon = null, int Cost = 0);

    private void BuildButtons(CombatEngine e, Combatant me)
    {
        var list = new List<(ButtonKind, string, bool, string, AbilityDef?, string?, int)>();
        if (me.Kind == CombatantKind.Player)
        {
            foreach (var id in e.SummonPolicy.Summonable)
            {
                if (!Services.Defs.TryGet<Core.Combat.MonsterDef>(id, out var m)) continue;
                bool ok = e.SummonPolicy.CanSummon(id, e.SummonsOnField(id), out var why) && me.Ap >= m.SummonApCost;
                if (ok) why = ""; else if (string.IsNullOrEmpty(why)) why = $"Needs {m.SummonApCost} AP.";
                list.Add((ButtonKind.Summon, $"Summon {m.DisplayName}", ok, why, null, id, m.SummonApCost));
            }
        }
        foreach (var a in e.Abilities(me))
        {
            bool ok = me.Ap >= a.ApCost && !(me.Cooldowns.TryGetValue(a.Id, out int cd) && cd > 0);
            string label = a.DisplayName;
            if (me.Kind == CombatantKind.Player && e.ItemFor(a.Id) is { } item)
            {
                string itemName = Services.Defs.TryGet<Core.Items.ItemDef>(item.ItemId, out var idef) ? idef.DisplayName : item.ItemId;
                int left = e.ItemCharges(a.Id);
                label = item.Consumed ? $"{itemName} ({left})" : itemName;
                if (left <= 0) ok = false;
            }
            list.Add((ButtonKind.Ability, label, ok, ok ? "" : me.Ap < a.ApCost ? $"Needs {a.ApCost} AP." : "Can't use that now.", a, null, a.ApCost));
        }
        list.Add((ButtonKind.EndTurn, "End turn", true, "", null, null, 0));
        if (me.Kind == CombatantKind.Player) list.Add((ButtonKind.Flee, "Flee", e.CanFlee, "You can't flee this fight!", null, null, 0));

        const int w = 170, h = 62, gap = 10;
        int x = 960 - (list.Count * (w + gap) - gap) / 2;
        _buttons = list.Select((b, i) => new ActionButton(b.Item1, b.Item2, new XRect(x + i * (w + gap), 1000, w, h), b.Item3, b.Item4, b.Item5, b.Item6, b.Item7)).ToList();
    }

    // ------------------------------------------------------------------ playback

    private Beat? StartBeat(CombatEvent ev)
    {
        switch (ev)
        {
            case MovedEvent m when _views.TryGetValue(m.Unit, out var v):
                return new MoveBeat(v, m.Path.Select(IsoGrid.CellCenter).ToList());
            case AbilityUsedEvent a when _views.TryGetValue(a.Unit, out var v):
                Services.Defs.TryGet<AbilityDef>(a.Ability, out var def);
                var dir = IsoGrid.CellCenter(a.Target) - v.Position;
                if (MathF.Abs(dir.X) > 1) v.Facing = dir.X < 0 ? Facing.Left : Facing.Right;
                var anim = def?.Animation ?? AnimState.Attack;
                if (anim == AnimState.Attack) anim = dir.Y < 0 ? AnimState.AttackUp : AnimState.AttackDown;
                v.Anim.Play(anim, restart: true);
                return new WaitBeat((def?.HitDelayMs ?? 400) / 1000f, () => { }, () => v.Anim.Play(AnimState.Idle));
            case DamagedEvent d when _views.TryGetValue(d.Unit, out var v):
                v.ShownHp = d.HpAfter;
                v.Flash = 0.35f;
                _floats.Add(new FloatText($"-{d.Amount}", v.Position + new Vector2(0, -90), ElementColor(d.Element)));
                return new WaitBeat(0.25f);
            case HealedEvent hl when _views.TryGetValue(hl.Unit, out var v):
                v.ShownHp = hl.HpAfter;
                _floats.Add(new FloatText($"+{hl.Amount}", v.Position + new Vector2(0, -90), new XColor(120, 255, 140)));
                return new WaitBeat(0.2f);
            case StatusAppliedEvent s when _views.TryGetValue(s.Unit, out var v):
                string sname = Services.Defs.TryGet<StatusDef>(s.Status, out var sd) ? sd.DisplayName : s.Status;
                _floats.Add(new FloatText(sname, v.Position + new Vector2(0, -120), sd?.IsBuff == true ? new XColor(140, 220, 255) : new XColor(255, 170, 90)));
                return new WaitBeat(0.2f);
            case DiedEvent dd when _views.TryGetValue(dd.Unit, out var v):
                v.Dying = true;
                return new WaitBeat(0.5f);
            case SummonedEvent sm:
                var unit = _engine!.Unit(sm.Unit);
                var view = new UnitView(unit, MakeAnim(unit)) { Appear = 0 };
                _views[unit.Id] = view;
                return new WaitBeat(0.4f);
            case TurnStartedEvent t:
                _selected = null;
                return new WaitBeat(_engine!.Unit(t.Unit).PlayerControlled && !AutoPlayPlayerSide ? 0.05f : 0.2f);
            case CombatEndedEvent:
                return new WaitBeat(0.6f);
        }
        return null;
    }

    private XColor ElementColor(string element) =>
        Services.Defs.TryGet<ElementDef>(element, out var el) ? TileRenderer.ParseColor(el.Color, XColor.White) : XColor.White;

    private abstract class Beat
    {
        /// <summary>Returns true when finished.</summary>
        public abstract bool Update(float dt);
    }

    private sealed class WaitBeat(float seconds, Action? start = null, Action? end = null) : Beat
    {
        private float _t;
        private bool _started;
        public override bool Update(float dt)
        {
            if (!_started) { _started = true; start?.Invoke(); }
            _t += dt;
            if (_t < seconds) return false;
            end?.Invoke();
            return true;
        }
    }

    private sealed class MoveBeat(UnitView view, List<Vector2> points) : Beat
    {
        public override bool Update(float dt)
        {
            float budget = 240 * dt;
            while (budget > 0 && points.Count > 0)
            {
                var to = points[0] - view.Position;
                float len = to.Length();
                if (len <= budget) { view.Position = points[0]; points.RemoveAt(0); budget -= len; }
                else { view.Position += to / len * budget; budget = 0; }
                if (len > 0.5f)
                {
                    if (MathF.Abs(to.X) > 0.5f) view.Facing = to.X < 0 ? Facing.Left : Facing.Right;
                    view.Anim.Play(to.Y < 0 ? AnimState.WalkUp : AnimState.WalkDown);
                }
            }
            if (points.Count > 0) return false;
            view.Anim.Play(AnimState.Idle);
            return true;
        }
    }

    private sealed class UnitView(Combatant unit, AnimationPlayer anim)
    {
        public Combatant Unit { get; } = unit;
        public AnimationPlayer Anim { get; } = anim;
        public Vector2 Position { get; set; } = IsoGrid.CellCenter(unit.Cell);
        public Facing Facing { get; set; } = unit.Team == Team.Enemy ? Facing.Left : Facing.Right;
        public int ShownHp { get; set; } = unit.Hp;
        public float Flash { get; set; }
        public bool Dying { get; set; }
        public float Fade { get; private set; } = 1;
        public float Appear { get; set; } = 1;
        public void Update(float dt)
        {
            Anim.Update(dt);
            Flash = Math.Max(0, Flash - dt);
            if (Dying) Fade = Math.Max(0, Fade - dt * 2);
            Appear = Math.Min(1, Appear + dt * 3);
        }
    }

    private sealed class FloatText(string text, Vector2 pos, XColor color)
    {
        public string Text { get; } = text;
        public Vector2 Pos { get; } = pos;
        public XColor Color { get; } = color;
        public float Age { get; set; }
    }

    // ------------------------------------------------------------------ draw

    public override void Draw(SpriteBatch batch)
    {
        var s = Services;
        var d = s.Draw;
        TileRenderer.QueueLayers(_queue, s, _req.Tile);
        _queue.Add(RenderLayer.Background, 1, b => d.Rect(b, new XRect(0, 0, 1920, 1080), XColor.Black * 0.25f));

        // Combat grid: every walkable cell outlined so the board reads clearly.
        _queue.Add(RenderLayer.Ground, 0, b =>
        {
            foreach (var c in IsoGrid.AllCells)
                if (_req.Tile.IsWalkable(c)) d.DiamondOutline(b, IsoGrid.CellCenter(c), IsoGrid.CellWidth - 4, IsoGrid.CellHeight - 2, XColor.White * 0.18f);
        });

        if (_phase == Phase.Placement) DrawPlacement();
        else DrawHighlights();

        foreach (var v in _views.Values)
        {
            if (v.Fade <= 0) continue;
            var tint = (v.Flash > 0 ? new XColor(255, 120, 120) : XColor.White) * (v.Fade * v.Appear);
            var u = v.Unit;
            v.Anim.Draw(_queue, s.Textures, d, v.Position, v.Facing, tint, u.Name);
            // Team ring + HP bar.
            var ringColor = u.Team == Team.Player ? new XColor(80, 160, 255) : new XColor(255, 90, 90);
            if (_engine is { Outcome: null } eng && eng.Current == u) ringColor = XColor.Gold;
            var pos = v.Position;
            _queue.Add(RenderLayer.Ground, 1, b => d.DiamondOutline(b, pos, 96, 48, ringColor * v.Fade, 3));
            int hp = v.ShownHp, max = u.MaxHp;
            _queue.Add(RenderLayer.WorldUi, 0, b =>
            {
                var bar = new XRect((int)pos.X - 40, (int)pos.Y + 22, 80, 8);
                d.Rect(b, bar, XColor.Black * 0.7f);
                d.Rect(b, new XRect(bar.X, bar.Y, (int)(bar.Width * Math.Clamp(hp / (float)max, 0, 1)), bar.Height), u.Team == Team.Player ? new XColor(90, 200, 120) : new XColor(220, 80, 70));
            });
        }
        foreach (var f in _floats)
        {
            float a = 1 - Math.Clamp((f.Age - 0.8f) / 0.5f, 0, 1);
            var p = f.Pos + new Vector2(0, -f.Age * 40);
            _queue.Add(RenderLayer.WorldUi, 1, b => d.TextCentered(b, f.Text, p, f.Color * a, 30, bold: true));
        }
        _queue.Flush(batch);

        batch.Begin();
        DrawHud(batch);
        batch.End();
    }

    private void DrawPlacement()
    {
        var d = Services.Draw;
        _queue.Add(RenderLayer.Ground, 2, b =>
        {
            foreach (var c in _playerStarts) d.DiamondFill(b, IsoGrid.CellCenter(c), 110, 55, new XColor(70, 140, 255) * (c == _hover ? 0.9f : 0.5f));
            foreach (var (_, c) in _enemyPlacement!) d.DiamondFill(b, IsoGrid.CellCenter(c), 110, 55, new XColor(255, 80, 80) * 0.5f);
        });
        foreach (var (monster, c) in _enemyPlacement!)
        {
            Services.Defs.TryGet<Core.Combat.MonsterDef>(monster, out var m);
            Services.Defs.TryGet<AnimationDef>(m?.Animation, out var anim);
            var player = new AnimationPlayer(anim) { PlaceholderSize = new(80, 70) };
            player.Draw(_queue, Services.Textures, d, IsoGrid.CellCenter(c), Facing.Left, XColor.White, m?.DisplayName ?? monster);
        }
    }

    private void DrawHighlights()
    {
        var e = _engine!;
        if (e.Outcome is not null || _beat is not null || _events.Count > 0) return;
        var me = e.Current;
        if (!me.PlayerControlled || AutoPlayPlayerSide) return;
        var d = Services.Draw;

        if (_selected?.Ability is { } a)
        {
            var range = e.RangeCells(me, a).Where(_req.Tile.IsWalkable).ToHashSet();
            bool valid = _hover is { } hc && e.CanUseAbility(me, a, hc, out _);
            var area = _hover is { } h && valid ? e.AffectedCells(a, h).ToHashSet() : new HashSet<CellCoord>();
            _queue.Add(RenderLayer.Ground, 2, b =>
            {
                foreach (var c in range) d.DiamondFill(b, IsoGrid.CellCenter(c), 116, 58, new XColor(80, 140, 255) * 0.35f);
                foreach (var c in area) d.DiamondFill(b, IsoGrid.CellCenter(c), 116, 58, new XColor(255, 120, 80) * 0.6f);
            });
            return;
        }
        if (_selected?.Summon is { } summon && Services.Defs.TryGet<Core.Combat.MonsterDef>(summon, out var sm))
        {
            var cells = TacticalGrid.InRange(me.Cell, 1, sm.SummonRange).Where(e.Passable).ToList();
            _queue.Add(RenderLayer.Ground, 2, b =>
            {
                foreach (var c in cells) d.DiamondFill(b, IsoGrid.CellCenter(c), 116, 58, new XColor(140, 220, 255) * (c == _hover ? 0.8f : 0.35f));
            });
            return;
        }
        // Default: show where the current creature can walk, and the path to the hovered cell.
        var reach = e.Reachable(me);
        var path = _hover is { } target && reach.ContainsKey(target) ? e.PathTo(me, target) : null;
        _queue.Add(RenderLayer.Ground, 2, b =>
        {
            foreach (var c in reach.Keys) if (c != me.Cell) d.DiamondFill(b, IsoGrid.CellCenter(c), 116, 58, new XColor(80, 220, 120) * 0.28f);
            if (path is not null) foreach (var c in path) d.DiamondFill(b, IsoGrid.CellCenter(c), 116, 58, new XColor(80, 220, 120) * 0.6f);
        });
    }

    private void DrawHud(SpriteBatch b)
    {
        var d = Services.Draw;
        if (_phase == Phase.Placement)
        {
            d.TextCentered(b, "Choose your starting cell (blue)", new XVec(960, 60), XColor.White, 34, bold: true);
            d.TextCentered(b, "Esc to back out", new XVec(960, 100), XColor.LightGray, 20);
            return;
        }
        var e = _engine!;

        // Turn order across the top.
        var order = e.TurnOrder.ToList();
        int x = 960 - order.Count * 85;
        foreach (var u in order)
        {
            var r = new XRect(x, 14, 160, 54);
            bool current = e.Outcome is null && u == e.Current;
            d.Rect(b, r, (u.Team == Team.Player ? new XColor(30, 60, 110) : new XColor(100, 30, 30)) * 0.85f);
            d.RectOutline(b, r, current ? XColor.Gold : XColor.White * 0.3f, current ? 3 : 1);
            d.Text(b, u.Name, new XVec(r.X + 8, r.Y + 4), XColor.White, 17, bold: current);
            d.Text(b, $"{u.Hp}/{u.MaxHp} HP", new XVec(r.X + 8, r.Y + 28), XColor.LightGray, 15);
            x += 170;
        }

        // Current creature panel + action bar.
        if (e.Outcome is null)
        {
            var me = e.Current;
            var panel = new XRect(20, 960, 330, 104);
            d.Rect(b, panel, XColor.Black * 0.7f);
            d.Text(b, me.Name, new XVec(panel.X + 12, panel.Y + 8), XColor.Gold, 22, bold: true);
            d.Text(b, $"HP {me.Hp}/{me.MaxHp}   AP {me.Ap}   MP {me.Mp}", new XVec(panel.X + 12, panel.Y + 40), XColor.White, 20);
            string statuses = string.Join(", ", me.Statuses.Select(st => $"{st.Def.DisplayName} ({st.TurnsLeft})"));
            if (statuses.Length > 0) d.Text(b, statuses, new XVec(panel.X + 12, panel.Y + 70), new XColor(150, 210, 255), 16);

            if (me.PlayerControlled && !AutoPlayPlayerSide && _beat is null && _events.Count == 0)
            {
                var mouse = Services.Input.MouseCanvas.ToPoint();
                for (int i = 0; i < _buttons.Count; i++)
                {
                    var bt = _buttons[i];
                    bool sel = (bt.Ability is not null && _selected?.Ability == bt.Ability) || (bt.Summon is not null && _selected?.Summon == bt.Summon);
                    var fill = !bt.Enabled ? new XColor(40, 40, 50) : sel ? new XColor(90, 110, 170) : bt.Rect.Contains(mouse) ? new XColor(60, 70, 110) : new XColor(35, 42, 70);
                    d.Rect(b, bt.Rect, fill * 0.95f);
                    d.RectOutline(b, bt.Rect, sel ? XColor.Gold : XColor.White * 0.4f, sel ? 2 : 1);
                    d.Text(b, $"{i + 1}", new XVec(bt.Rect.X + 6, bt.Rect.Y + 4), XColor.Gray, 14);
                    d.TextCentered(b, bt.Label, new XVec(bt.Rect.Center.X, bt.Rect.Center.Y - 6), bt.Enabled ? XColor.White : XColor.Gray, 18);
                    if (bt.Cost > 0) d.TextCentered(b, $"{bt.Cost} AP", new XVec(bt.Rect.Center.X, bt.Rect.Bottom - 14), new XColor(120, 200, 255), 14);
                }
                string hint = _selected?.Ability is not null ? "Click a target (Esc to cancel)"
                    : _selected?.Summon is not null ? "Click a free cell near you to summon"
                    : "Click a green cell to move";
                d.TextCentered(b, hint, new XVec(960, 975), XColor.White * 0.8f, 18);
            }
        }

        // Hover info.
        if (_hover is { } hc && e.At(hc) is { } hu)
        {
            var info = new XRect(1560, 90, 340, 110);
            d.Rect(b, info, XColor.Black * 0.75f);
            d.Text(b, hu.Name, new XVec(info.X + 12, info.Y + 8), hu.Team == Team.Player ? new XColor(130, 190, 255) : new XColor(255, 140, 130), 22, bold: true);
            d.Text(b, $"HP {hu.Hp}/{hu.MaxHp}  AP {hu.BaseAp}  MP {hu.BaseMp}  Init {hu.Initiative}", new XVec(info.X + 12, info.Y + 40), XColor.White, 17);
            string st = string.Join(", ", hu.Statuses.Select(x => $"{x.Def.DisplayName} ({x.TurnsLeft})"));
            if (st.Length > 0) d.Text(b, st, new XVec(info.X + 12, info.Y + 70), new XColor(150, 210, 255), 16);
        }

        if (_toastTime > 0 && _toast is not null) d.TextCentered(b, _toast, new XVec(960, 920), new XColor(255, 200, 120), 26, bold: true);

        if (_phase == Phase.Result)
        {
            var outcome = e.Outcome!.Value;
            d.Rect(b, new XRect(0, 0, 1920, 1080), XColor.Black * 0.5f);
            string title = outcome switch { CombatOutcome.Victory => "Victory", CombatOutcome.Defeat => "Defeated", _ => "You fled" };
            d.TextCentered(b, title, new XVec(960, 440), outcome == CombatOutcome.Victory ? XColor.Gold : XColor.LightGray, 72, bold: true);
            var kills = e.Units.Where(u => u.Kind == CombatantKind.Monster && u.IsDead).GroupBy(u => u.Monster!.DisplayName).Select(g => $"{g.Key} x{g.Count()}");
            d.TextCentered(b, outcome == CombatOutcome.Victory ? "Defeated: " + string.Join(", ", kills) : "", new XVec(960, 520), XColor.White, 24);
            d.TextCentered(b, "Click to continue", new XVec(960, 600), XColor.LightGray, 22);
        }
    }

    public override void DrawOverlayUi() => DebugGui?.Invoke(this);
}
