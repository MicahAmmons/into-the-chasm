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
    public RenderTarget2D Target { get; }
    public int Width => GameInfo.CanvasWidth;
    public int Height => GameInfo.CanvasHeight;

    /// <summary>Where the canvas lands in the window this frame.</summary>
    public Rectangle Destination { get; private set; }

    public Canvas(GraphicsDevice device)
    {
        _device = device;
        Target = new RenderTarget2D(device, GameInfo.CanvasWidth, GameInfo.CanvasHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
        UpdateDestination();
    }

    public void UpdateDestination()
    {
        var pp = _device.PresentationParameters;
        float scale = Math.Min(pp.BackBufferWidth / (float)Width, pp.BackBufferHeight / (float)Height);
        int w = (int)(Width * scale), h = (int)(Height * scale);
        Destination = new Rectangle((pp.BackBufferWidth - w) / 2, (pp.BackBufferHeight - h) / 2, w, h);
    }

    public Vector2 WindowToCanvas(Point window)
    {
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
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp);
        batch.Draw(Target, Destination, Color.White);
        batch.End();
    }

    public void Dispose() => Target.Dispose();
}
