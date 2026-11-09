using Chasm.Core.Content;
using Chasm.Core.Events;
using Chasm.Game.Assets;
using Chasm.Game.Input;
using Chasm.Game.Rendering;
using Chasm.Game.Scenes;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Hosting;

/// <summary>
/// The shared services every scene can use. Built once by <see cref="ChasmGame"/> and handed to
/// scenes by the stack. This replaces the old "static manager" pattern: nothing is global, so
/// sandboxes can build their own isolated worlds side by side.
/// </summary>
public sealed class GameServices
{
    public required ChasmGame Game { get; init; }
    public required GraphicsDevice Graphics { get; init; }
    public required ContentPaths Paths { get; init; }
    public required DefRegistry Defs { get; init; }
    public required DefHotReload DefReload { get; init; }
    public required TextureStore Textures { get; init; }
    public required FontStore Fonts { get; init; }
    public required Draw Draw { get; init; }
    public required Canvas Canvas { get; init; }
    public required InputState Input { get; init; }
    public required EventBus Events { get; init; }
    public required World.TileVisuals TileVisuals { get; init; }
    public SceneStack Scenes { get; internal set; } = null!;

    /// <summary>Total seconds since start, for shaders and animated UI.</summary>
    public double TotalTime { get; internal set; }
}
