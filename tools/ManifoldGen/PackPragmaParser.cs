// ManifoldGen — #pragma pack parser
// Scans Steamworks SDK header files to derive the correct Pack value for each
// callback/struct definition. This is the authoritative source since steam_api.json
// contains no struct_size or pack information.
//
// Known pack values in the SDK:
//   Pack=4 (VALVE_CALLBACK_PACK_SMALL) — Linux, macOS, FreeBSD
//   Pack=8 (VALVE_CALLBACK_PACK_LARGE) — Windows
//   Pack=1 — specific structs (SteamNetworkingMessage_t, input types, etc.)
//
// Known Oddities list (structs that fall outside the standard pragma pattern):
//   These bypass automated derivation and use hardcoded values.

namespace ManifoldGen;

public static class PackPragmaParser
{
    // ── Known Oddities — structs with explicit non-standard pack values ───────
    // Valve may change these outside of normal pragma blocks. Review on every SDK upgrade.
    private static readonly Dictionary<string, int> KnownOddities = new(StringComparer.Ordinal)
    {
        { "SteamNetworkingMessage_t",   1 },
        { "InputAnalogActionData_t",    1 },
        { "InputDigitalActionData_t",   1 },
        { "InputMotionData_t",          1 },
        // CSteamID uses a union internally with Pack=1 context
        { "CSteamID",                   1 },
    };

    /// <summary>
    /// Builds a map of struct name → pack override for all structs whose pack value
    /// deviates from the platform default (SMALL=4 / LARGE=8).
    /// Returns only explicit overrides. Structs not in the map use the platform default.
    /// </summary>
    public static Dictionary<string, int> BuildPackMap(string sdkHeaderDir)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);

        // Seed with known oddities — these are hardcoded and trusted
        foreach (var (name, pack) in KnownOddities)
            result[name] = pack;

        // Scan headers for Pack=1 explicit blocks (non-standard)
        // We're only looking for structs inside explicit pack(1) blocks
        // because pack(4) and pack(8) are the platform-conditional defaults
        if (!Directory.Exists(sdkHeaderDir))
        {
            Console.Error.WriteLine($"WARNING: SDK header dir not found: {sdkHeaderDir}. Using Known Oddities only.");
            return result;
        }

        foreach (string headerFile in Directory.GetFiles(sdkHeaderDir, "*.h", SearchOption.TopDirectoryOnly))
        {
            ScanHeaderForPack1Structs(headerFile, result);
        }

        return result;
    }

    private static void ScanHeaderForPack1Structs(string headerPath, Dictionary<string, int> result)
    {
        string[] lines;
        try { lines = File.ReadAllLines(headerPath); }
        catch { return; }

        int currentPack = 0; // 0 = not in an explicit pack block
        var packStack   = new Stack<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            // Detect #pragma pack(push, N)
            var pushMatch = System.Text.RegularExpressions.Regex.Match(
                line, @"#pragma\s+pack\s*\(\s*push\s*,\s*(\d+)\s*\)");
            if (pushMatch.Success)
            {
                packStack.Push(currentPack);
                currentPack = int.Parse(pushMatch.Groups[1].Value);
                continue;
            }

            // Detect #pragma pack(pop)
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"#pragma\s+pack\s*\(\s*pop\s*\)"))
            {
                currentPack = packStack.Count > 0 ? packStack.Pop() : 0;
                continue;
            }

            // Inside a Pack=1 block, look for struct/class definitions
            if (currentPack == 1)
            {
                var structMatch = System.Text.RegularExpressions.Regex.Match(
                    line, @"^(?:struct|class)\s+(\w+)");
                if (structMatch.Success)
                {
                    string structName = structMatch.Groups[1].Value;
                    // Only record if not already in the map (Known Oddities take priority)
                    if (!result.ContainsKey(structName))
                        result[structName] = 1;
                }
            }
        }
    }

    /// <summary>
    /// Returns the Pack value for the given struct, or null if the struct should use
    /// the platform default (emitting the #if MANIFOLD_PACK_SMALL block).
    /// </summary>
    public static int? GetExplicitPack(string structName, Dictionary<string, int> packMap)
    {
        return packMap.TryGetValue(structName, out int pack) ? pack : null;
    }
}
