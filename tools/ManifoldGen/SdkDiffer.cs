// ManifoldGen — SDK differ
// Prints a human-readable diff between two steam_api.json versions

namespace ManifoldGen;

public static class SdkDiffer
{
    public static void PrintDiff(SteamApiModel oldModel, SteamApiModel newModel)
    {
        Console.WriteLine("=== SDK Diff ===");
        Console.WriteLine();

        DiffInterfaces(oldModel, newModel);
        DiffEnums(oldModel, newModel);
        DiffCallbacks(oldModel, newModel);
    }

    private static void DiffInterfaces(SteamApiModel old, SteamApiModel @new)
    {
        var oldMethods = FlattenMethods(old);
        var newMethods = FlattenMethods(@new);

        var added   = newMethods.Keys.Except(oldMethods.Keys).ToList();
        var removed = oldMethods.Keys.Except(newMethods.Keys).ToList();

        if (added.Count > 0)
        {
            Console.WriteLine($"  ADDED methods ({added.Count}):");
            foreach (var m in added.Take(50)) Console.WriteLine($"    + {m}");
            if (added.Count > 50) Console.WriteLine($"    ... and {added.Count - 50} more");
            Console.WriteLine();
        }

        if (removed.Count > 0)
        {
            Console.WriteLine($"  REMOVED methods ({removed.Count}):");
            foreach (var m in removed.Take(50)) Console.WriteLine($"    - {m}");
            if (removed.Count > 50) Console.WriteLine($"    ... and {removed.Count - 50} more");
            Console.WriteLine();
        }

        if (added.Count == 0 && removed.Count == 0)
            Console.WriteLine("  Methods: no changes");
    }

    private static void DiffEnums(SteamApiModel old, SteamApiModel @new)
    {
        var oldEnums = old.Enums?.Select(e => e.EnumName!).ToHashSet() ?? new HashSet<string>();
        var newEnums = @new.Enums?.Select(e => e.EnumName!).ToHashSet() ?? new HashSet<string>();

        var added   = newEnums.Except(oldEnums).ToList();
        var removed = oldEnums.Except(newEnums).ToList();

        if (added.Count > 0)
        {
            Console.WriteLine($"  ADDED enums: {string.Join(", ", added)}");
            Console.WriteLine();
        }
        if (removed.Count > 0)
        {
            Console.WriteLine($"  REMOVED enums: {string.Join(", ", removed)}");
            Console.WriteLine();
        }
    }

    private static void DiffCallbacks(SteamApiModel old, SteamApiModel @new)
    {
        var oldCbs = old.CallbackStructs?.Select(c => c.Name!).ToHashSet() ?? new HashSet<string>();
        var newCbs = @new.CallbackStructs?.Select(c => c.Name!).ToHashSet() ?? new HashSet<string>();

        var added   = newCbs.Except(oldCbs).ToList();
        var removed = oldCbs.Except(newCbs).ToList();

        if (added.Count > 0)
        {
            Console.WriteLine($"  ADDED callbacks: {string.Join(", ", added)}");
            Console.WriteLine();
        }
        if (removed.Count > 0)
        {
            Console.WriteLine($"  REMOVED callbacks: {string.Join(", ", removed)}");
            Console.WriteLine();
        }
    }

    private static Dictionary<string, SteamMethod> FlattenMethods(SteamApiModel model)
    {
        var result = new Dictionary<string, SteamMethod>(StringComparer.Ordinal);
        if (model.Interfaces == null) return result;
        foreach (var iface in model.Interfaces)
        {
            if (iface.Methods == null) continue;
            foreach (var m in iface.Methods)
            {
                string key = m.MethodNameFlat ?? m.MethodName ?? "";
                if (!string.IsNullOrEmpty(key))
                    result[key] = m;
            }
        }
        return result;
    }
}
