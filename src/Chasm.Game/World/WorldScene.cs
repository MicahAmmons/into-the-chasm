using System.Numerics;
using Chasm.Core.Animation;
using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.Rules;
using Chasm.Core.Session;
using Chasm.Core.Stats;
using Chasm.Core.World;
using Chasm.Game.Debug;
using Chasm.Game.Input;
using Chasm.Game.Rendering;
using Chasm.Game.Scenes;
using Chasm.Game.Ui;
using Microsoft.Xna.Framework.Graphics;
using XColor = Microsoft.Xna.Framework.Color;
using XRect = Microsoft.Xna.Framework.Rectangle;
using XVec = Microsoft.Xna.Framework.Vector2;

namespace Chasm.Game.World;

/// <summary>
/// Out-of-combat play on one Tile: walking, interacting, picking things up, leaving through edges.
/// Implements <see cref="IRuleHost"/> so rules can show messages, move the player, etc.
/// The World Sandbox wraps this same scene with editing panels.
/// </summary>
public class WorldScene : Scene, IRuleHost
{
    private const float FadeSeconds = 0.35f;

    private readonly RenderQueue _queue = new();
    private readonly StatsOverlay _statsOverlay = new();
    private CellCoord _lastCell;
    private Edge _suppressedEdges;
    private Placement? _pendingInteract;
    private float _fade;                 // 0 = clear, 1 = black
    private int _fadeDir;                // +1 fading out, -1 fading in
    private (string tile, Vector2? pos, CellCoord? cell)? _pendingChange;
    private IDisposable? _defSub;

    public GameSession Session { get; }
    public TileDef Tile { get; private set; } = null!;
    public PlayerAvatar Player { get; private set; } = null!;
    public bool ShowGrid { get; set; }
    public bool ShowPath { get; set; }
    public Placement? Hovered { get; private set; }

    /// <summary>When false, time doesn't pass (editor sandboxes can freeze the clock).</summary>
    public bool ClockRunning { get; set; } = true;

    public WorldScene(GameSession session) => Session = session;

    public override void OnEnter()
    {
        var cfg = Session.Config;
        Session.Defs.TryGet<AnimationDef>(cfg.PlayerAnimation, out var anim);
        Player = new PlayerAvatar(anim) { Speed = cfg.PlayerWalkSpeed };
        Player.Walked += d => Session.Stats.Add(StatKeys.DistanceWalked, d);
        Player.Arrived += OnArrived;

        string tileId = string.IsNullOrEmpty(Session.CurrentTile) ? cfg.StartTile : Session.CurrentTile;
        LoadTile(tileId, Session.PlayerPosition == Vector2.Zero ? null : Session.PlayerPosition, null);

        if (Environment.GetEnvironmentVariable("CHASM_DEBUG_GRID") == "1") ShowGrid = true;
        Session.Defs.Changed += OnDefChanged;
        _defSub = new Unsub(() => Session.Defs.Changed -= OnDefChanged);
    }

    public override void OnExit() => _defSub?.Dispose();

    private sealed class Unsub(Action a) : IDisposable { public void Dispose() => a(); }

    private void OnDefChanged(Def def)
    {
        // Hot reload: the tile we're standing in was edited (in the map editor or a text editor).
        if (def is TileDef t && Tile is not null && t.Id == Tile.Id) Tile = t;
        if (def is GameConfigDef) Session.ApplyConfig();
    }

    // ------------------------------------------------------------------ tiles

    public void LoadTile(string tileId, Vector2? position, CellCoord? cell)
    {
        if (!Session.Defs.TryGet<TileDef>(tileId, out var tile))
        {
            tile = Tile ?? new TileDef { Id = "missing", Name = "Missing tile" };
            ShowMessage(new[] { $"Tile '{tileId}' doesn't exist yet. The clouds are too thick to pass." }, null);
            if (Tile is not null) return;
        }
        Tile = tile;
        Session.CurrentTile = tile.Id;
        Vector2 pos = cell is { } c ? IsoGrid.CellCenter(c)
            : position ?? IsoGrid.CellCenter(tile.Spawn);
        Player.Position = SnapToWalkable(pos);
        Player.Stop();
        Session.PlayerPosition = Player.Position;
        _lastCell = Player.Cell;
        _suppressedEdges = IsoGrid.EdgesOf(_lastCell); // don't instantly re-prompt on arrival
        _pendingInteract = null;
    }

    private Vector2 SnapToWalkable(Vector2 pos)
    {
        if (Walkable(IsoGrid.CellAt(pos))) return pos;
        CellCoord? best = null;
        float bestD = float.MaxValue;
        foreach (var c in IsoGrid.AllCells)
        {
            if (!Walkable(c)) continue;
            float d = Vector2.DistanceSquared(IsoGrid.CellCenter(c), pos);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best is { } b ? IsoGrid.CellCenter(b) : pos;
    }

    public bool Walkable(CellCoord c) =>
        Tile.IsWalkable(c) && !Tile.Placements.Any(p => p.BlocksMovement && p.Cell == c && IsActive(p));

    // ------------------------------------------------------------------ placements

    public RuleContext Context(Placement? source = null)
    {
        Session.PlayerPosition = Player.Position;
        var ctx = Session.CreateContext(this);
        ctx.SourcePlacementId = source?.Id;
        return ctx;
    }

    public bool IsActive(Placement p)
    {
        if (Session.World.IsSet(WorldState.PlacementKey(Tile.Id, p.Id, "gone"))) return false;
        return p.VisibleWhen.Count == 0 || Rules.AllPass(p.VisibleWhen, Context(p));
    }

    private bool IsHidden(Placement p) => p is InteractablePlacement { Invisible: true };

    public IEnumerable<Placement> ActivePlacements => Tile.Placements.Where(IsActive);

    public void Interact(Placement p)
    {
        var ctx = Context(p);
        switch (p)
        {
            case ItemPickupPlacement pick:
                var name = Session.Defs.TryGet<ItemDef>(pick.Item, out var item) ? item.DisplayName : pick.Item;
                Session.Inventory.Add(pick.Item, pick.Count);
                Session.Events.Publish(new ItemGainedEvent(pick.Item, pick.Count));
                if (pick.OneTime) Session.World.Set(WorldState.PlacementKey(Tile.Id, p.Id, "gone"), 1);
                ShowMessage(new[] { $"Picked up {(pick.Count > 1 ? pick.Count + "x " : "")}{name}." }, null);
                foreach (var o in pick.AlsoOnPickup) o.Execute(ctx);
                break;
            case InteractablePlacement inter:
                if (Rules.RunFirst(inter.Rules, ctx) is null && !string.IsNullOrWhiteSpace(inter.FallbackMessage))
                    ShowMessage(new[] { inter.FallbackMessage }, null);
                break;
        }
    }

    private Placement? PlacementAt(Vector2 canvasPos)
    {
        // Prefer things whose sprite box contains the mouse; fall back to the cell under it.
        var cell = IsoGrid.CellAt(canvasPos);
        return ActivePlacements.Where(p => !IsHidden(p))
            .OrderByDescending(p => IsoGrid.CellCenter(p.Cell).Y)
            .FirstOrDefault(p => p.Cell == cell || SpriteBounds(p).Contains((int)canvasPos.X, (int)canvasPos.Y));
    }

    private XRect SpriteBounds(Placement p)
    {
        var c = IsoGrid.CellCenter(p.Cell);
        var size = SpriteSize(p);
        int yOff = p is InteractablePlacement ip ? ip.SpriteYOffset : 0;
        return new XRect((int)c.X - size.X / 2, (int)c.Y - size.Y + 12 - yOff, size.X, size.Y);
    }

    private Microsoft.Xna.Framework.Point SpriteSize(Placement p)
    {
        string? tex = SpriteKey(p);
        if (!string.IsNullOrEmpty(tex) && Services.Textures.Exists(tex))
        {
            var t = Services.Textures.Get(tex);
            float scale = p is ItemPickupPlacement ? Math.Min(1f, 64f / Math.Max(t.Width, t.Height)) : 1f;
            return new((int)(t.Width * scale), (int)(t.Height * scale));
        }
        return p is ItemPickupPlacement ? new(60, 48) : new(110, 80);
    }

    private string? SpriteKey(Placement p) => p switch
    {
        ItemPickupPlacement ip when Session.Defs.TryGet<ItemDef>(ip.Item, out var i) =>
            string.IsNullOrEmpty(i.WorldSprite) ? i.Icon : i.WorldSprite,
        InteractablePlacement ip => ip.Sprite,
        _ => null,
    };

    private string Label(Placement p) => p switch
    {
        ItemPickupPlacement ip => Session.Defs.TryGet<ItemDef>(ip.Item, out var i) ? i.DisplayName : ip.Item,
        InteractablePlacement ip => ip.Label,
        _ => p.Id,
    };

    // ------------------------------------------------------------------ update

    public override void Update(float dt)
    {
        UpdateFade(dt);
        if (_fadeDir != 0) { Player.Animation.Update(dt); return; }

        var input = Services.Input;
        if (ClockRunning) Session.Clock.Advance(dt);
        if (input.Pressed(InputAction.DebugOverlay)) _statsOverlay.Visible = !_statsOverlay.Visible;
        if (input.Pressed(InputAction.DebugCells)) ShowGrid = !ShowGrid;
        if (input.Pressed(InputAction.Rest)) Rest();

        Hovered = input.MouseInCanvas && !input.MouseCapturedByUi ? PlacementAt(input.MouseCanvas.ToNumerics()) : null;

        if (input.LeftClicked && input.MouseInCanvas && IsTop) OnClick(input.MouseCanvas.ToNumerics());
        if (input.Pressed(InputAction.Interact) && IsTop) InteractNearest();

        Player.Update(dt);
        Session.PlayerPosition = Player.Position;

        var cell = Player.Cell;
        if (cell != _lastCell)
        {
            _lastCell = cell;
            _suppressedEdges &= IsoGrid.EdgesOf(cell);
            foreach (var p in ActivePlacements.OfType<InteractablePlacement>().Where(p => p.Trigger == InteractTrigger.StepOn && p.Cell == cell).ToList())
                Interact(p);
            CheckExits(cell);
        }
    }

    protected virtual void OnClick(Vector2 point)
    {
        if (Hovered is { } target)
        {
            if (InRange(target)) { Player.Stop(); Interact(target); return; }
            _pendingInteract = target;
            WalkTo(IsoGrid.CellCenter(target.Cell));
            return;
        }
        _pendingInteract = null;
        WalkTo(point);
    }

    public void WalkTo(Vector2 point)
    {
        var result = FreePathfinder.FindPath(Player.Position, point, Walkable);
        Player.SetPath(result.Waypoints);
        if (result.Waypoints.Count == 0) OnArrived();
    }

    private bool InRange(Placement p) => p.Cell.ManhattanTo(Player.Cell) <= Math.Max(1, Session.Config.InteractRange) || p.Cell == Player.Cell;

    private void OnArrived()
    {
        if (_pendingInteract is { } p && IsActive(p) && InRange(p)) { _pendingInteract = null; Interact(p); }
        CheckExits(Player.Cell);
    }

    private void InteractNearest()
    {
        var target = ActivePlacements
            .Where(p => !(p is InteractablePlacement { Trigger: InteractTrigger.StepOn }))
            .Where(InRange)
            .OrderBy(p => p.Cell.ManhattanTo(Player.Cell))
            .FirstOrDefault();
        if (target is not null) Interact(target);
    }

    private void CheckExits(CellCoord cell)
    {
        var edges = IsoGrid.EdgesOf(cell);
        if (edges == Edge.None || Player.IsMoving) return;
        foreach (var exit in Tile.Exits)
        {
            if ((edges & exit.Edge) == 0 || (_suppressedEdges & exit.Edge) != 0) continue;
            _suppressedEdges |= exit.Edge;
            if (!Rules.AllPass(exit.Conditions, Context()))
            {
                if (!string.IsNullOrWhiteSpace(exit.BlockedMessage)) ShowMessage(new[] { exit.BlockedMessage }, null);
                return;
            }
            var from = Player.Position;
            var edge = exit.Edge;
            Services.Scenes.Push(new ConfirmScene(exit.Prompt, yes =>
            {
                if (yes) BeginChange(exit.TargetTile, Mirror(from, edge), null);
            }));
            return;
        }
    }

    /// <summary>Leaving east puts you on the west side of the next tile, and so on.</summary>
    private static Vector2 Mirror(Vector2 p, Edge leftThrough) => leftThrough switch
    {
        Edge.East or Edge.West => new Vector2(1920 - p.X, p.Y),
        _ => new Vector2(p.X, 1080 - p.Y),
    };

    private void BeginChange(string tile, Vector2? pos, CellCoord? cell)
    {
        _pendingChange = (tile, pos, cell);
        _fadeDir = 1;
        Player.Stop();
    }

    private void UpdateFade(float dt)
    {
        if (_fadeDir == 0) return;
        _fade += _fadeDir * dt / FadeSeconds;
        if (_fadeDir > 0 && _fade >= 1)
        {
            _fade = 1;
            if (_pendingChange is { } ch) LoadTile(ch.tile, ch.pos, ch.cell);
            _pendingChange = null;
            _fadeDir = -1;
        }
        else if (_fadeDir < 0 && _fade <= 0) { _fade = 0; _fadeDir = 0; }
    }

    // ------------------------------------------------------------------ IRuleHost

    public void ShowMessage(IReadOnlyList<string> lines, string? speaker)
    {
        if (lines.Count == 0 || Services is null) return;
        Services.Scenes.Push(new MessageBoxScene(lines, speaker));
    }

    public void ChangeTile(string tileId, CellCoord? arrivalCell) => BeginChange(tileId, null, arrivalCell);
    public virtual void StartEncounter(string encounterId) => ShowMessage(new[] { $"(Combat '{encounterId}' would start here.)" }, null);
    public virtual void StartDialogue(string dialogueId) => ShowMessage(new[] { $"(Dialogue '{dialogueId}' would start here.)" }, null);

    public void Rest()
    {
        Session.Clock.Rest();
        Session.Stats.Add(StatKeys.Rests);
        ShowMessage(new[] { $"You rest for half a day. It is now day {Session.Clock.Day}, {(Session.Clock.IsNight ? "night" : "daytime")}." }, null);
    }

    // ------------------------------------------------------------------ draw

    public override void Draw(SpriteBatch batch)
    {
        var s = Services;
        TileRenderer.QueueLayers(_queue, s, Tile);
        if (ShowGrid) TileRenderer.QueueWalkGrid(_queue, s, Tile);

        foreach (var p in ActivePlacements)
        {
            if (IsHidden(p))
            {
                if (ShowGrid) TileRenderer.QueueCell(_queue, s, p.Cell, new XColor(200, 120, 255) * 0.5f);
                continue;
            }
            var rect = SpriteBounds(p);
            string? key = SpriteKey(p);
            var tex = s.Textures.Get(key);
            string label = Label(p);
            bool hovered = p == Hovered;
            _queue.Add(RenderLayer.Entities, IsoGrid.CellCenter(p.Cell).Y, b =>
            {
                if (s.Textures.IsPlaceholder(tex)) s.Draw.Placeholder(b, rect, label);
                else b.Draw(tex, rect, hovered ? new XColor(255, 255, 220) : XColor.White);
            });
            if (hovered)
                _queue.Add(RenderLayer.WorldUi, 0, b => s.Draw.TextCentered(b, label, new XVec(rect.Center.X, rect.Y - 18), XColor.White, 22));
        }

        Player.Draw(_queue, s);
        if (ShowPath && Player.Path.Count > 0)
            _queue.Add(RenderLayer.WorldUi, 0, b =>
            {
                var prev = Player.Position;
                foreach (var w in Player.Path) { s.Draw.Line(b, prev, w, XColor.Yellow, 3); prev = w; }
            });

        _queue.Flush(batch);

        batch.Begin();
        Hud.DrawDay(batch, s.Draw, Session.Clock);
        if (_fade > 0) s.Draw.Rect(batch, new XRect(0, 0, 1920, 1080), XColor.Black * _fade);
        batch.End();

        _statsOverlay.ExtraLines.Clear();
        _statsOverlay.ExtraLines.Add($"tile {Tile.Id} (floor {Tile.Floor})  cell {Player.Cell}");
        _statsOverlay.ExtraLines.Add($"gold {Session.Inventory.Gold}  items {string.Join(", ", Session.Inventory.Items.Select(kv => $"{kv.Key} x{kv.Value}"))}");
        _statsOverlay.ExtraLines.Add($"flags {string.Join(", ", Session.World.Flags.Select(kv => $"{kv.Key}={kv.Value}"))}");
        _statsOverlay.Draw(batch, s.Draw, Session.Stats);
    }
}
