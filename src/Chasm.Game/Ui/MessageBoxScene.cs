using Chasm.Game.Input;
using Chasm.Game.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Ui;

/// <summary>A message box over the current scene. Click / Space / E advances pages.</summary>
public sealed class MessageBoxScene(IReadOnlyList<string> pages, string? speaker, Action? onClosed = null) : Scene
{
    private int _page;
    private float _age;

    public override bool DrawsSceneBelow => true;

    public override void Update(float dt)
    {
        _age += dt;
        var input = Services.Input;
        if (_age > 0.15f && (input.LeftClicked || input.Pressed(InputAction.Confirm) || input.Pressed(InputAction.Interact)))
        {
            _page++;
            if (_page >= pages.Count) { Services.Scenes.Pop(); onClosed?.Invoke(); }
        }
        if (input.Pressed(InputAction.Cancel)) { Services.Scenes.Pop(); onClosed?.Invoke(); }
    }

    public override void Draw(SpriteBatch batch)
    {
        var d = Services.Draw;
        var box = new Rectangle(360, 780, 1200, 220);
        batch.Begin();
        d.Rect(batch, box, new Color(10, 12, 22) * 0.92f);
        d.RectOutline(batch, box, new Color(200, 210, 235) * 0.6f, 2);
        float y = box.Y + 24;
        if (!string.IsNullOrEmpty(speaker)) { d.Text(batch, speaker, new Vector2(box.X + 32, y), new Color(255, 215, 140), 26, bold: true); y += 40; }
        if (_page < pages.Count)
            foreach (var line in Wrap(pages[_page], 80)) { d.Text(batch, line, new Vector2(box.X + 32, y), Color.White, 26); y += 34; }
        string hint = _page < pages.Count - 1 ? $"click to continue  ({_page + 1}/{pages.Count})" : "click to close";
        d.Text(batch, hint, new Vector2(box.Right - d.Measure(hint, 16).X - 20, box.Bottom - 30), Color.Gray, 16);
        batch.End();
    }

    public static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (var para in text.Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' '))
            {
                if (line.Length + word.Length + 1 > width && line.Length > 0) { yield return line; line = ""; }
                line = line.Length == 0 ? word : line + " " + word;
            }
            yield return line;
        }
    }
}

/// <summary>A yes/no question over the current scene.</summary>
public sealed class ConfirmScene(string question, Action<bool> answered) : Scene
{
    private readonly Rectangle _yes = new(760, 580, 180, 60), _no = new(980, 580, 180, 60);
    public override bool DrawsSceneBelow => true;

    public override void Update(float dt)
    {
        var input = Services.Input;
        bool? result = null;
        if (input.LeftClicked)
        {
            var m = input.MouseCanvas.ToPoint();
            if (_yes.Contains(m)) result = true;
            else if (_no.Contains(m)) result = false;
        }
        if (input.Pressed(InputAction.Confirm) || input.Pressed(InputAction.Interact)) result = true;
        if (input.Pressed(InputAction.Cancel)) result = false;
        if (result is bool r) { Services.Scenes.Pop(); answered(r); }
    }

    public override void Draw(SpriteBatch batch)
    {
        var d = Services.Draw;
        var box = new Rectangle(660, 420, 600, 250);
        var mouse = Services.Input.MouseCanvas.ToPoint();
        batch.Begin();
        d.Rect(batch, box, new Color(10, 12, 22) * 0.94f);
        d.RectOutline(batch, box, new Color(200, 210, 235) * 0.6f, 2);
        float y = box.Y + 40;
        foreach (var line in MessageBoxScene.Wrap(question, 40)) { d.TextCentered(batch, line, new Vector2(960, y), Color.White, 28); y += 38; }
        foreach (var (rect, label) in new[] { (_yes, "Yes"), (_no, "No") })
        {
            d.Rect(batch, rect, rect.Contains(mouse) ? new Color(70, 80, 120) : new Color(40, 45, 70));
            d.RectOutline(batch, rect, Color.White * 0.5f);
            d.TextCentered(batch, label, rect.Center.ToVector2(), Color.White, 26);
        }
        batch.End();
    }
}
