using SteamKit2;

namespace Cato.Infrastructure.Steam.SteamKit;

/// <summary>
/// Utility for converting SteamKit2 KeyValue trees to dictionaries,
/// flattening them to dot-delimited paths, and diffing two snapshots.
/// </summary>
public static class KeyValueDiffUtility
{
    /// <summary>
    /// Recursively converts a SteamKit2 KeyValue tree to nested dictionaries.
    /// Leaf nodes become string values, nodes with children become nested dicts.
    /// </summary>
    public static Dictionary<string, object?> KeyValueToDict(KeyValue kv)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var child in kv.Children)
        {
            if (child.Children.Count > 0)
            {
                dict[child.Name!] = KeyValueToDict(child);
            }
            else
            {
                dict[child.Name!] = child.Value;
            }
        }

        return dict;
    }

    /// <summary>
    /// Flattens a nested dictionary to dot-delimited key paths.
    /// e.g. { "depots": { "731": { "gid": "123" } } } → { "depots.731.gid": "123" }
    /// </summary>
    public static Dictionary<string, string?> Flatten(Dictionary<string, object?> tree, string prefix = "")
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        FlattenRecursive(tree, prefix, result);
        return result;
    }

    private static void FlattenRecursive(Dictionary<string, object?> tree, string prefix, Dictionary<string, string?> result)
    {
        foreach (var (key, value) in tree)
        {
            var fullKey = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";

            if (value is Dictionary<string, object?> nested)
            {
                FlattenRecursive(nested, fullKey, result);
            }
            else
            {
                result[fullKey] = value?.ToString();
            }
        }
    }

    /// <summary>
    /// Diffs two flattened key-value maps and returns a list of changes.
    /// </summary>
    public static IReadOnlyList<KeyValueChange> Diff(
        Dictionary<string, string?> oldFlat,
        Dictionary<string, string?> newFlat)
    {
        var changes = new List<KeyValueChange>();

        // Modified or Removed
        foreach (var (key, oldValue) in oldFlat)
        {
            if (newFlat.TryGetValue(key, out var newValue))
            {
                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                {
                    changes.Add(new KeyValueChange(
                        ExtractSection(key), key, "Modified", oldValue, newValue));
                }
            }
            else
            {
                changes.Add(new KeyValueChange(
                    ExtractSection(key), key, "Removed", oldValue, null));
            }
        }

        // Added
        foreach (var (key, newValue) in newFlat)
        {
            if (!oldFlat.ContainsKey(key))
            {
                changes.Add(new KeyValueChange(
                    ExtractSection(key), key, "Added", null, newValue));
            }
        }

        return changes;
    }

    /// <summary>
    /// Extracts the top-level section from a dot-delimited key path.
    /// e.g. "depots.731.gid" → "depots"
    /// </summary>
    private static string ExtractSection(string keyPath)
    {
        var dotIndex = keyPath.IndexOf('.');
        return dotIndex >= 0 ? keyPath[..dotIndex] : keyPath;
    }
}

public record KeyValueChange(
    string Section,
    string KeyPath,
    string Action,
    string? OldValue,
    string? NewValue);
