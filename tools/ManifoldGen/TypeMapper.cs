// ManifoldGen — C → C# Type Mapper
// Converts Steamworks C type strings to their C# equivalents.
// Handles the full type mapping table from MASTER_DESIGN.md §6.

namespace ManifoldGen;

public static class TypeMapper
{
    // ── Reserved C# keywords — generated identifiers must avoid these ────────
    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked",
        "class","const","continue","decimal","default","delegate","do","double","else",
        "enum","event","explicit","extern","false","finally","fixed","float","for",
        "foreach","goto","if","implicit","in","int","interface","internal","is","lock",
        "long","namespace","new","null","object","operator","out","override","params",
        "private","protected","public","readonly","ref","return","sbyte","sealed",
        "short","sizeof","stackalloc","static","string","struct","switch","this",
        "throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort",
        "using","virtual","void","volatile","while",
        // Contextual keywords also worth escaping
        "add","alias","ascending","async","await","by","descending","dynamic","equals",
        "from","get","global","group","into","join","let","managed","nameof","notnull",
        "on","orderby","partial","record","remove","select","set","unmanaged","value",
        "var","when","where","with","yield","nint","nuint"
    };

    // ── Typedef aliases that get rich domain types instead of primitives ─────
    // Everything NOT in this set falls through to the primitive mapping below.
    private static readonly HashSet<string> DomainTypeAliases = new(StringComparer.Ordinal)
    {
        "uint64_steamid",    // → SteamId
        "HSteamNetConnection", // → NetConnection (used internally as uint)
        "HSteamListenSocket",  // → ListenSocket  (used internally as uint)
        "HSteamNetPollGroup",  // → uint (internal only)
        "SteamAPICall_t",      // → ulong (internal only)
        "HSteamPipe",          // → uint (internal only)
        "HSteamUser",          // → uint (internal only)
    };

    /// <summary>
    /// Maps a Steamworks C type string to a C# type string suitable for P/Invoke.
    /// Returns (csType, needsMarshalAsU1) where needsMarshalAsU1 indicates a bool
    /// that requires [MarshalAs(UnmanagedType.U1)].
    /// </summary>
    public static (string CsType, bool IsBool, bool IsString, bool IsUnsupported) Map(string cType)
    {
        string t = cType.Trim();

        // Strip trailing const
        if (t.EndsWith(" const")) t = t[..^6].TrimEnd();

        // ── Void ────────────────────────────────────────────────────────────
        if (t == "void")                    return ("void",    false, false, false);
        if (t == "void *" || t == "void*")  return ("IntPtr",  false, false, false);

        // ── Bool — ALWAYS needs MarshalAs(U1) ───────────────────────────────
        if (t == "bool")                    return ("bool",    true,  false, false);

        // ── Strings ──────────────────────────────────────────────────────────
        if (t == "const char *" || t == "const char*" || t == "char *" || t == "char*")
            return ("string",    false, true,  false);

        // ── Domain type aliases ──────────────────────────────────────────────
        if (t == "uint64_steamid")          return ("ulong",   false, false, false); // raw in interop; SteamId in wrappers
        if (t == "SteamAPICall_t")          return ("ulong",   false, false, false);
        if (t == "HSteamNetConnection")     return ("uint",    false, false, false);
        if (t == "HSteamListenSocket")      return ("uint",    false, false, false);
        if (t == "HSteamNetPollGroup")      return ("uint",    false, false, false);
        if (t == "HSteamPipe")              return ("uint",    false, false, false);
        if (t == "HSteamUser")              return ("uint",    false, false, false);
        if (t == "HServerListRequest")      return ("IntPtr",  false, false, false);
        if (t == "HAuthTicket")             return ("uint",    false, false, false);
        if (t == "UGCHandle_t")             return ("ulong",   false, false, false);
        if (t == "UGCQueryHandle_t")        return ("ulong",   false, false, false);
        if (t == "UGCUpdateHandle_t")       return ("ulong",   false, false, false);
        if (t == "SteamInventoryResult_t")  return ("int",     false, false, false);
        if (t == "SteamItemDef_t")          return ("int",     false, false, false);
        if (t == "SteamItemInstanceID_t")   return ("ulong",   false, false, false);
        if (t == "PartyBeaconID_t")         return ("ulong",   false, false, false);

        // ── C primitive int aliases ───────────────────────────────────────────
        if (t == "int")    return ("int",    false, false, false);
        if (t == "unsigned int" || t == "unsigned")
                           return ("uint",   false, false, false);
        if (t == "short")  return ("short",  false, false, false);
        if (t == "unsigned short")
                           return ("ushort", false, false, false);
        if (t == "long")   return ("long",   false, false, false);
        if (t == "unsigned long" || t == "unsigned long int")
                           return ("ulong",  false, false, false);
        if (t == "char")   return ("byte",   false, false, false);
        if (t == "unsigned char")
                           return ("byte",   false, false, false);
        if (t == "signed char")
                           return ("sbyte",  false, false, false);

        // ── Steam typedef primitives (common aliases) ─────────────────────────
        if (t == "AppId_t")                return ("uint",   false, false, false);
        if (t == "DepotId_t")              return ("uint",   false, false, false);
        if (t == "AccountID_t")            return ("uint",   false, false, false);
        if (t == "PublishedFileId_t")      return ("ulong",  false, false, false);
        if (t == "HHTMLBrowser")           return ("uint",   false, false, false);
        if (t == "InputHandle_t")          return ("ulong",  false, false, false);
        if (t == "RemotePlaySessionID_t")  return ("uint",   false, false, false);
        if (t == "SteamLeaderboard_t")     return ("ulong",  false, false, false);
        if (t == "SteamLeaderboardEntries_t") return ("ulong", false, false, false);
        if (t == "SteamNetworkingMicroseconds") return ("long", false, false, false);
        if (t == "RTime32")                return ("uint",   false, false, false);
        if (t == "HTTPRequestHandle")      return ("uint",   false, false, false);
        if (t == "HTTPCookieContainerHandle") return ("uint", false, false, false);
        if (t == "SteamNetworkingPOPID")   return ("uint",   false, false, false);
        if (t == "SteamNetworkingIdentity") return ("IntPtr", false, false, false);
        if (t == "SteamNetworkingIPAddr")  return ("IntPtr", false, false, false);
        if (t == "SteamNetConnectionInfo_t") return ("IntPtr", false, false, false);
        if (t == "SteamDatagramHostedAddress") return ("IntPtr", false, false, false);
        if (t == "uint8" || t == "uint8_t")   return ("byte",    false, false, false);
        if (t == "int8"  || t == "int8_t")    return ("sbyte",   false, false, false);
        if (t == "uint16"|| t == "uint16_t")  return ("ushort",  false, false, false);
        if (t == "int16" || t == "int16_t")   return ("short",   false, false, false);
        if (t == "uint32"|| t == "uint32_t")  return ("uint",    false, false, false);
        if (t == "int32" || t == "int32_t")   return ("int",     false, false, false);
        if (t == "uint64"|| t == "uint64_t")  return ("ulong",   false, false, false);
        if (t == "int64" || t == "int64_t")   return ("long",    false, false, false);
        if (t == "intp"  || t == "uintp")     return ("IntPtr",  false, false, false);

        // ── Floating point ───────────────────────────────────────────────────
        if (t == "float")   return ("float",  false, false, false);
        if (t == "double")  return ("double", false, false, false);

        // ── Common Steam ID / handle primitives ──────────────────────────────
        if (t == "CSteamID")        return ("ulong",  false, false, false);
        if (t == "CGameID")         return ("ulong",  false, false, false);
        if (t == "EResult")         return ("int",    false, false, false); // emitted as enum separately

        // ── Pointers — anything ending in * that we haven't matched ──────────
        if (t.EndsWith("*") || t.EndsWith("* "))
            return ("IntPtr", false, false, false);

        // ── Enum types — map to int for P/Invoke layer ───────────────────────
        if (t.StartsWith("E") && char.IsUpper(t.Length > 1 ? t[1] : ' '))
            return ("int",    false, false, false);

        // ── Unknown / unsupported ────────────────────────────────────────────
        return ($"IntPtr /* {t} */", false, false, true);
    }

    /// <summary>
    /// Sanitises a raw SDK identifier for use as a C# identifier.
    /// Escapes reserved keywords with @-prefix.
    /// Strips Hungarian prefixes (b, n, ul, psz) from public-facing names.
    /// </summary>
    public static string SanitiseIdentifier(string name, bool stripHungarian = false)
    {
        if (string.IsNullOrEmpty(name)) return "_unnamed";

        string result = name;

        if (stripHungarian)
            result = StripHungarianPrefix(result);

        if (ReservedKeywords.Contains(result))
            result = "@" + result;

        return result;
    }

    /// <summary>
    /// Produces the PascalCase method name from a flat SDK method name.
    /// E.g. "SteamAPI_ISteamMatchmaking_CreateLobby" → "CreateLobby"
    /// </summary>
    public static string FlatMethodToName(string flatName, string interfaceName)
    {
        // Strip "SteamAPI_ISteamXxx_" prefix
        string prefix = $"SteamAPI_{interfaceName}_";
        if (flatName.StartsWith(prefix))
            return flatName[prefix.Length..];

        // Strip "SteamAPI_" global prefix
        if (flatName.StartsWith("SteamAPI_"))
            return flatName["SteamAPI_".Length..];

        return flatName;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string StripHungarianPrefix(string name)
    {
        // Strip common Steamworks Hungarian prefixes on parameter names
        // Only strip from the PUBLIC API wrapper layer — not from P/Invoke or struct fields
        if (name.Length > 2)
        {
            // psz → strip psz
            if (name.StartsWith("psz") && char.IsUpper(name[3])) return char.ToLower(name[3]) + name[4..];
            // pch → strip pch
            if (name.StartsWith("pch") && char.IsUpper(name[3])) return char.ToLower(name[3]) + name[4..];
            // pb  → strip p
            if (name.StartsWith("pb")  && char.IsUpper(name[2])) return char.ToLower(name[2]) + name[3..];
            // ul  → strip ul
            if (name.StartsWith("ul")  && char.IsUpper(name[2])) return char.ToLower(name[2]) + name[3..];
            // b   → strip b (bool param)
            if (name.StartsWith("b")   && char.IsUpper(name[1])) return char.ToLower(name[1]) + name[2..];
            // n   → strip n (numeric param)
            if (name.StartsWith("n")   && char.IsUpper(name[1])) return char.ToLower(name[1]) + name[2..];
            // c   → strip c (count param)
            if (name.StartsWith("c")   && char.IsUpper(name[1])) return char.ToLower(name[1]) + name[2..];
            // e   → keep (enum params, e.g. eType — stripping would produce just type which is a keyword)
        }
        return name;
    }

    public static bool IsReservedKeyword(string name) => ReservedKeywords.Contains(name);
}
