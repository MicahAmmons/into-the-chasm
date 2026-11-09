using Chasm.Core.Content;
using Chasm.Core.Events;
using Chasm.Core.Serialization;
using Chasm.Game.Assets;
using Chasm.Game.Input;
using Chasm.Game.Rendering;
using Chasm.Game.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Hosting;

/// <summary>
/// The MonoGame host. Owns the window, the frame loop and the shared services, and nothing else:
/// all behaviour lives in scenes.
///
/// Frame order: hot reload -> input -> host extension (ImGui) -> apply scene changes -> update
/// scenes -> draw scenes into canvas -> present canvas -> top scene overlay UI -> host extension.
/// </summary>
public sealed class ChasmGame : Microsoft.Xna.Framework.Game
{
    private readonly GameOptions _options;
    private readonly Func<GameServices, Scene> _firstScene;
    private readonly IHostExtension? _extension;
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _batch = null!;
    private int _frameCount;
    private bool _screenshotTaken;

    public GameServices Svc { get; private set; } = null!;
    public GameOptions Options => _options;

    public ChasmGame(GameOptions options, Func<GameServices, Scene> firstScene, IHostExtension? extension = null)
    {
        _options = options;
        _firstScene = firstScene;
        _extension = extension;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = options.WindowWidth,
            PreferredBackBufferHeight = options.WindowHeight,
            SynchronizeWithVerticalRetrace = true,
            GraphicsProfile = GraphicsProfile.HiDef,
        };
        IsMouseVisible = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        Window.Title = options.WindowTitle;
        Window.AllowUserResizing = true;
        ChasmJson.RegisterAssembly(typeof(ChasmGame).Assembly);
        foreach (var asm in options.ContentAssemblies) ChasmJson.RegisterAssembly(asm);
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        var paths = ContentPaths.Resolve();
        var defs = new DefRegistry();
        defs.LoadAll(paths.Defs);
        foreach (var err in defs.LoadErrors) Console.WriteLine($"[content] {err}");
        var textures = new TextureStore(GraphicsDevice, paths);
        var fonts = new FontStore(paths);
        Svc = new GameServices
        {
            Game = this,
            Graphics = GraphicsDevice,
            Paths = paths,
            Defs = defs,
            DefReload = new DefHotReload(defs, paths.Defs),
            Textures = textures,
            Fonts = fonts,
            Draw = new Draw(GraphicsDevice, textures, fonts),
            Canvas = new Canvas(GraphicsDevice),
            Input = new InputState(),
            Events = new EventBus(),
        };
        Svc.Scenes = new SceneStack(Svc);
        Console.WriteLine($"[content] root: {paths.Root} (dev checkout: {paths.IsDevelopmentCheckout})");
        _extension?.Initialize(Svc);
        Svc.Scenes.Push(_firstScene(Svc));
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Svc.TotalTime = gameTime.TotalGameTime.TotalSeconds;
        Svc.Textures.ProcessHotReload();
        Svc.DefReload.Process();
        Svc.Input.Update(Svc.Canvas, IsActive);
        _extension?.BeginFrame(dt);
        Svc.Scenes.ApplyPending();
        if (Svc.Scenes.Count == 0) { Exit(); return; }
        Svc.Scenes.Update(dt);

        _frameCount++;
        if (_options.ExitAfterFrames is int limit && _frameCount >= limit && (_options.ScreenshotPath is null || _screenshotTaken))
        {
            Console.WriteLine($"[smoke-test] Ran {_frameCount} frames, exiting cleanly.");
            Exit();
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        Svc.Canvas.Begin();
        GraphicsDevice.Clear(new Color(18, 20, 32));
        Svc.Scenes.Draw(_batch);
        Svc.Canvas.Present(_batch);
        Svc.Scenes.Top?.DrawOverlayUi();
        _extension?.EndFrame();
        if (_options.ScreenshotPath is { } shot && !_screenshotTaken && _frameCount >= (_options.ExitAfterFrames ?? 0) - 5)
        {
            _screenshotTaken = true;
            SaveScreenshot(shot);
        }
        base.Draw(gameTime);
    }

    /// <summary>Saves the canvas to a PNG (used by automated checks and a debug hotkey later).</summary>
    public void SaveScreenshot(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var pp = GraphicsDevice.PresentationParameters;
        var data = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
        GraphicsDevice.GetBackBufferData(data);
        using var tex = new Texture2D(GraphicsDevice, pp.BackBufferWidth, pp.BackBufferHeight);
        tex.SetData(data);
        using var fs = File.Create(path);
        tex.SaveAsPng(fs, tex.Width, tex.Height);
        Console.WriteLine($"[screenshot] {path}");
    }

    protected override void UnloadContent()
    {
        _extension?.Dispose();
        Svc.DefReload.Dispose();
        Svc.Textures.Dispose();
        Svc.Canvas.Dispose();
        base.UnloadContent();
    }
}
