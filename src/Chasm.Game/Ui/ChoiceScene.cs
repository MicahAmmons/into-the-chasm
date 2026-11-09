using Chasm.Game.Input;
using Chasm.Game.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Ui;

/// <summary>A small menu of choices over the current scene (Figure: Talk / Rest / Travel...).</summary>
public sealed class ChoiceScene(string title, IReadOnlyList<(string Label, Action Chosen)> options) : Scene
{
    public override bool DrawsSceneBelow => true;

    private Rectangle Row(int i) => new(760, 420 + 56 + i * 58, 400, 50);

    public override void Update(float dt)
    {
        var input = Services.Input;
        if (input.Pressed(InputAction.Cancel)) { Services.Scenes.Pop(); return; }
        for (int i = 0; i < options.Count && i < 9; i++)
            if (input.Pressed(InputAction.Quick1 + i)) { Choose(i); return; }
        if (!input.LeftClicked) return;
        var m = input.MouseCanvas.ToPoint();
        for (int i = 0; i < options.Count; i++)
            if (Row(i).Contains(m)) { Choose(i); return; }
        if (!new Rectangle(740, 400, 440, 80 + options.Count * 58).Contains(m)) Services.Scenes.Pop();
    }

    private void Choose(int i)
    {
        Services.Scenes.Pop();
        options[i].Chosen();
    }

    public override void Draw(SpriteBatch batch)
    {
        var d = Services.Draw;
        var m = Services.Input.MouseCanvas.ToPoint();
        batch.Begin();
        var box = new Rectangle(740, 400, 440, 80 + options.Count * 58);
        d.Rect(batch, box, new Color(10, 12, 22) * 0.94f);
        d.RectOutline(batch, box, Color.White * 0.5f, 2);
        d.TextCentered(batch, title, new Vector2(960, 440), new Color(255, 215, 140), 28, bold: true);
        for (int i = 0; i < options.Count; i++)
        {
            var r = Row(i);
            d.Rect(batch, r, r.Contains(m) ? new Color(70, 80, 120) : new Color(40, 45, 70));
            d.Text(batch, $"{i + 1}", new Vector2(r.X + 10, r.Y + 14), Color.Gray, 16);
            d.TextCentered(batch, options[i].Label, r.Center.ToVector2(), Color.White, 24);
        }
        batch.End();
    }
}
