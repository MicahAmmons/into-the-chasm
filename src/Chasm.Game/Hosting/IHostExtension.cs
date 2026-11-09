namespace Chasm.Game.Hosting;

/// <summary>
/// Lets a host add per-frame behaviour without the game knowing about it. The tools launcher
/// uses this to run ImGui. The shipped game has none.
/// </summary>
public interface IHostExtension : IDisposable
{
    void Initialize(GameServices services);

    /// <summary>Before input is read by scenes. ImGui starts its frame here and reports mouse capture.</summary>
    void BeginFrame(float dt);

    /// <summary>After the canvas is presented, drawn in window space.</summary>
    void EndFrame();
}
