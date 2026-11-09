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

    static ChasmGame()
    {
        // MonoGame's SDL library has a different file name per platform; let our own SDL calls find it.
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(ChasmGame).Assembly, (name, asm, path) =>
        {
            if (name != "SDL2") return IntPtr.Zero;
            foreach (var candidate in new[] { "SDL2", "libSDL2-2.0.so.0", "libSDL2.so", "libSDL2-2.0.0.dylib", "libSDL2.dylib" })
                if (System.Runtime.InteropServices.NativeLibrary.TryLoad(candidate, asm, path, out var handle)) return handle;
            return IntPtr.Zero;
        });
    }

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

    protected override void Initialize()
    {
        base.Initialize();
        Diagnostics.Log.Start(Window.Title);
        Window.ClientSizeChanged += OnClientSizeChanged;
        Exiting += (_, _) => Svc?.Scenes.ExitAll();
        Activated += (_, _) => { Diagnostics.Log.Write($"Focus gained (IsActive={IsActive}, window {Window.ClientBounds})"); _checkOnScreen = 3; Svc?.TileVisuals.InvalidateAll(); };
        Deactivated += (_, _) => Diagnostics.Log.Write("Focus lost");
        GraphicsDevice.DeviceReset += (_, _) => { Diagnostics.Log.Write("Graphics device reset"); Svc?.TileVisuals.InvalidateAll(); };
        FitWindowToScreen();
    }

    private bool _resizing;
    private int _sizeMismatchFrames;

    /// <summary>
    /// Whether the window has the keyboard/mouse. MonoGame's IsActive can get stuck "false" after
    /// alt-tabbing on some Windows setups, which made every click and key press ignored; so we also
    /// ask SDL directly whether the window has input focus.
    /// </summary>
    public bool HasFocus
    {
        get
        {
            if (IsActive) return true;
            if (_sdlFlagsBroken) return true; // can't tell: never block input
            try { return (SDL_GetWindowFlags(Window.Handle) & (SdlWindowInputFocus | SdlWindowMouseFocus)) != 0; }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { _sdlFlagsBroken = true; return true; }
        }
    }

    private bool _sdlFlagsBroken;
    private const uint SdlWindowInputFocus = 0x200, SdlWindowMouseFocus = 0x400;

    [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern uint SDL_GetWindowFlags(IntPtr window);

    /// <summary>
    /// "Turn it off and on again" for the display (F9, or Tools > Reset display): resizes the back
    /// buffer to the window, rebuilds the canvas and re-bakes all tile art.
    /// </summary>
    public void ResetDisplay()
    {
        Diagnostics.Log.Write($"Display reset requested (window {Window.ClientBounds.Size}, backbuffer {GraphicsDevice.PresentationParameters.BackBufferWidth}x{GraphicsDevice.PresentationParameters.BackBufferHeight})");
        SyncBackBuffer(force: true);
        EnsureOnScreen();
        Svc?.Canvas.Rebuild();
        Svc?.TileVisuals.InvalidateAll();
    }

    /// <summary>
    /// Keeps the back buffer the same size as the window. Without this, maximizing, restoring or
    /// alt-tabbing can leave the drawing surface at the old size and the picture looks scrambled.
    /// </summary>
    private void OnClientSizeChanged(object? sender, EventArgs e)
    {
        Diagnostics.Log.Write($"Window resized to {Window.ClientBounds.Size}");
        SyncBackBuffer();
    }

    private void SyncBackBuffer(bool force = false)
    {
        if (_resizing) return;
        // While minimized Windows parks the window at -32000,-32000; resizing then makes MonoGame
        // re-centre it relative to that spot, which threw the window far off-screen after alt-tab.
        if (IsMinimized) return;
        var bounds = Window.ClientBounds;
        if (bounds.Width < 200 || bounds.Height < 150) return; // minimized / mid-transition: never shrink the surface to nothing
        var pp = GraphicsDevice.PresentationParameters;
        if (!force && pp.BackBufferWidth == bounds.Width && pp.BackBufferHeight == bounds.Height) return;
        _resizing = true;
        _graphics.PreferredBackBufferWidth = bounds.Width;
        _graphics.PreferredBackBufferHeight = bounds.Height;
        _graphics.ApplyChanges();
        _resizing = false;
        Svc?.Canvas.UpdateDestination();
        _checkOnScreen = 3;
    }

    // ---------------------------------------------------------------- keep the window on screen

    private int _checkOnScreen;
    private const uint SdlWindowMinimized = 0x40, SdlWindowHidden = 0x8, SdlWindowMaximized = 0x80;

    private uint WindowFlags()
    {
        if (_sdlFlagsBroken) return 0;
        try { return SDL_GetWindowFlags(Window.Handle); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { _sdlFlagsBroken = true; return 0; }
    }

    public bool IsMinimized => (WindowFlags() & (SdlWindowMinimized | SdlWindowHidden)) != 0;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SdlRect { public int X, Y, W, H; }

    [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int SDL_GetNumVideoDisplays();
    [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int SDL_GetDisplayUsableBounds(int displayIndex, out SdlRect rect);
    [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern void SDL_GetWindowPosition(IntPtr window, out int x, out int y);
    [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern void SDL_SetWindowPosition(IntPtr window, int x, int y);

    /// <summary>
    /// If the window's title bar isn't reachable on any monitor, bring the window back to the
    /// middle of the main screen (shrinking it to fit if needed). Runs after focus/resize changes
    /// and from F9 / Tools > Reset display.
    /// </summary>
    public void EnsureOnScreen()
    {
        if (_options.ExitAfterFrames is not null || IsMinimized) return;
        try
        {
            if ((WindowFlags() & SdlWindowMaximized) != 0) return;
            SDL_GetWindowPosition(Window.Handle, out int x, out int y);
            var size = Window.ClientBounds.Size;
            int displays = Math.Max(1, SDL_GetNumVideoDisplays());
            for (int i = 0; i < displays; i++)
            {
                if (SDL_GetDisplayUsableBounds(i, out var r) != 0) continue;
                // Title bar strip (window top, 200px of its width) must overlap the monitor.
                int overlapX = Math.Min(x + Math.Min(size.X, 400), r.X + r.W) - Math.Max(x, r.X);
                bool topVisible = y >= r.Y - 10 && y < r.Y + r.H - 40;
                if (overlapX >= 100 && topVisible) return;
            }
            if (SDL_GetDisplayUsableBounds(0, out var main) != 0) return;
            int w = Math.Min(size.X, main.W - 20), h = Math.Min(size.Y, main.H - 60);
            if (w != size.X || h != size.Y)
            {
                _resizing = true;
                _graphics.PreferredBackBufferWidth = w;
                _graphics.PreferredBackBufferHeight = h;
                _graphics.ApplyChanges();
                _resizing = false;
                Svc?.Canvas.UpdateDestination();
            }
            int nx = main.X + (main.W - w) / 2, ny = main.Y + Math.Max(30, (main.H - h) / 2);
            SDL_SetWindowPosition(Window.Handle, nx, ny);
            Diagnostics.Log.Write($"Window was off-screen at {x},{y}; moved back to {nx},{ny}");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>
    /// Opens at the requested size (1920x1080) centred on the screen. If the screen can't fit that
    /// plus the title bar and taskbar, the window is maximized instead so nothing hangs off-screen.
    /// </summary>
    private void FitWindowToScreen()
    {
        if (_options.ExitAfterFrames is not null) return; // automated runs: leave as-is
        var display = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        bool tooBig = _options.WindowWidth > display.Width - 40 || _options.WindowHeight > display.Height - 100;
        if (tooBig && _options.MaximizeIfTooBig)
        {
            try { SDL_MaximizeWindow(Window.Handle); return; }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                // Fallback: fill the screen minus room for the title bar and taskbar.
                _graphics.PreferredBackBufferWidth = display.Width;
                _graphics.PreferredBackBufferHeight = display.Height - 100;
                _graphics.ApplyChanges();
                Window.Position = new Point(0, 30);
                return;
            }
        }
        Window.Position = new Point(Math.Max(0, (display.Width - _options.WindowWidth) / 2), Math.Max(0, (display.Height - _options.WindowHeight) / 2));
    }

    [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern void SDL_MaximizeWindow(IntPtr window);

    [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern void SDL_SetWindowSize(IntPtr window, int w, int h);

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
            TileVisuals = new World.TileVisuals(GraphicsDevice, textures, defs),
        };
        Svc.Scenes = new SceneStack(Svc);
        Console.WriteLine($"[content] root: {paths.Root} (dev checkout: {paths.IsDevelopmentCheckout})");
        _extension?.Initialize(Svc);
        Svc.Scenes.Push(_firstScene(Svc));
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        // Self-heal: if the drawing surface and the window disagree for a moment (missed resize
        // event after alt-tab / minimize), fix it.
        var pp0 = GraphicsDevice.PresentationParameters;
        var cb = Window.ClientBounds;
        if (_checkOnScreen > 0 && --_checkOnScreen == 0) EnsureOnScreen();
        if (!IsMinimized && cb.Width >= 200 && cb.Height >= 150 && (pp0.BackBufferWidth != cb.Width || pp0.BackBufferHeight != cb.Height))
        {
            if (++_sizeMismatchFrames >= 5) { Diagnostics.Log.Write($"Back buffer {pp0.BackBufferWidth}x{pp0.BackBufferHeight} != window {cb.Size}; fixing"); SyncBackBuffer(); _sizeMismatchFrames = 0; }
        }
        else _sizeMismatchFrames = 0;
        Svc.TotalTime = gameTime.TotalGameTime.TotalSeconds;
        Svc.Textures.ProcessHotReload();
        Svc.DefReload.Process();
        Svc.Input.Update(Svc.Canvas, HasFocus);
        if (Svc.Input.KeyPressed(Microsoft.Xna.Framework.Input.Keys.F9)) ResetDisplay();
        _extension?.BeginFrame(dt);
        Svc.Scenes.ApplyPending();
        if (Svc.Scenes.Count == 0) { Exit(); return; }
        Svc.Scenes.Update(dt);

        _frameCount++;
        // Dev-only reproduction of alt-tab / minimize: CHASM_SIM_ALTTAB=1 shrinks the window, then restores it.
        if (Environment.GetEnvironmentVariable("CHASM_SIM_ALTTAB") == "1")
        {
            if (_frameCount == 20) { SDL_SetWindowSize(Window.Handle, 160, 90); Console.WriteLine($"[sim] shrunk, bounds {Window.ClientBounds}"); }
            if (_frameCount == 45) { SDL_SetWindowSize(Window.Handle, 1600, 900); Console.WriteLine($"[sim] restored, bounds {Window.ClientBounds}"); }
            if (_frameCount % 15 == 0) Console.WriteLine($"[sim] f{_frameCount} active={IsActive} window={Window.ClientBounds.Size} backbuffer={GraphicsDevice.PresentationParameters.BackBufferWidth}x{GraphicsDevice.PresentationParameters.BackBufferHeight}");
        }
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
        Svc.TileVisuals.Dispose();
        Svc.Textures.Dispose();
        Svc.Canvas.Dispose();
        base.UnloadContent();
    }
}
