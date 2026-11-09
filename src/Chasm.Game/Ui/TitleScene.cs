using Chasm.Core;
using Chasm.Core.Session;
using Chasm.Core.World;
using Chasm.Game.Input;
using Chasm.Game.Saving;
using Chasm.Game.Scenes;
using Chasm.Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Ui;

/// <summary>Title screen: three save slots. Click a slot to continue it or start a new game in it.</summary>
public sealed class TitleScene : Scene
{
    private readonly SaveStore _store;
    private SaveData?[] _slots = new SaveData?[SaveStore.SlotCount];
    private int? _confirmDelete;

    public TitleScene(SaveStore? store = null) => _store = store ?? new SaveStore();

    public override void OnEnter() => Refresh();
    public override void OnUncovered() => Refresh();

    private void Refresh()
    {
        for (int i = 0; i < SaveStore.SlotCount; i++) _slots[i] = _store.Load(i + 1);
    }

    private static Rectangle SlotRect(int i) => new(660, 380 + i * 150, 600, 130);
    private static Rectangle DeleteRect(int i) { var r = SlotRect(i); return new Rectangle(r.Right + 16, r.Y + 40, 120, 50); }
    private static readonly Rectangle QuitRect = new(860, 860, 200, 56);

    public override void Update(float dt)
    {
        var input = Services.Input;
        if (_confirmDelete is not null) return;
        if (!input.LeftClicked) return;
        var m = input.MouseCanvas.ToPoint();
        for (int i = 0; i < SaveStore.SlotCount; i++)
        {
            if (SlotRect(i).Contains(m)) { Play(i + 1); return; }
            if (_slots[i] is not null && DeleteRect(i).Contains(m))
            {
                int slot = i + 1;
                _confirmDelete = slot;
                Services.Scenes.Push(new ConfirmScene($"Erase save {slot}? This can't be undone.", yes =>
                {
                    if (yes) _store.Delete(slot);
                    _confirmDelete = null;
                    Refresh();
                }));
                return;
            }
        }
        if (QuitRect.Contains(m)) Services.Game.Exit();
    }

    private void Play(int slot)
    {
        var session = new GameSession(Services.Defs);
        var data = _store.Load(slot);
        if (data is not null) session.LoadSave(data);
        var world = new WorldScene(session) { SaveTarget = (_store, slot) };
        Services.Scenes.Push(world);
        if (data?.FightInProgress is not null) world.ResolveAbandonedFight();
    }

    public override void Draw(SpriteBatch batch)
    {
        var d = Services.Draw;
        var m = Services.Input.MouseCanvas.ToPoint();
        batch.Begin();
        // Placeholder title art: slow drifting cloud bands until real title art exists.
        float t = (float)Services.TotalTime;
        for (int i = 0; i < 12; i++)
        {
            float y = 120 + i * 85 + MathF.Sin(t * 0.3f + i) * 12;
            d.Rect(batch, new Rectangle(-200 + (int)(MathF.Sin(t * 0.1f + i * 2) * 120), (int)y, 2400, 46), new Color(200, 210, 235) * (0.03f + 0.02f * (i % 3)));
        }
        d.TextCentered(batch, GameInfo.Title, new Vector2(960, 200), new Color(220, 228, 250), 96, bold: true);
        d.TextCentered(batch, "Choose a save", new Vector2(960, 320), Color.LightGray, 26);
        for (int i = 0; i < SaveStore.SlotCount; i++)
        {
            var r = SlotRect(i);
            var s = _slots[i];
            d.Rect(batch, r, r.Contains(m) ? new Color(50, 60, 95) : new Color(28, 32, 52));
            d.RectOutline(batch, r, Color.White * 0.4f, 2);
            if (s is null)
            {
                d.Text(batch, $"Save {i + 1}", new Vector2(r.X + 24, r.Y + 20), Color.White, 30, bold: true);
                d.Text(batch, "Empty - click to begin a new journey", new Vector2(r.X + 24, r.Y + 70), Color.LightGray, 22);
                continue;
            }
            string where = Services.Defs.TryGet<TileDef>(s.Tile, out var tile) ? $"Floor {tile.Floor} - {tile.DisplayName}" : s.Tile;
            var played = TimeSpan.FromSeconds(s.PlaySeconds);
            d.Text(batch, $"Save {i + 1}   Day {s.Day}", new Vector2(r.X + 24, r.Y + 16), Color.White, 30, bold: true);
            d.Text(batch, where, new Vector2(r.X + 24, r.Y + 60), new Color(170, 210, 255), 22);
            d.Text(batch, $"played {(int)played.TotalHours}h {played.Minutes}m   last saved {s.SavedAtUtc.ToLocalTime():g}", new Vector2(r.X + 24, r.Y + 92), Color.Gray, 17);
            var del = DeleteRect(i);
            d.Rect(batch, del, del.Contains(m) ? new Color(120, 40, 40) : new Color(60, 25, 25));
            d.TextCentered(batch, "Erase", del.Center.ToVector2(), Color.White, 20);
        }
        d.Rect(batch, QuitRect, QuitRect.Contains(m) ? new Color(70, 70, 90) : new Color(40, 40, 55));
        d.TextCentered(batch, "Quit", QuitRect.Center.ToVector2(), Color.White, 24);
        batch.End();
    }
}
