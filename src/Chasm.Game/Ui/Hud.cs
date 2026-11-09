using Chasm.Core.Session;
using Chasm.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Ui;

/// <summary>Always-on HUD pieces: the day counter with the scrolling day/night bar.</summary>
public static class Hud
{
    public static void DrawDay(SpriteBatch b, Draw d, DayClock clock)
    {
        var label = $"Day {clock.Day}";
        d.TextShadowed(b, label, new Vector2(1500, 18), Color.White, 26, bold: true);
        var bar = new Rectangle(1620, 26, 270, 18);
        d.Rect(b, bar, Color.Black * 0.6f);
        // Day portion then night portion; a marker shows where we are. Icons are placeholders: sun / moon glyphs.
        int nightX = bar.X + (int)(bar.Width * clock.NightStartsAt);
        d.Rect(b, new Rectangle(bar.X, bar.Y, nightX - bar.X, bar.Height), new Color(240, 200, 110) * 0.55f);
        d.Rect(b, new Rectangle(nightX, bar.Y, bar.Right - nightX, bar.Height), new Color(70, 80, 160) * 0.7f);
        int markerX = bar.X + (int)(bar.Width * clock.Time);
        d.Rect(b, new Rectangle(markerX - 2, bar.Y - 4, 4, bar.Height + 8), Color.White);
        d.RectOutline(b, bar, Color.White * 0.5f);
        d.Text(b, clock.IsNight ? "night" : "day", new Vector2(bar.X, bar.Bottom + 2), Color.White * 0.7f, 14);
    }
}
