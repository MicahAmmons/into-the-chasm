namespace Chasm.Core;

/// <summary>
/// Facts about the game that every layer may need. Kept in Core so tests and tools can read them
/// without referencing MonoGame.
/// </summary>
public static class GameInfo
{
    public const string Title = "Into the Chasm";

    /// <summary>The fixed resolution every Tile is designed at. The window scales this canvas.</summary>
    public const int CanvasWidth = 1920;
    public const int CanvasHeight = 1080;
}
