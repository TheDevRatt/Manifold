// ManifoldGen — Callback struct emitter
// Emits Steam callback structs with k_iCallback constants in SteamNative.Callbacks.cs

using System.Text;

namespace ManifoldGen;

public sealed class CallbackEmitter : IEmitter
{
    public string OutputFileName => "SteamNative.Callbacks.cs";

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

        if (ctx.Model.CallbackStructs != null)
        {
            foreach (var cb in ctx.Model.CallbackStructs)
            {
                if (string.IsNullOrEmpty(cb.Name)) continue;
                StructEmitter.EmitStruct(sb, cb.Name!, cb.Fields, cb.CallbackId, ctx.PackMap, skipped);
            }
        }

        sb.AppendLine("#pragma warning restore CS1591");
        return sb.ToString();
    }
}
