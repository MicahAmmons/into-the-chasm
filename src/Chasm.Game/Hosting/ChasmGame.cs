using Chasm.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Hosting;

/// <summary>
/// The MonoGame host. It owns the window and the frame loop and nothing else: game behaviour lives
/// in scenes (added in step 2), so this class should stay small for the life of the project.
/// </summary>
public sealed class ChasmGame : Microsoft.Xna.Framework.Game
{
    private readonly GameOptions _options;
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch? _spriteBatch;
    private int _frameCount;

    public ChasmGame(GameOptions options)
    {
        _options = options;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = GameInfo.CanvasWidth,
            PreferredBackBufferHeight = GameInfo.CanvasHeight,
            SynchronizeWithVerticalRetrace = true,
        };
        IsMouseVisible = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        Window.Title = options.WindowTitle;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        _frameCount++;
        if (_options.ExitAfterFrames is int limit && _frameCount >= limit)
        {
            Console.WriteLine($"[smoke-test] Ran {_frameCount} frames, exiting cleanly.");
            Exit();
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // Placeholder: the chasm-sky colour. Real drawing arrives with scenes and the renderer.
        GraphicsDevice.Clear(new Color(18, 20, 32));
        base.Draw(gameTime);
    }
}
