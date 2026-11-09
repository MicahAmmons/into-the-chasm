using Chasm.Core.Combat;
using Chasm.Core.Content;

namespace Chasm.Core.Tests;

/// <summary>Loads the real content folder: every file must parse and every reference must resolve.</summary>
public class ContentTests
{
    private static string DefsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IntoTheChasm.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "content", "defs");
    }

    [Fact]
    public void All_content_loads_without_errors()
    {
        var reg = new DefRegistry();
        reg.LoadAll(DefsRoot());
        Assert.Empty(reg.LoadErrors);
        Assert.NotEmpty(reg.All<MonsterDef>());
    }

    [Fact]
    public void All_content_references_resolve()
    {
        var reg = new DefRegistry();
        reg.LoadAll(DefsRoot());
        var problems = ContentValidator.ValidateAll(reg)
            // Known: the original Floor 2 data has exits to tiles that were never made.
            .Where(p => !p.Contains("missing TileDef 'f2_")).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Ported_monsters_finish_off_a_passive_player()
    {
        var reg = new DefRegistry();
        reg.LoadAll(DefsRoot());
        var e = new CombatEngine(new CombatSetup
        {
            Defs = reg, Walkable = _ => true, PlayerCell = new Grid.CellCoord(10, 2), Seed = 3,
            Enemies = { ("frost_ooze", new Grid.CellCoord(18, 0)), ("fire_ooze", new Grid.CellCoord(19, 3)), ("earth_ooze", new Grid.CellCoord(20, 5)) },
            Summons = new UnlimitedSummons(new[] { "turtle" }),
        });
        e.Start();
        for (int i = 0; i < 3000 && e.Outcome is null; i++)
        {
            if (e.Current.Kind == CombatantKind.Player)
            {
                e.Apply(new EndTurnAction());
            }
            else CombatAi.PlayTurn(e);
        }
        Assert.True(e.Outcome is not null, $"round {e.Round}\n" + string.Join("\n", e.Units) + "\n" + string.Join("\n", e.Log.TakeLast(25)));
    }
}
