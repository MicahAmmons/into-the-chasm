using Chasm.Core.Content;
using Chasm.Core.Serialization;

namespace Chasm.Core.Tests;

[DefFolder("test_things", "Test Things")]
public sealed class TestThingDef : Def
{
    [Ref(typeof(TestThingDef))] public string? Partner { get; set; }
    [Required] public string Flavour { get; set; } = "";
    public List<TestShape> Shapes { get; set; } = new();
}

[Polymorphic] public abstract class TestShape { }
[TypeId("circle")] public sealed class TestCircle : TestShape { public float Radius { get; set; } }
public sealed class TestSquare : TestShape { public float Side { get; set; } }

public class DefRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chasm-tests-" + Guid.NewGuid().ToString("N"));

    public DefRegistryTests() => ChasmJson.RegisterAssembly(typeof(DefRegistryTests).Assembly);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void Save_then_load_round_trips_including_polymorphic_lists()
    {
        var reg = new DefRegistry();
        reg.LoadAll(_root);
        reg.Save(new TestThingDef
        {
            Id = "a", Name = "A", Flavour = "salty",
            Shapes = { new TestCircle { Radius = 2 }, new TestSquare { Side = 3 } },
        });

        var reloaded = new DefRegistry();
        reloaded.LoadAll(_root);
        var a = reloaded.Get<TestThingDef>("a");
        Assert.Equal("salty", a.Flavour);
        Assert.IsType<TestCircle>(a.Shapes[0]);
        Assert.Equal(3, ((TestSquare)a.Shapes[1]).Side);
        Assert.Contains("\"$type\": \"circle\"", File.ReadAllText(reloaded.PathFor(typeof(TestThingDef), "a")));
    }

    [Fact]
    public void Validator_reports_missing_refs_and_required_fields()
    {
        var reg = new DefRegistry();
        var def = new TestThingDef { Id = "b", Partner = "nobody" };
        reg.Put(def);
        var problems = ContentValidator.Validate(def, reg);
        Assert.Contains(problems, p => p.Contains("missing TestThingDef 'nobody'"));
        Assert.Contains(problems, p => p.Contains("Flavour is required"));
    }

    [Fact]
    public void SuggestId_makes_snake_case_and_avoids_collisions()
    {
        var reg = new DefRegistry();
        Assert.Equal("turtle_berry", reg.SuggestId(typeof(TestThingDef), "Turtle Berry!"));
        reg.Put(new TestThingDef { Id = "turtle_berry", Flavour = "x" });
        Assert.Equal("turtle_berry_2", reg.SuggestId(typeof(TestThingDef), "Turtle Berry"));
    }
}
