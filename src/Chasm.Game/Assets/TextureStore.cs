using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Assets;

/// <summary>
/// Loads PNGs from content/art at runtime by path key (e.g. "tiles/floor0/0_0_0.png").
///
/// Missing art never crashes: you get a placeholder (a coloured box) instead, and
/// <see cref="IsPlaceholder"/> lets drawing code label it with what it stands for. That's how
/// new content can exist before its art does.
///
/// Files changed on disk are reloaded on the next frame (hot reload).
/// </summary>
public sealed class TextureStore : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ContentPaths _paths;
    private readonly Dictionary<string, Texture2D> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Texture2D> _placeholders = new();
    private readonly Texture2D _placeholder;
    private readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher? _watcher;

    public Texture2D Pixel { get; }

    /// <summary>Raised on the main thread after a texture was reloaded from disk.</summary>
    public event Action<string>? Reloaded;

    public TextureStore(GraphicsDevice device, ContentPaths paths)
    {
        _device = device;
        _paths = paths;
        Pixel = new Texture2D(device, 1, 1);
        Pixel.SetData(new[] { Color.White });
        _placeholder = MakePlaceholder(device);
        _placeholders.Add(_placeholder);

        if (Directory.Exists(paths.Art))
        {
            _watcher = new FileSystemWatcher(paths.Art, "*.png") { IncludeSubdirectories = true, EnableRaisingEvents = true };
            _watcher.Changed += (_, e) => MarkDirty(e.FullPath);
            _watcher.Created += (_, e) => MarkDirty(e.FullPath);
            _watcher.Renamed += (_, e) => MarkDirty(e.FullPath);
        }
    }

    public static string NormalizeKey(string key) => key.Replace('\\', '/').TrimStart('/');

    /// <summary>Gets a texture by art key. Returns the placeholder when the key is empty or the file is missing.</summary>
    public Texture2D Get(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return _placeholder;
        key = NormalizeKey(key);
        if (_cache.TryGetValue(key, out var tex)) return tex;
        tex = Load(key) ?? _placeholder;
        _cache[key] = tex;
        return tex;
    }

    public bool Exists(string? key) =>
        !string.IsNullOrWhiteSpace(key) && File.Exists(Path.Combine(_paths.Art, NormalizeKey(key)));

    public bool IsPlaceholder(Texture2D texture) => _placeholders.Contains(texture);

    /// <summary>All art keys under content/art, for pickers.</summary>
    public IEnumerable<string> AllKeys() =>
        Directory.Exists(_paths.Art)
            ? Directory.EnumerateFiles(_paths.Art, "*.png", SearchOption.AllDirectories)
                .Select(_paths.ArtKey).OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();

    /// <summary>Reloads textures whose files changed. Called once per frame by the host.</summary>
    public void ProcessHotReload()
    {
        string[] dirty;
        lock (_dirty) { if (_dirty.Count == 0) return; dirty = _dirty.ToArray(); _dirty.Clear(); }
        foreach (var key in dirty)
        {
            if (!_cache.TryGetValue(key, out var old)) continue;
            var fresh = Load(key);
            if (fresh is null) continue;
            if (!_placeholders.Contains(old)) old.Dispose();
            _cache[key] = fresh;
            Reloaded?.Invoke(key);
        }
    }

    private void MarkDirty(string fullPath)
    {
        lock (_dirty) _dirty.Add(_paths.ArtKey(fullPath));
    }

    private Texture2D? Load(string key)
    {
        string path = Path.Combine(_paths.Art, key);
        if (!File.Exists(path)) return null;
        // The file may still be being written by an art program; retry briefly.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return Texture2D.FromStream(_device, stream, DefaultColorProcessors.PremultiplyAlpha);
            }
            catch (IOException) { Thread.Sleep(30); }
            catch (InvalidOperationException) { return null; }
        }
        return null;
    }

    private static Texture2D MakePlaceholder(GraphicsDevice device)
    {
        // 32x32 checker in magenta/dark so it reads as "art missing" at any scale.
        const int size = 32;
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                data[y * size + x] = ((x / 8 + y / 8) % 2 == 0) ? new Color(170, 60, 170) : new Color(60, 30, 70);
        var tex = new Texture2D(device, size, size);
        tex.SetData(data);
        return tex;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        foreach (var t in _cache.Values.Distinct()) if (!_placeholders.Contains(t)) t.Dispose();
        _placeholder.Dispose();
        Pixel.Dispose();
    }
}
