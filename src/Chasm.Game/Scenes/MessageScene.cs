using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Scenes;

/// <summary>A plain full-screen message. Placeholder start scene until the title screen exists.</summary>
public sealed class MessageScene(string title, string body) : Scene
{
    public override void Update(float dt) { }

    public override void Draw(SpriteBatch batch)
    {
        var d = Services.Draw;
        batch.Begin();
        d.TextCentered(batch, title, new Vector2(960, 480), Color.White, 64, bold: true);
        d.TextCentered(batch, body, new Vector2(960, 570), new Color(170, 180, 210), 26);
        batch.End();
    }
}
