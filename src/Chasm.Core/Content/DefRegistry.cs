using System.Reflection;
using Chasm.Core.Serialization;

namespace Chasm.Core.Content;

/// <summary>
/// Holds every loaded definition, keyed by type and id. Loads from and saves to
/// <c>content/defs/&lt;folder&gt;/&lt;id&gt;.json</c>. Editors write through this so the game,
/// sandboxes and tools always see the same data.
/// </summary>
public sealed class DefRegistry
{
    private readonly Dictionary<Type, SortedDictionary<string, Def>> _defs = new();
    private readonly List<string> _loadErrors = new();

    /// <summary>Folder that contains one sub-folder per def type. Null for in-memory registries (tests).</summary>
    public string? Root { get; private set; }

    /// <summary>Raised after any def is added, replaced or removed.</summary>
    public event Action<Def>? Changed;

    public IReadOnlyList<string> LoadErrors => _loadErrors;

    /// <summary>Every concrete Def type with a [DefFolder], from all registered assemblies.</summary>
    public static IReadOnlyList<Type> DefTypes =>
        _defTypes ??= ChasmJson.Assemblies.SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(Def).IsAssignableFrom(t) && t.GetCustomAttribute<DefFolderAttribute>() is not null)
            .OrderBy(t => t.GetCustomAttribute<DefFolderAttribute>()!.DisplayName)
            .ToList();
    private static List<Type>? _defTypes;
    internal static void ResetTypeCache() => _defTypes = null;

    public static DefFolderAttribute FolderOf(Type defType) =>
        defType.GetCustomAttribute<DefFolderAttribute>()
        ?? throw new InvalidOperationException($"{defType.Name} has no [DefFolder] attribute.");

    public void LoadAll(string root)
    {
        Root = root;
        _defs.Clear();
        _loadErrors.Clear();
        foreach (var type in DefTypes)
        {
            string dir = Path.Combine(root, FolderOf(type).Folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
                LoadFile(type, file);
        }
    }

    /// <summary>(Re)loads one file. Used by hot reload when a file changes on disk.</summary>
    public Def? LoadFile(Type type, string file)
    {
        try
        {
            var def = (Def?)ChasmJson.Deserialize(File.ReadAllText(file), type);
            if (def is null) { _loadErrors.Add($"{file}: empty file"); return null; }
            if (string.IsNullOrWhiteSpace(def.Id)) def.Id = Path.GetFileNameWithoutExtension(file);
            Put(def);
            return def;
        }
        catch (Exception e)
        {
            _loadErrors.Add($"{Path.GetFileName(file)}: {e.Message}");
            return null;
        }
    }

    /// <summary>Finds which def type owns a file path (by folder), for hot reload.</summary>
    public Type? TypeForFile(string file)
    {
        if (Root is null) return null;
        string rel = Path.GetRelativePath(Root, file).Replace('\\', '/');
        string folder = rel.Split('/')[0];
        return DefTypes.FirstOrDefault(t => FolderOf(t).Folder == folder);
    }

    /// <summary>Adds or replaces in memory only.</summary>
    public void Put(Def def)
    {
        if (string.IsNullOrWhiteSpace(def.Id)) throw new ArgumentException("Def needs an Id.");
        Bucket(def.GetType())[def.Id] = def;
        Changed?.Invoke(def);
    }

    /// <summary>Adds or replaces in memory and writes the JSON file (if this registry has a root).</summary>
    public void Save(Def def)
    {
        Put(def);
        if (Root is null) return;
        string path = PathFor(def.GetType(), def.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, ChasmJson.Serialize(def, def.GetType()));
        File.Move(tmp, path, overwrite: true);
    }

    public bool Delete(Type type, string id)
    {
        if (!Bucket(type).Remove(id, out var def)) return false;
        if (Root is not null && File.Exists(PathFor(type, id))) File.Delete(PathFor(type, id));
        Changed?.Invoke(def);
        return true;
    }

    public string PathFor(Type type, string id) =>
        Path.Combine(Root ?? "", FolderOf(type).Folder, id + ".json");

    public T Get<T>(string id) where T : Def =>
        TryGet<T>(id, out var def) ? def : throw new KeyNotFoundException($"No {typeof(T).Name} with id '{id}'.");

    public bool TryGet<T>(string? id, out T def) where T : Def
    {
        if (id is not null && Bucket(typeof(T)).TryGetValue(id, out var d)) { def = (T)d; return true; }
        def = null!;
        return false;
    }

    public Def? Find(Type type, string? id) =>
        id is not null && Bucket(type).TryGetValue(id, out var d) ? d : null;

    public bool Exists(Type type, string? id) => Find(type, id) is not null;

    public IEnumerable<T> All<T>() where T : Def => Bucket(typeof(T)).Values.Cast<T>();
    public IEnumerable<Def> All(Type type) => Bucket(type).Values;

    /// <summary>Suggests an unused id based on a name, e.g. "Turtle Berry" -> "turtle_berry".</summary>
    public string SuggestId(Type type, string name)
    {
        string baseId = new string(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (baseId.Length == 0) baseId = type.Name.Replace("Def", "").ToLowerInvariant();
        string id = baseId;
        for (int n = 2; Exists(type, id); n++) id = $"{baseId}_{n}";
        return id;
    }

    private SortedDictionary<string, Def> Bucket(Type type)
    {
        if (!_defs.TryGetValue(type, out var bucket))
            _defs[type] = bucket = new SortedDictionary<string, Def>(StringComparer.Ordinal);
        return bucket;
    }
}
