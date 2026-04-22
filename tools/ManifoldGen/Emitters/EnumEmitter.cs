// ManifoldGen — Enum emitter
// Emits all Steam enums as C# enum types in SteamNative.Enums.cs

using System.Text;

namespace ManifoldGen;

public sealed class EnumEmitter : IEmitter
{
    public string OutputFileName => "SteamNative.Enums.cs";

    public string Emit(GeneratorContext ctx, List<SkippedItem> skipped)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ctx.FileHeader(OutputFileName));
        sb.AppendLine("namespace Manifold.Core.Interop;");
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CA1712 // enum values should not be prefixed");
        sb.AppendLine("#pragma warning disable CS1591 // missing XML doc on public member");
        sb.AppendLine();

        var allEnums = new List<SteamEnum>();

        // Top-level enums
        if (ctx.Model.Enums != null)
            allEnums.AddRange(ctx.Model.Enums);

        // Interface-nested enums
        if (ctx.Model.Interfaces != null)
            foreach (var iface in ctx.Model.Interfaces)
                if (iface.Enums != null)
                    allEnums.AddRange(iface.Enums);

        foreach (var e in allEnums)
        {
            if (string.IsNullOrEmpty(e.EnumName)) continue;
            EmitEnum(sb, e, skipped);
        }

        sb.AppendLine("#pragma warning restore CA1712");
        sb.AppendLine("#pragma warning restore CS1591");

        return sb.ToString();
    }

    private static void EmitEnum(StringBuilder sb, SteamEnum e, List<SkippedItem> skipped)
    {
        string name = e.EnumName!;

        if (e.Values == null || e.Values.Count == 0)
        {
            skipped.Add(new SkippedItem("Enum", name, "No values defined"));
            return;
        }

        // Determine underlying type — use int by default, uint if any value >= 2^31
        bool needsUint = e.Values.Any(v => IsLargeUnsigned(v.Value));
        string underlying = needsUint ? "uint" : "int";

        sb.AppendLine($"public enum {name} : {underlying}");
        sb.AppendLine("{");

        foreach (var v in e.Values)
        {
            if (string.IsNullOrEmpty(v.Name)) continue;
            string valName = SanitiseEnumMemberName(v.Name, name);
            string valStr  = NormaliseEnumValue(v.Value ?? "0", needsUint);
            sb.AppendLine($"    {valName} = {valStr},");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string SanitiseEnumMemberName(string raw, string enumName)
    {
        // Strip k_E prefix: k_ELobbyTypePublic → LobbyTypePublic
        if (raw.StartsWith("k_E")) raw = raw["k_E".Length..];
        // Strip k_ prefix: k_SomeThing → SomeThing
        else if (raw.StartsWith("k_")) raw = raw["k_".Length..];

        // If identifier starts with a digit, prefix with underscore
        if (raw.Length > 0 && char.IsDigit(raw[0])) raw = "_" + raw;

        // Escape reserved keywords
        if (TypeMapper.IsReservedKeyword(raw)) raw = "@" + raw;

        return raw;
    }

    private static string NormaliseEnumValue(string raw, bool isUint)
    {
        if (string.IsNullOrEmpty(raw)) return "0";
        // Remove trailing u/U suffix if present — C# doesn't use it for enum values
        raw = raw.TrimEnd('u', 'U');
        // Handle hex
        if (raw.StartsWith("0x") || raw.StartsWith("0X")) return raw;
        // Handle negative
        if (raw.StartsWith("-")) return raw;
        return raw;
    }

    private static bool IsLargeUnsigned(string? val)
    {
        if (val == null) return false;
        val = val.TrimEnd('u', 'U');
        if (val.StartsWith("0x") || val.StartsWith("0X"))
        {
            if (ulong.TryParse(val[2..], System.Globalization.NumberStyles.HexNumber, null, out ulong hex))
                return hex > int.MaxValue;
        }
        if (long.TryParse(val, out long l)) return l > int.MaxValue || l < int.MinValue;
        return false;
    }
}
