using Chasm.Core.Stats;
using Chasm.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Debug;

/// <summary>
/// The hidden stats overlay (F3 by default). Lists every lifetime counter, grouped by the first
/// part of the key, plus any extra debug lines a scene wants to show.
/// </summary>
public sealed class StatsOverlay
{
    public bool Visible { get; set; }
    public List<string> ExtraLines { get; } = new();

    public void Draw(SpriteBatch batch, Draw draw, StatsTracker stats)
    {
        if (!Visible) return;
        var lines = new List<(string text, Color color)> { ("STATS (F3)", Color.Gold) };
        foreach (var line in ExtraLines) lines.Add((line, new Color(150, 220, 255)));
        string? group = null;
        foreach (var (key, value) in stats.Values)
        {
            string g = key.Split('.')[0];
            if (g != group) { group = g; lines.Add((g.ToUpperInvariant(), new Color(255, 200, 120))); }
            lines.Add(($"  {key[(g.Length + 1)..]}: {value:0.##}", Color.White));
        }
        if (stats.Values.Count == 0) lines.Add(("  (nothing tracked yet)", Color.Gray));

        int h = lines.Count * 20 + 16;
        batch.Begin();
        draw.Rect(batch, new Rectangle(12, 12, 420, h), Color.Black * 0.7f);
        float y = 20;
        foreach (var (text, color) in lines) { draw.Text(batch, text, new Vector2(24, y), color, 16); y += 20; }
        batch.End();
    }
}
