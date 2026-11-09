using Chasm.Core.Animation;
using Chasm.Core.Events;
using Chasm.Core.Stats;

namespace Chasm.Core.Tests;

public class AnimationAndStatsTests
{
    [Fact]
    public void Sequence_matches_original_rules()
    {
        Assert.Equal(new[] { 0, 1, 2, 1 }, FrameSequence.Build(3, false, 0, true, 0));
        Assert.Equal(new[] { -1, 2, 1, 0, 0, 0 }, FrameSequence.Build(3, true, 2, false, 1));
    }

    [Fact]
    public void Non_looping_layer_holds_last_frame_and_finishes()
    {
        var clock = new LayerClock(new AnimationLayerDef { FrameCount = 3, FrameDurationMs = 100, Loop = false });
        clock.Update(0.35f);
        Assert.True(clock.Finished);
        Assert.Equal(2, clock.Frame);
    }

    [Fact]
    public void Looping_layer_wraps()
    {
        var clock = new LayerClock(new AnimationLayerDef { FrameCount = 3, FrameDurationMs = 100, Loop = true });
        clock.Update(0.31f);
        Assert.Equal(0, clock.Frame);
        Assert.False(clock.Finished);
    }

    [Fact]
    public void Missing_walk_up_falls_back_to_walk_then_idle()
    {
        var def = new AnimationDef
        {
            States =
            {
                new AnimationStateDef { State = AnimState.Idle, Layers = { new AnimationLayerDef() } },
                new AnimationStateDef { State = AnimState.Walk, Layers = { new AnimationLayerDef() } },
            },
        };
        Assert.Equal(AnimState.Walk, AnimationRules.Resolve(def, AnimState.WalkUp));
        Assert.Equal(AnimState.Idle, AnimationRules.Resolve(def, AnimState.Hurt));
    }

    private sealed record Hit(int Amount) : IStatEvent
    {
        public IEnumerable<(string Key, double Amount)> StatChanges => new[] { (StatKeys.DamageTaken, (double)Amount) };
    }

    [Fact]
    public void Stats_tracker_counts_events_from_the_bus()
    {
        var bus = new EventBus();
        var stats = new StatsTracker();
        stats.Attach(bus);
        bus.Publish(new Hit(3));
        bus.Publish(new Hit(4));
        Assert.Equal(7, stats.Get(StatKeys.DamageTaken));
    }
}
