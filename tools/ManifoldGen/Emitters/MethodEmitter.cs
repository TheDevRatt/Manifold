// ManifoldGen — Method emitter
// Emits all [LibraryImport] P/Invoke declarations in SteamNative.Methods.cs
// Key policies enforced here:
//   - bool return/params ALWAYS get [MarshalAs(UnmanagedType.U1)]
//   - string params get [MarshalAs(UnmanagedType.LPUTF8Str)]
//   - All methods use CallingConvention.Cdecl via [UnmanagedCallConv]

using System.Text;

namespace ManifoldGen;

public sealed class MethodEmitter : IEmitter
{
    public string OutputFileName => "SteamNative.Methods.cs";

    // Interfaces we emit P/Invoke for — scoped to Phase 1 MVP
    private static readonly HashSet<string> TargetInterfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISteamClient",
        "ISteamUser",
        "ISteamFriends",
        "ISteamUtils",
        "ISteamMatchmaking",
        "ISteamMatchmakingServers",
        "ISteamNetworkingSockets",
        "ISteamNetworkingUtils",
        "ISteamApps",
    };

    public string Emit(GeneratorContext ctx, List<SkippedItem> skipped)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ctx.FileHeader(OutputFileName));
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine();
        sb.AppendLine("namespace Manifold.Core.Interop;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Auto-generated P/Invoke declarations for the Steamworks flat C API.");
        sb.AppendLine("/// Do not call these directly — use the Manifold.Core wrapper classes.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("internal static partial class SteamNative");
        sb.AppendLine("{");
        sb.AppendLine("    // Platform-conditional library name resolved via NativeLibrary.SetDllImportResolver");
        sb.AppendLine("    private const string LibName = \"steam_api64\";");
        sb.AppendLine();

        // Global API functions (non-interface)
        EmitGlobalMethods(sb, skipped);
        sb.AppendLine();

        // Interface methods
        if (ctx.Model.Interfaces != null)
        {
            var emittedEntryPoints = new HashSet<string>(StringComparer.Ordinal);

            foreach (var iface in ctx.Model.Interfaces)
            {
                if (string.IsNullOrEmpty(iface.ClassName)) continue;
                if (!TargetInterfaces.Contains(iface.ClassName!)) continue;
                if (iface.Methods == null || iface.Methods.Count == 0) continue;

                sb.AppendLine($"    // ── {iface.ClassName} ─────────────────────────────────────────────────────");
                sb.AppendLine();

                foreach (var method in iface.Methods)
                {
                    string flatName = method.MethodNameFlat ?? "";
                    if (!emittedEntryPoints.Add(flatName))
                    {
                        sb.AppendLine($"    // DUPLICATE SKIPPED: {flatName}");
                        sb.AppendLine();
                        continue;
                    }
                    EmitMethod(sb, iface.ClassName!, method, skipped);
                }

                sb.AppendLine();
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitGlobalMethods(StringBuilder sb, List<SkippedItem> skipped)
    {
        sb.AppendLine("    // ── Global SteamAPI functions ─────────────────────────────────────────────");
        sb.AppendLine();

        // Core lifecycle
        EmitRaw(sb, "bool",   "SteamAPI_Init",           returnIsBool: true);
        EmitRaw(sb, "void",   "SteamAPI_Shutdown");
        EmitRaw(sb, "bool",   "SteamAPI_RestartAppIfNecessary", returnIsBool: true, extraParams: "uint unOwnAppID");
        EmitRaw(sb, "bool",   "SteamAPI_IsSteamRunning",  returnIsBool: true);
        EmitRaw(sb, "IntPtr", "SteamAPI_GetSteamInstallPath");

        // Manual dispatch
        EmitRaw(sb, "void",   "SteamAPI_ManualDispatch_Init");
        EmitRaw(sb, "void",   "SteamAPI_ManualDispatch_RunFrame",        extraParams: "uint hSteamPipe");
        EmitRaw(sb, "bool",   "SteamAPI_ManualDispatch_GetNextCallback", returnIsBool: true,
                extraParams: "uint hSteamPipe, out CallbackMsg_t pCallbackMsg");
        EmitRaw(sb, "void",   "SteamAPI_ManualDispatch_FreeLastCallback",extraParams: "uint hSteamPipe");
        EmitRaw(sb, "bool",   "SteamAPI_ManualDispatch_GetAPICallResult", returnIsBool: true,
                extraParams: "uint hSteamPipe, ulong hSteamAPICall, IntPtr pCallback, int cubCallback, int iCallbackExpected, [MarshalAs(UnmanagedType.U1)] out bool pbFailed");

        // Accessor versioned functions — returns interface pointer as IntPtr
        EmitRaw(sb, "IntPtr", "SteamAPI_SteamUser_v023");
        EmitRaw(sb, "IntPtr", "SteamAPI_SteamFriends_v017");
        EmitRaw(sb, "IntPtr", "SteamAPI_SteamUtils_v010");
        EmitRaw(sb, "IntPtr", "SteamAPI_SteamMatchmaking_v009");
        EmitRaw(sb, "IntPtr", "SteamAPI_SteamNetworkingSockets_v012");
        EmitRaw(sb, "IntPtr", "SteamAPI_SteamNetworkingUtils_v004");
        EmitRaw(sb, "IntPtr", "SteamAPI_SteamApps_v008");

        sb.AppendLine();

        // CallbackMsg_t struct needed for ManualDispatch
        sb.AppendLine("    // CallbackMsg_t — used by ManualDispatch");
        sb.AppendLine("    [StructLayout(LayoutKind.Sequential)]");
        sb.AppendLine("    internal struct CallbackMsg_t");
        sb.AppendLine("    {");
        sb.AppendLine("        internal uint   m_hSteamUser;");
        sb.AppendLine("        internal int    m_iCallback;");
        sb.AppendLine("        internal IntPtr m_pubParam;");
        sb.AppendLine("        internal int    m_cubParam;");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void EmitRaw(
        StringBuilder sb,
        string returnType,
        string entryPoint,
        bool returnIsBool  = false,
        string? extraParams = null)
    {
        sb.AppendLine($"    [LibraryImport(LibName, EntryPoint = \"{entryPoint}\")]");
        sb.AppendLine($"    [UnmanagedCallConv(CallConvs = new[] {{ typeof(CallConvCdecl) }})]");
        if (returnIsBool)
            sb.AppendLine($"    [return: MarshalAs(UnmanagedType.U1)]");

        string paramStr = extraParams ?? string.Empty;
        sb.AppendLine($"    internal static partial {returnType} {entryPoint}({paramStr});");
        sb.AppendLine();
    }

    private static void EmitMethod(
        StringBuilder sb,
        string interfaceName,
        SteamMethod method,
        List<SkippedItem> skipped)
    {
        if (string.IsNullOrEmpty(method.MethodNameFlat)) return;
        string flatName  = method.MethodNameFlat!;
        string csName    = TypeMapper.FlatMethodToName(flatName, interfaceName);
        // Prefix with interface name to disambiguate same-named methods across interfaces
        string ifaceShort   = interfaceName.StartsWith("ISteam") ? interfaceName["ISteam".Length..] : interfaceName;
        string disambigName = $"{ifaceShort}_{csName}";
        string rawReturn = method.ReturnType ?? "void";

        var (csReturn, returnIsBool, returnIsString, returnUnsupported) = TypeMapper.Map(rawReturn);

        if (returnUnsupported)
        {
            skipped.Add(new SkippedItem("Method", flatName, $"Unsupported return type: {rawReturn}"));
            sb.AppendLine($"    // SKIPPED: {flatName} — unsupported return type: {rawReturn}");
            sb.AppendLine();
            return;
        }

        // Build parameter list
        var paramParts      = new List<string>();
        bool anyUnsupported = false;

        // 'self' is always the first param for interface methods
        paramParts.Add("IntPtr self");

        if (method.Params != null)
        {
            foreach (var p in method.Params)
            {
                string rawType  = p.ParamTypeFlat ?? p.ParamType ?? "void*";
                string rawPName = p.ParamName ?? "_unnamed";
                string safeName = TypeMapper.SanitiseIdentifier(rawPName);

                // Handle out pointer params: "SomeStruct *" as out
                bool isOutPtr = rawType.EndsWith(" *") && !rawType.StartsWith("const");

                var (csType, isBool, isString, unsupported) = TypeMapper.Map(rawType);

                if (unsupported)
                {
                    anyUnsupported = true;
                    break;
                }

                if (isBool)
                {
                    paramParts.Add($"[MarshalAs(UnmanagedType.U1)] bool {safeName}");
                }
                else if (isString)
                {
                    paramParts.Add($"[MarshalAs(UnmanagedType.LPUTF8Str)] string {safeName}");
                }
                else
                {
                    paramParts.Add($"{csType} {safeName}");
                }
            }
        }

        if (anyUnsupported)
        {
            skipped.Add(new SkippedItem("Method", flatName, "Unsupported parameter type"));
            sb.AppendLine($"    // SKIPPED: {flatName} — unsupported parameter type");
            sb.AppendLine();
            return;
        }

        string paramStr = string.Join(", ", paramParts);

        sb.AppendLine($"    [LibraryImport(LibName, EntryPoint = \"{flatName}\")]");
        sb.AppendLine($"    [UnmanagedCallConv(CallConvs = new[] {{ typeof(CallConvCdecl) }})]");
        if (returnIsBool)
            sb.AppendLine($"    [return: MarshalAs(UnmanagedType.U1)]");
        if (returnIsString)
            sb.AppendLine($"    [return: MarshalAs(UnmanagedType.LPUTF8Str)]");

        sb.AppendLine($"    internal static partial {csReturn} {disambigName}({paramStr});");
        sb.AppendLine();
    }
}
