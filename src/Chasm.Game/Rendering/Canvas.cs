using Chasm.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Rendering;

/// <summary>
/// Everything is drawn into a fixed 1920x1080 render target, then scaled (letterboxed) into the
/// window. All game coordinates are canvas coordinates, regardless of window size.
/// </summary>
public sealed class Canvas : IDisposable
{
    private readonly GraphicsDevice _device;
    public RenderTarget2D Target { get; private set; }
    public int Width => GameInfo.CanvasWidth;
    public int Height => GameInfo.CanvasHeight;

    /// <summary>Where the canvas lands in the window this frame.</summary>
    public Rectangle Destination { get; private set; }

    public Canvas(GraphicsDevice device)
    {
        _device = device;
        Target = new RenderTarget2D(device, GameInfo.CanvasWidth, GameInfo.CanvasHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        UpdateDestination();
    }

    /// <summary>
    /// Window area the canvas is fitted into. Null = the whole window. Editors set this so their
    /// side panels sit beside the map instead of covering it.
    /// </summary>
    public Func<Rectangle>? Region { get; set; }

    /// <summary>Editor zoom (1 = fit the region) and pan (window pixels). The game leaves these alone.</summary>
    public float Zoom { get; set; } = 1f;
    public Vector2 Pan { get; set; }

    /// <summary>The window area the canvas may draw into this frame.</summary>
    public Rectangle Area { get; private set; }

    public void UpdateDestination()
    {
        var pp = _device.PresentationParameters;
        var area = Region?.Invoke() ?? new Rectangle(0, 0, pp.BackBufferWidth, pp.BackBufferHeight);
        if (area.Width <= 0 || area.Height <= 0) area = new Rectangle(0, 0, pp.BackBufferWidth, pp.BackBufferHeight);
        Area = area;
        float scale = Math.Min(area.Width / (float)Width, area.Height / (float)Height) * Zoom;
        int w = (int)(Width * scale), h = (int)(Height * scale);
        var center = area.Center.ToVector2() + Pan;
        Destination = new Rectangle((int)(center.X - w / 2f), (int)(center.Y - h / 2f), w, h);
    }

    /// <summary>Zoom around a window point so the canvas point under it stays put.</summary>
    public void ZoomAround(Point window, float newZoom)
    {
        UpdateDestination();
        newZoom = Math.Clamp(newZoom, 0.2f, 8f);
        var canvasPt = WindowToCanvas(window);
        Zoom = newZoom;
        UpdateDestination();
        var after = CanvasToWindow(canvasPt);
        Pan += new Vector2(window.X, window.Y) - after;
        UpdateDestination();
    }

    public Vector2 CanvasToWindow(Vector2 canvas)
    {
        var d = Destination;
        return new Vector2(d.X + canvas.X * d.Width / (float)Width, d.Y + canvas.Y * d.Height / (float)Height);
    }

    public void ResetView() { Zoom = 1f; Pan = Vector2.Zero; }

    private static readonly RasterizerState Scissor = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    public Vector2 WindowToCanvas(Point window)
    {
        UpdateDestination();
        var d = Destination;
        if (d.Width == 0 || d.Height == 0) return Vector2.Zero;
        return new Vector2((window.X - d.X) * Width / (float)d.Width, (window.Y - d.Y) * Height / (float)d.Height);
    }

    public void Begin() => _device.SetRenderTarget(Target);

    public void Present(SpriteBatch batch)
    {
        _device.SetRenderTarget(null);
        UpdateDestination();
        _device.Clear(Color.Black);
        _device.ScissorRectangle = Area;
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, Zoom > 1.5f ? SamplerState.PointClamp : SamplerState.LinearClamp, null, Scissor);
        batch.Draw(Target, Destination, Color.White);
        batch.End();
    }

    /// <summary>Recreates the canvas render target (part of "Reset display").</summary>
    public void Rebuild()
    {
        Target.Dispose();
        Target = new RenderTarget2D(_device, GameInfo.CanvasWidth, GameInfo.CanvasHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        UpdateDestination();
    }

    public void Dispose() => Target.Dispose();
}
