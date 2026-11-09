using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Chasm.Core.Content;

namespace Chasm.Core.Serialization;

/// <summary>
/// The single JSON configuration used for every content and save file, so files are written and
/// read identically everywhere (game, tools, tests).
/// </summary>
public static class ChasmJson
{
    private static readonly List<Assembly> _assemblies = new() { typeof(ChasmJson).Assembly };
    private static JsonSerializerOptions? _options;

    /// <summary>Lets other assemblies (e.g. Game) contribute polymorphic subclasses. Call before first use.</summary>
    public static void RegisterAssembly(Assembly assembly)
    {
        if (_assemblies.Contains(assembly)) return;
        _assemblies.Add(assembly);
        _options = null;
        PolymorphicTypes.Reset();
        Content.DefRegistry.ResetTypeCache();
    }

    public static IReadOnlyList<Assembly> Assemblies => _assemblies;

    public static JsonSerializerOptions Options => _options ??= Build();

    private static JsonSerializerOptions Build()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(AddPolymorphism);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = resolver,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new Grid.CellCoordJsonConverter());
        options.Converters.Add(new Grid.CellListJsonConverter());
        return options;
    }

    private static void AddPolymorphism(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        if (info.Type.GetCustomAttribute<PolymorphicAttribute>(inherit: false) is null) return;

        var poly = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$type",
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var derived in PolymorphicTypes.ConcreteSubtypes(info.Type))
            poly.DerivedTypes.Add(new JsonDerivedType(derived, PolymorphicTypes.TypeId(derived)));
        info.PolymorphismOptions = poly;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    public static object? Deserialize(string json, Type type) => JsonSerializer.Deserialize(json, type, Options);
    public static string Serialize(object value, Type type) => JsonSerializer.Serialize(value, type, Options);

    /// <summary>Deep copy through JSON. Fine for editor duplication; never use in hot game loops.</summary>
    public static T Clone<T>(T value) where T : notnull =>
        (T)Deserialize(Serialize(value, value.GetType()), value.GetType())!;
}

/// <summary>Finds concrete subclasses of <see cref="PolymorphicAttribute"/> base types by reflection.</summary>
public static class PolymorphicTypes
{
    private static readonly Dictionary<Type, List<Type>> _cache = new();

    internal static void Reset() { lock (_cache) _cache.Clear(); }

    public static IReadOnlyList<Type> ConcreteSubtypes(Type baseType)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(baseType, out var list)) return list;
            list = ChasmJson.Assemblies
                .SelectMany(SafeTypes)
                .Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(t => TypeId(t), StringComparer.Ordinal)
                .ToList();
            _cache[baseType] = list;
            return list;
        }
    }

    public static string TypeId(Type t) => t.GetCustomAttribute<TypeIdAttribute>()?.Id ?? t.Name;

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t is not null)!; }
    }
}
