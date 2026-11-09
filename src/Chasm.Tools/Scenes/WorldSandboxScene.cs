using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.Session;
using Chasm.Core.World;
using Chasm.Game.World;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.Scenes;

/// <summary>
/// The real World scene plus live debug panels: jump to any tile, give/take items and gold, edit
/// world flags, control the clock, watch stats. Uses a throwaway session, so nothing here touches
/// save files.
/// </summary>
public sealed class WorldSandboxScene : WorldScene
{
    private readonly string? _startTile;
    private readonly CellCoord? _startCell;
    private string _giveItem = "";
    private int _giveCount = 1;
    private string _newFlag = "";
    private string _teleportTile = "";
    private bool _panelOpen = true;

    public WorldSandboxScene(string? tileId, CellCoord? cell = null) : base(NewSession(tileId))
    {
        _startTile = tileId;
        _startCell = cell;
    }

    private static GameSession NewSession(string? tile) => new(SandboxDefs!) { CurrentTile = tile ?? "" };

    /// <summary>Set by Program before any scene is built (sessions need the registry).</summary>
    public static DefRegistry? SandboxDefs { get; set; }

    public override void OnEnter()
    {
        ToolContext.EnsureInitialized(Services);
        Services.Canvas.Region = () =>
        {
            var pp = Services.Graphics.PresentationParameters;
            return new Microsoft.Xna.Framework.Rectangle(0, 24, pp.BackBufferWidth, pp.BackBufferHeight - 24);
        };
        base.OnEnter();
        ShowPath = true;
        if (_startCell is { } c) LoadTile(Tile.Id, null, c);
        _teleportTile = Tile.Id;
    }

    public override void OnExit()
    {
        Services.Canvas.Region = null;
        base.OnExit();
    }

    public override void Update(float dt)
    {
        ToolContext.MenuBar(Services, $"World Sandbox - {Tile.Id}", () =>
        {
            if (ImGui.BeginMenu("Sandbox"))
            {
                if (ImGui.MenuItem("Edit this tile in the Map Editor")) Services.Scenes.ResetTo(new MapEditorScene(Tile.Id));
                if (ImGui.MenuItem("Reset session (clear items, flags, stats)")) Services.Scenes.ResetTo(new WorldSandboxScene(Tile.Id, Player.Cell));
                ImGui.MenuItem("Show panel", null, ref _panelOpen);
                ImGui.EndMenu();
            }
        });
        if (_panelOpen && IsTop) Panel();
        ToolContext.Wizards.Draw();
        base.Update(dt);
    }

    private void Panel()
    {
        ImGui.SetNextWindowPos(new Num.Vector2(10, 34), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Num.Vector2(440, 640), ImGuiCond.FirstUseEver);
        ImGui.Begin("Sandbox", ref _panelOpen);
        var insp = ToolContext.Wizards.Inspector;

        if (ImGui.CollapsingHeader("Where", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Tile {Tile.Id} (floor {Tile.Floor})  cell {Player.Cell}");
            ImGui.SetNextItemWidth(250);
            insp.RefPicker(typeof(TileDef), ref _teleportTile);
            ImGui.SameLine();
            if (ImGui.Button("Go")) ChangeTile(_teleportTile, null);
            bool grid = ShowGrid; if (ImGui.Checkbox("Walk grid (F4)", ref grid)) ShowGrid = grid;
            ImGui.SameLine();
            bool path = ShowPath; if (ImGui.Checkbox("Path", ref path)) ShowPath = path;
        }

        if (ImGui.CollapsingHeader("Clock", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var clock = Session.Clock;
            ImGui.Text($"Day {clock.Day}, {(clock.IsNight ? "night" : "day")}  ({clock.Time:P0} through the day)");
            bool run = ClockRunning; if (ImGui.Checkbox("Time passes", ref run)) ClockRunning = run;
            ImGui.SameLine();
            if (ImGui.Button("Rest")) Rest();
            ImGui.SameLine();
            if (ImGui.Button("+1 day")) clock.Load(clock.Day + 1, clock.Time);
            float t = clock.Time;
            if (ImGui.SliderFloat("time", ref t, 0, 0.999f)) clock.Load(clock.Day, t);
        }

        if (ImGui.CollapsingHeader("Inventory", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int gold = Session.Inventory.Gold;
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputInt("Gold", ref gold, 100)) Session.Inventory.AddGold(gold - Session.Inventory.Gold);
            ImGui.SetNextItemWidth(220);
            insp.RefPicker(typeof(ItemDef), ref _giveItem);
            ImGui.SameLine(); ImGui.SetNextItemWidth(60);
            ImGui.InputInt("##n", ref _giveCount, 0);
            ImGui.SameLine();
            if (ImGui.Button("Give") && _giveItem.Length > 0) Session.Inventory.Add(_giveItem, Math.Max(1, _giveCount));
            foreach (var (id, count) in Session.Inventory.Items.ToList())
            {
                ImGui.PushID(id);
                if (ImGui.SmallButton("-")) Session.Inventory.Remove(id);
                ImGui.SameLine();
                string name = Session.Defs.TryGet<ItemDef>(id, out var def) ? def.DisplayName : id;
                ImGui.Text($"{name} x{count}");
                ImGui.PopID();
            }
        }

        if (ImGui.CollapsingHeader("World flags", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var (flag, value) in Session.World.Flags.ToList())
            {
                ImGui.PushID(flag);
                int v = value;
                ImGui.SetNextItemWidth(90);
                if (ImGui.InputInt("##v", ref v)) Session.World.Set(flag, v);
                ImGui.SameLine(); ImGui.Text(flag);
                ImGui.PopID();
            }
            ImGui.SetNextItemWidth(220);
            ImGui.InputTextWithHint("##nf", "new flag name", ref _newFlag, 100);
            ImGui.SameLine();
            if (ImGui.Button("Set") && _newFlag.Length > 0) { Session.World.Set(_newFlag, 1); _newFlag = ""; }
        }

        if (ImGui.CollapsingHeader("Stats"))
            foreach (var (k, v) in Session.Stats.Values) ImGui.Text($"{k}: {v:0.##}");

        if (ImGui.CollapsingHeader("Objects on this tile"))
            foreach (var p in Tile.Placements)
            {
                bool active = IsActive(p);
                ImGui.TextColored(active ? new Num.Vector4(0.6f, 1, 0.6f, 1) : new Num.Vector4(0.6f, 0.6f, 0.6f, 1),
                    $"{(active ? "active " : "hidden ")} {p.Id}: {p.Describe(Session.Defs)}");
            }
        ImGui.End();
    }
}
