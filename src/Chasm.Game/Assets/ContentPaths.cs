namespace Chasm.Game.Assets;

/// <summary>
/// Where content lives on disk.
///
/// During development (running from a repo checkout) this resolves to the repo's own /content
/// folder, so anything the editors save lands directly in source control and hot reload sees edits
/// immediately. A shipped build falls back to the content folder copied next to the exe.
/// </summary>
public sealed class ContentPaths
{
    public string Root { get; }
    public bool IsDevelopmentCheckout { get; }

    public string Defs => Path.Combine(Root, "defs");
    public string Art => Path.Combine(Root, "art");
    public string Fonts => Path.Combine(Root, "fonts");
    public string Floors => Path.Combine(Root, "floors");
    public string Audio => Path.Combine(Root, "audio");

    private ContentPaths(string root, bool dev) { Root = root; IsDevelopmentCheckout = dev; }

    public static ContentPaths Resolve()
    {
        // Walk up from the exe looking for the repo root (marked by the .sln) with a content folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IntoTheChasm.sln")) &&
                Directory.Exists(Path.Combine(dir.FullName, "content")))
                return new ContentPaths(Path.Combine(dir.FullName, "content"), dev: true);
        }
        return new ContentPaths(Path.Combine(AppContext.BaseDirectory, "content"), dev: false);
    }

    /// <summary>Converts an absolute path under /art into the art-relative key used in content files.</summary>
    public string ArtKey(string absolutePath) =>
        Path.GetRelativePath(Art, absolutePath).Replace('\\', '/');
}
