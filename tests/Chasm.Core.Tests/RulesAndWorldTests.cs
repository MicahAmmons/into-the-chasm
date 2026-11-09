using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.Rules;
using Chasm.Core.Serialization;
using Chasm.Core.Session;
using Chasm.Core.World;

namespace Chasm.Core.Tests;

public class RulesAndWorldTests
{
    private sealed class FakeHost : IRuleHost
    {
        public List<string> Messages { get; } = new();
        public string? Tile;
        public void ShowMessage(IReadOnlyList<string> lines, string? speaker) => Messages.AddRange(lines);
        public void ChangeTile(string tileId, CellCoord? arrivalCell) => Tile = tileId;
        public void StartEncounter(string encounterId) { }
        public void StartDialogue(string dialogueId) { }
        public void Rest() { }
    }

    private static (GameSession, RuleContext, FakeHost) NewSession()
    {
        var defs = new DefRegistry();
        defs.Put(new ItemDef { Id = "berry", Name = "Glow Berry" });
        defs.Put(new ItemDef { Id = "turtle_flow", Name = "Turtle's Flow" });
        var s = new GameSession(defs);
        var host = new FakeHost();
        return (s, s.CreateContext(host), host);
    }

    [Fact]
    public void Feeding_the_turtle_rule_swaps_berry_for_flow()
    {
        var (s, ctx, host) = NewSession();
        var feed = new RuleDef
        {
            Conditions = { new HasItemCondition { Item = "berry" } },
            Outcomes =
            {
                new TakeItemOutcome { Item = "berry" },
                new GiveItemOutcome { Item = "turtle_flow" },
                new FlagOutcome { Flag = "turtle_fed" },
            },
        };
        var noBerry = new RuleDef { Outcomes = { new MessageOutcome { Pages = { "The turtle looks hungry." } } } };

        Rules.Rules.RunFirst(new[] { feed, noBerry }, ctx);
        Assert.Contains("The turtle looks hungry.", host.Messages);

        s.Inventory.Add("berry");
        Rules.Rules.RunFirst(new[] { feed, noBerry }, ctx);
        Assert.False(s.Inventory.Has("berry"));
        Assert.True(s.Inventory.Has("turtle_flow"));
        Assert.True(s.World.IsSet("turtle_fed"));
        Assert.Contains("Received Turtle's Flow.", host.Messages);
        Assert.Equal(1, s.Stats.Get("item.gained.turtle_flow"));
    }

    [Fact]
    public void Composite_conditions_and_gold()
    {
        var (s, ctx, _) = NewSession();
        var dragonDeal = new AllOfCondition
        {
            Conditions = { new GoldCondition { Amount = 1000 }, new NotCondition { Inner = new FlagCondition { Flag = "dragon_paid" } } },
        };
        Assert.False(dragonDeal.Evaluate(ctx));
        s.Inventory.AddGold(1200);
        Assert.True(dragonDeal.Evaluate(ctx));
        s.World.Set("dragon_paid", true);
        Assert.False(dragonDeal.Evaluate(ctx));
    }

    [Fact]
    public void Day_clock_rest_skips_half_a_day_and_rolls_over()
    {
        var clock = new DayClock { DayLengthSeconds = 100 };
        int newDays = 0;
        clock.NewDay += _ => newDays++;
        clock.Advance(60);
        clock.Rest();
        Assert.Equal(2, clock.Day);
        Assert.Equal(1, newDays);
        Assert.InRange(clock.Time, 0.09f, 0.11f);
    }

    [Fact]
    public void Walk_map_round_trips_every_cell()
    {
        var tile = new TileDef { Id = "t" };
        var rng = new Random(4);
        var expected = new Dictionary<CellCoord, bool>();
        foreach (var c in IsoGrid.AllCells) { bool w = rng.Next(2) == 0; expected[c] = w; tile.Walkable[c] = w; }
        tile.CommitWalkable();

        var copy = new TileDef { Id = "t", WalkMap = tile.WalkMap };
        foreach (var c in IsoGrid.AllCells) Assert.Equal(expected[c], copy.IsWalkable(c));
        Assert.Equal(34, tile.WalkMap.Count);
    }

    [Fact]
    public void Tile_with_conditional_item_pickup_round_trips_through_json()
    {
        var tile = new TileDef
        {
            Id = "f1_meadow",
            Placements =
            {
                new ItemPickupPlacement
                {
                    Id = "item_1", Cell = new CellCoord(12, 3), Item = "berry",
                    VisibleWhen = { new HasItemCondition { Item = "turtle_flow" } },
                    AlsoOnPickup = { new FlagOutcome { Flag = "found_berry" } },
                },
                new InteractablePlacement { Id = "bush_1", Label = "Bush", Rules = { new RuleDef { Outcomes = { new HidePlacementOutcome() } } } },
            },
        };
        string json = ChasmJson.Serialize(tile);
        var back = ChasmJson.Deserialize<TileDef>(json)!;
        var pickup = Assert.IsType<ItemPickupPlacement>(back.Placements[0]);
        Assert.IsType<HasItemCondition>(pickup.VisibleWhen[0]);
        Assert.IsType<FlagOutcome>(pickup.AlsoOnPickup[0]);
        Assert.IsType<HidePlacementOutcome>(((InteractablePlacement)back.Placements[1]).Rules[0].Outcomes[0]);
        Assert.Contains("\"$type\": \"ItemPickup\"", json);
    }

    [Fact]
    public void Validator_catches_broken_item_reference_inside_a_tile()
    {
        var defs = new DefRegistry();
        var tile = new TileDef
        {
            Id = "t",
            Placements = { new ItemPickupPlacement { Id = "a", Cell = new CellCoord(10, 2), Item = "nope" } },
        };
        defs.Put(tile);
        var problems = ContentValidator.Validate(tile, defs);
        Assert.Contains(problems, p => p.Contains("missing ItemDef 'nope'"));
    }

    [Fact]
    public void Save_round_trip_restores_session()
    {
        var (s, _, _) = NewSession();
        s.Inventory.Add("berry", 3);
        s.Inventory.AddGold(42);
        s.World.Set("vines_fallen", true);
        s.Stats.Add("player.deaths", 2);
        s.CurrentTile = "f1_meadow";
        string json = ChasmJson.Serialize(s.ToSave());

        var (s2, _, _) = NewSession();
        s2.LoadSave(ChasmJson.Deserialize<SaveData>(json)!);
        Assert.Equal(3, s2.Inventory.Count("berry"));
        Assert.Equal(42, s2.Inventory.Gold);
        Assert.True(s2.World.IsSet("vines_fallen"));
        Assert.Equal(2, s2.Stats.Get("player.deaths"));
        Assert.Equal("f1_meadow", s2.CurrentTile);
    }
}
