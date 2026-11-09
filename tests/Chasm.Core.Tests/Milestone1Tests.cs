using Chasm.Core.Combat;
using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.Rules;
using Chasm.Core.Session;
using Chasm.Core.World;

namespace Chasm.Core.Tests;

/// <summary>Plays the Floor 1 quest chain using the real content files, without graphics.</summary>
public class Milestone1Tests
{
    private sealed class Host : IRuleHost
    {
        public List<string> Log { get; } = new();
        public string? MovedTo;
        public void ShowMessage(IReadOnlyList<string> lines, string? speaker) => Log.AddRange(lines);
        public void ChangeTile(string tileId, CellCoord? arrivalCell) => MovedTo = tileId;
        public void StartEncounter(string encounterId) { }
        public void StartDialogue(string dialogueId) { }
        public void Rest() { }
        public void Confirm(string question, Action<bool> answered) { Log.Add("?" + question); answered(true); }
    }

    private static DefRegistry Content()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "IntoTheChasm.sln"))) dir = dir.Parent;
        var reg = new DefRegistry();
        reg.LoadAll(Path.Combine(dir.FullName, "content", "defs"));
        return reg;
    }

    private static RuleContext Ctx(GameSession s, Host h, string tile, string? placement)
    {
        s.CurrentTile = tile;
        var c = s.CreateContext(h);
        c.SourcePlacementId = placement;
        return c;
    }

    [Fact]
    public void Berry_turtle_flow_aspect_jump()
    {
        var defs = Content();
        var s = new GameSession(defs);
        var h = new Host();
        var figure = (FigurePlacement)defs.Get<TileDef>("f1_0_0").FindPlacement("figure_1")!;

        // 1. The Figure gives the quest.
        Rules.Rules.RunFirst(figure.TalkRules, Ctx(s, h, "f1_0_0", "figure_1"));
        Assert.True(s.World.IsSet("figure_quest_given"));

        // 2. The berry appears (it's only visible after the quest) and is picked up.
        var berry = (ItemPickupPlacement)defs.Get<TileDef>("f1_m1_0").FindPlacement("item_berry")!;
        Assert.True(Rules.Rules.AllPass(berry.VisibleWhen, Ctx(s, h, "f1_m1_0", berry.Id)));
        s.Inventory.Add(berry.Item);

        // 3. Feed the turtle: berry -> flow.
        var turtle = (InteractablePlacement)defs.Get<TileDef>("f1_0_m1").FindPlacement("turtle_beast")!;
        Rules.Rules.RunFirst(turtle.Rules, Ctx(s, h, "f1_0_m1", turtle.Id));
        Assert.False(s.Inventory.Has("glow_berry"));
        Assert.True(s.Inventory.Has("turtle_flow"));

        // 4. Use the flow: unlocks the turtle summon.
        Rules.Rules.RunFirst(defs.Get<ItemDef>("turtle_flow").UseRules, Ctx(s, h, "f1_0_m1", null));
        Assert.True(s.Progress.HasSummon("turtle"));
        Assert.False(s.Inventory.Has("turtle_flow"));

        // 5. Summon it in fights until the aspect is learned (4 summons x 10 xp).
        var turtleDef = defs.Get<MonsterDef>("turtle");
        for (int i = 0; i < 4; i++) s.Progress.AddAffinity(turtleDef, turtleDef.Summon.XpPerSummon);
        Assert.Contains("aspect_of_the_turtle", s.Progress.Aspects);

        // 6. Without the aspect active, the ledge only explains the wind.
        var ledge = (InteractablePlacement)defs.Get<TileDef>("f1_m1_m1").FindPlacement("windy_ledge")!;
        Rules.Rules.RunFirst(ledge.Rules, Ctx(s, h, "f1_m1_m1", ledge.Id));
        Assert.Null(h.MovedTo);

        // 7. Heavy as stone: the ledge asks to jump, and down we go.
        s.Progress.ActiveAspect = "aspect_of_the_turtle";
        Rules.Rules.RunFirst(ledge.Rules, Ctx(s, h, "f1_m1_m1", ledge.Id));
        Assert.Equal("f2_0_0", h.MovedTo);
        Assert.True(s.World.IsSet("reached_floor_2"));
    }
}
