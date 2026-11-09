using System.Collections;
using System.Reflection;

namespace Chasm.Core.Content;

/// <summary>
/// Walks any definition and reports missing required fields and broken [Ref] links. Runs in the
/// editors on save and in a test over all shipped content, so typos are caught before playing.
/// </summary>
public static class ContentValidator
{
    public static List<string> Validate(Def def, DefRegistry registry)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(def.Id)) problems.Add("Id is empty.");
        Walk(def, def.GetType().Name, registry, problems, depth: 0);
        problems.AddRange(def.Validate(registry));
        return problems;
    }

    public static List<string> ValidateAll(DefRegistry registry)
    {
        var problems = new List<string>();
        foreach (var type in DefRegistry.DefTypes)
            foreach (var def in registry.All(type))
                problems.AddRange(Validate(def, registry).Select(p => $"[{type.Name} {def.Id}] {p}"));
        return problems;
    }

    private static void Walk(object obj, string path, DefRegistry registry, List<string> problems, int depth)
    {
        if (depth > 16) return;
        foreach (var prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            if (prop.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is not null) continue;
            object? value = prop.GetValue(obj);
            string here = $"{path}.{prop.Name}";

            if (prop.GetCustomAttribute<RequiredAttribute>() is not null && IsEmpty(value))
                problems.Add($"{here} is required.");

            if (prop.GetCustomAttribute<RefAttribute>() is { } r)
            {
                foreach (var id in RefIds(value))
                    if (!string.IsNullOrEmpty(id) && !registry.Exists(r.Target, id))
                        problems.Add($"{here} points to missing {r.Target.Name} '{id}'.");
                continue;
            }

            switch (value)
            {
                case null or string: break;
                case IEnumerable list when value is not string:
                    int i = 0;
                    foreach (var item in list)
                    {
                        if (item is not null && IsComplex(item.GetType())) Walk(item, $"{here}[{i}]", registry, problems, depth + 1);
                        i++;
                    }
                    break;
                default:
                    if (IsComplex(value.GetType())) Walk(value, here, registry, problems, depth + 1);
                    break;
            }
        }
    }

    private static IEnumerable<string?> RefIds(object? value) => value switch
    {
        string s => new[] { s },
        IEnumerable<string> many => many,
        _ => Array.Empty<string?>(),
    };

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        ICollection c => c.Count == 0,
        _ => false,
    };

    private static bool IsComplex(Type t) =>
        t.IsClass && t != typeof(string) && !t.Namespace!.StartsWith("System", StringComparison.Ordinal);
}
