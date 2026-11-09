using Chasm.Core.Content;

namespace Chasm.Core.Animation;

/// <summary>Which animation a character is playing. Missing states fall back sensibly (see AnimationRules).</summary>
public enum AnimState
{
    Idle, Walk, WalkUp, WalkDown, StartWalk, EndWalk,
    Attack, AttackUp, AttackDown, Cast, Hurt, Death, Summoned,
}

public enum Facing { Right, Left }

/// <summary>Optional shader used when drawing a layer.</summary>
public enum LayerShader { None, Smoke, CloudMovement }

/// <summary>
/// All animations for one character or effect. Same idea as the original system: each state has
/// one or more layers drawn on top of each other (body, shadow, glow...), each read from a row of a
/// sprite sheet.
/// </summary>
[DefFolder("animations", "Animations")]
[Tooltip("Sprite-sheet animations for a character or effect.")]
public sealed class AnimationDef : Def
{
    public List<AnimationStateDef> States { get; set; } = new();

    public AnimationStateDef? Find(AnimState state) => States.FirstOrDefault(s => s.State == state);
}

public sealed class AnimationStateDef
{
    public AnimState State { get; set; }

    [Tooltip("Drawn in list order: first is at the back.")]
    public List<AnimationLayerDef> Layers { get; set; } = new();

    public override string ToString() => $"{State} ({Layers.Count} layer{(Layers.Count == 1 ? "" : "s")})";
}

public sealed class AnimationLayerDef
{
    [Texture, Required, Tooltip("Sprite sheet (relative to content/art).")]
    public string Texture { get; set; } = "";

    [Tooltip("Row of the sheet where this animation starts (1 = top row). Frames continue onto following rows if a row runs out.")]
    [Range(1, 64)] public int Row { get; set; } = 1;

    [Range(1, 256)] public int FrameCount { get; set; } = 1;
    [Range(1, 2048)] public int FrameWidth { get; set; } = 64;
    [Range(1, 2048)] public int FrameHeight { get; set; } = 64;

    [Tooltip("How long each frame shows, in milliseconds.")]
    [Range(1, 10000)] public int FrameDurationMs { get; set; } = 100;

    [Tooltip("Repeat forever. Off = play once and hold the last frame.")]
    public bool Loop { get; set; } = true;

    [Tooltip("Play forward then backward (1-2-3-2-1).")]
    public bool PingPong { get; set; }

    [Tooltip("Play the frames in reverse order.")]
    public bool Reverse { get; set; }

    [Tooltip("Extra frames to hold the last frame before repeating.")]
    [Range(0, 600)] public int EndPauseFrames { get; set; }

    [Tooltip("Blank frames before the animation starts (staggers layers).")]
    [Range(0, 600)] public int StartPauseFrames { get; set; }

    [Tooltip("Which way the art faces in the sheet. It is mirrored automatically when facing the other way.")]
    public Facing ArtFaces { get; set; } = Facing.Right;

    [Tooltip("Pixels to raise the sprite above the cell centre (so feet sit on the cell).")]
    public int YOffset { get; set; }

    [Tooltip("Blend smoothly into the next frame (the old 'FadeEffect').")]
    public bool Crossfade { get; set; }

    public LayerShader Shader { get; set; } = LayerShader.None;

    [Tooltip("Draw scale (1 = original pixel size).")]
    [Range(0.1, 8)] public float Scale { get; set; } = 1f;

    /// <summary>The frame order after reverse / pauses / ping-pong; -1 means "draw nothing" (start pause).</summary>
    public int[] BuildSequence() => FrameSequence.Build(FrameCount, Reverse, EndPauseFrames, PingPong, StartPauseFrames);
}

/// <summary>Pure frame-order logic, identical to the original Animation.FinalizeRectangleList.</summary>
public static class FrameSequence
{
    public static int[] Build(int frameCount, bool reverse, int endPause, bool pingPong, int startPause)
    {
        var frames = Enumerable.Range(0, Math.Max(1, frameCount)).ToList();
        if (reverse) frames.Reverse();
        int last = frames[^1];
        for (int i = 0; i < endPause; i++) frames.Add(last);
        if (pingPong)
            for (int i = frames.Count - 2; i >= 1; i--) frames.Add(frames[i]);
        var result = new List<int>();
        for (int i = 0; i < startPause; i++) result.Add(-1);
        result.AddRange(frames);
        return result.ToArray();
    }
}

/// <summary>Plays one layer: advances time, loops or holds. Pure (no drawing).</summary>
public sealed class LayerClock
{
    private readonly int[] _sequence;
    private readonly float _frameSeconds;
    private readonly bool _loop;
    private float _timer;
    private int _position;

    public LayerClock(AnimationLayerDef def, float speedMultiplier = 1f)
    {
        _sequence = def.BuildSequence();
        _frameSeconds = Math.Max(0.001f, def.FrameDurationMs / 1000f / Math.Max(0.01f, speedMultiplier));
        _loop = def.Loop;
    }

    /// <summary>Frame index into the sheet, or -1 for "nothing this frame".</summary>
    public int Frame => _sequence[_position];
    public int NextFrame => _sequence[_loop ? (_position + 1) % _sequence.Length : Math.Min(_position + 1, _sequence.Length - 1)];
    /// <summary>0..1 progress through the current frame (for crossfading).</summary>
    public float FrameProgress => _timer / _frameSeconds;
    public bool Finished { get; private set; }
    public int Position => _position;
    public int Length => _sequence.Length;

    public void Reset() { _timer = 0; _position = 0; Finished = false; }

    public void Update(float dt)
    {
        if (Finished) return;
        _timer += dt;
        while (_timer >= _frameSeconds)
        {
            _timer -= _frameSeconds;
            if (_position + 1 < _sequence.Length) _position++;
            else if (_loop) _position = 0;
            else { Finished = true; _timer = 0; return; }
        }
    }
}

/// <summary>Which state to actually play when the requested one isn't defined.</summary>
public static class AnimationRules
{
    public static AnimState? Resolve(AnimationDef def, AnimState wanted)
    {
        foreach (var s in Chain(wanted))
            if (def.Find(s) is { Layers.Count: > 0 }) return s;
        return def.States.FirstOrDefault(s => s.Layers.Count > 0)?.State;
    }

    private static IEnumerable<AnimState> Chain(AnimState s)
    {
        yield return s;
        switch (s)
        {
            case AnimState.WalkUp or AnimState.WalkDown: yield return AnimState.Walk; break;
            case AnimState.AttackUp or AnimState.AttackDown: yield return AnimState.Attack; yield return AnimState.Cast; break;
            case AnimState.Cast: yield return AnimState.Attack; break;
            case AnimState.StartWalk or AnimState.EndWalk: yield return AnimState.Walk; break;
        }
        yield return AnimState.Idle;
    }
}
