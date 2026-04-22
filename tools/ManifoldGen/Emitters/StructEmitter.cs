// ManifoldGen — Struct emitter
// Emits general (non-callback) Steam structs as [StructLayout] types in SteamNative.Structs.cs

using System.Text;

namespace ManifoldGen;

public sealed class StructEmitter : IEmitter
{
    public string OutputFileName => "SteamNative.Structs.cs";

    public string Emit(GeneratorContext ctx, List<SkippedItem> skipped)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ctx.FileHeader(OutputFileName));
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine();
        sb.AppendLine("namespace Manifold.Core.Interop;");
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CS1591");
        sb.AppendLine();

        if (ctx.Model.Structs != null)
        {
            foreach (var s in ctx.Model.Structs)
            {
                if (string.IsNullOrEmpty(s.Name)) continue;
                EmitStruct(sb, s.Name!, s.Fields, null, ctx.PackMap, skipped);
            }
        }

        sb.AppendLine("#pragma warning restore CS1591");
        return sb.ToString();
    }

    internal static void EmitStruct(
        StringBuilder sb,
        string name,
        List<SteamField>? fields,
        int? callbackId,
        Dictionary<string, int> packMap,
        List<SkippedItem> skipped)
    {
        // Determine pack value
        int? explicitPack = PackPragmaParser.GetExplicitPack(name, packMap);

        if (explicitPack.HasValue)
        {
            // Hard explicit pack value (e.g. Pack=1 for SteamNetworkingMessage_t)
            sb.AppendLine($"[StructLayout(LayoutKind.Sequential, Pack = {explicitPack.Value})]");
        }
        else
        {
            // Platform-conditional: Pack=4 (Linux/macOS) or Pack=8 (Windows)
            sb.AppendLine("#if MANIFOLD_PACK_SMALL");
            sb.AppendLine($"[StructLayout(LayoutKind.Sequential, Pack = 4)]");
            sb.AppendLine("#else");
            sb.AppendLine($"[StructLayout(LayoutKind.Sequential, Pack = 8)]");
            sb.AppendLine("#endif");
        }

        sb.AppendLine($"internal unsafe struct {name}");
        sb.AppendLine("{");

        if (callbackId.HasValue)
            sb.AppendLine($"    internal const int k_iCallback = {callbackId.Value};");

        if (fields != null)
        {
            foreach (var f in fields)
            {
                if (string.IsNullOrEmpty(f.FieldName) || string.IsNullOrEmpty(f.FieldType)) continue;
                EmitField(sb, f, skipped);
            }
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitField(StringBuilder sb, SteamField field, List<SkippedItem> skipped)
    {
        string rawType  = field.FieldType!;
        string rawName  = field.FieldName!;
        string safeName = TypeMapper.SanitiseIdentifier(rawName);

        // ── Fixed-size array detection: e.g. "char [256]" or "uint8 [16]" ────
        var arrayMatch = System.Text.RegularExpressions.Regex.Match(rawType, @"^(.+?)\s*\[(\d+)\]$");
        if (arrayMatch.Success)
        {
            string elemType = arrayMatch.Groups[1].Value.Trim();
            int    count    = int.Parse(arrayMatch.Groups[2].Value);

            var (csElem, isBool, isString, isUnsupported) = TypeMapper.Map(elemType);

            // char arrays are byte fixed buffers
            if (elemType.Trim() == "char") csElem = "byte";

            if (isUnsupported && elemType.Trim() != "char")
            {
                skipped.Add(new SkippedItem("StructField", $"{field.FieldType} {field.FieldName}", "Unsupported array element type"));
                sb.AppendLine($"    // SKIPPED: {rawType} {rawName}");
                return;
            }

            // Use fixed buffer for blittable element types
            if (csElem is "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "float" or "double")
            {
                sb.AppendLine($"    internal fixed {csElem} {safeName}[{count}];");
            }
            else
            {
                // Non-blittable array — emit as IntPtr array stub
                sb.AppendLine($"    // NOTE: array [{count}] of {csElem} — emitted as IntPtr");
                sb.AppendLine($"    internal fixed byte {safeName}_raw[{count * 8}]; // {rawType}");
            }
            return;
        }

        // ── Regular field ─────────────────────────────────────────────────────
        var (csType, needsBoolMarshal, needsStringMarshal, unsupported) = TypeMapper.Map(rawType);

        if (unsupported)
        {
            skipped.Add(new SkippedItem("StructField", $"{rawType} {rawName}", "Unsupported type"));
            sb.AppendLine($"    // SKIPPED: {rawType} {rawName}");
            return;
        }

        if (needsBoolMarshal)
        {
            // Steam bool = 1 byte; C# bool requires explicit MarshalAs
            sb.AppendLine($"    [MarshalAs(UnmanagedType.U1)]");
            sb.AppendLine($"    internal bool {safeName};");
        }
        else if (needsStringMarshal)
        {
            sb.AppendLine($"    internal IntPtr {safeName}; // const char* — use Marshal.PtrToStringUTF8");
        }
        else
        {
            sb.AppendLine($"    internal {csType} {safeName};");
        }
    }
}
