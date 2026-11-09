using System.Numerics;
using Chasm.Core.Animation;
using Chasm.Core.Grid;
using Chasm.Game.Animation;
using Chasm.Game.Hosting;
using Chasm.Game.Rendering;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace Chasm.Game.World;

/// <summary>The player walking around a Tile out of combat: follows waypoints and animates.</summary>
public sealed class PlayerAvatar
{
    private readonly List<Vector2> _path = new();

    public Vector2 Position { get; set; }
    public Facing Facing { get; private set; } = Facing.Right;
    public AnimationPlayer Animation { get; }
    public float Speed { get; set; } = 220;
    public bool IsMoving => _path.Count > 0;
    public IReadOnlyList<Vector2> Path => _path;

    /// <summary>Called with the distance walked this frame (for the distance stat).</summary>
    public event Action<float>? Walked;
    /// <summary>Called when the path finishes.</summary>
    public event Action? Arrived;

    public PlayerAvatar(AnimationDef? def)
    {
        Animation = new AnimationPlayer(def) { PlaceholderSize = new Microsoft.Xna.Framework.Point(56, 110) };
    }

    public CellCoord Cell => IsoGrid.CellAt(Position);

    public void SetPath(IEnumerable<Vector2> waypoints)
    {
        _path.Clear();
        _path.AddRange(waypoints);
    }

    public void Stop() => _path.Clear();

    public void Update(float dt)
    {
        if (_path.Count > 0)
        {
            float budget = Speed * dt, walked = 0;
            Vector2 dir = Vector2.Zero;
            while (budget > 0 && _path.Count > 0)
            {
                var to = _path[0] - Position;
                float len = to.Length();
                if (len <= budget) { Position = _path[0]; _path.RemoveAt(0); budget -= len; walked += len; if (len > 0.01f) dir = to; }
                else { dir = to; Position += to / len * budget; walked += budget; budget = 0; }
            }
            if (dir != Vector2.Zero)
            {
                if (MathF.Abs(dir.X) > 0.01f) Facing = dir.X < 0 ? Facing.Left : Facing.Right;
                Animation.Play(dir.Y < 0 ? AnimState.WalkUp : AnimState.WalkDown);
            }
            Walked?.Invoke(walked);
            if (_path.Count == 0) { Animation.Play(AnimState.Idle); Arrived?.Invoke(); }
        }
        Animation.Update(dt);
    }

    public void Draw(RenderQueue q, GameServices s) =>
        Animation.Draw(q, s.Textures, s.Draw, Position, Facing, XnaColor.White, "Player");
}
