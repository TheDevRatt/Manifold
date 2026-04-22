// ManifoldGen — Entry point
// Reads steam_api.json + SDK headers, emits Manifold.Core/Interop/Generated/

using ManifoldGen;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: ManifoldGen <steam_api.json> <output_dir> [--diff <old_json>]");
    return 1;
}

string jsonPath   = args[0];
string outputDir  = args[1];
bool   diffMode   = args.Length >= 4 && args[2] == "--diff";
string? oldJson   = diffMode ? args[3] : null;

if (!File.Exists(jsonPath))
{
    Console.Error.WriteLine($"ERROR: steam_api.json not found at: {jsonPath}");
    return 1;
}

Console.WriteLine($"ManifoldGen — Steamworks SDK P/Invoke Generator");
Console.WriteLine($"  Input:  {jsonPath}");
Console.WriteLine($"  Output: {outputDir}");
Console.WriteLine();

// ── Load and parse the SDK JSON ──────────────────────────────────────────────
SteamApiModel model;
try
{
    string json = File.ReadAllText(jsonPath);
    model = SteamApiModel.Deserialize(json);
    Console.WriteLine($"  Loaded: {model.Interfaces?.Count ?? 0} interfaces, " +
                      $"{model.Structs?.Count ?? 0} structs, " +
                      $"{model.CallbackStructs?.Count ?? 0} callback structs, " +
                      $"{model.Enums?.Count ?? 0} enums, " +
                      $"{model.Typedefs?.Count ?? 0} typedefs");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: Failed to parse steam_api.json: {ex.Message}");
    return 1;
}

// ── Diff mode ────────────────────────────────────────────────────────────────
if (diffMode && oldJson != null)
{
    Console.WriteLine($"\nDiff mode: comparing against {oldJson}");
    string oldJsonText = File.ReadAllText(oldJson);
    var    oldModel    = SteamApiModel.Deserialize(oldJsonText);
    SdkDiffer.PrintDiff(oldModel, model);
    return 0;
}

// ── Parse pragma pack from headers ──────────────────────────────────────────
string sdkHeaderDir = Path.Combine(Path.GetDirectoryName(jsonPath)!, ".");
var packMap = PackPragmaParser.BuildPackMap(sdkHeaderDir);
Console.WriteLine($"  Pack map: {packMap.Count} struct pack overrides derived from headers");

// ── Generate ─────────────────────────────────────────────────────────────────
Directory.CreateDirectory(outputDir);
var context  = new GeneratorContext(model, packMap);
var skipped  = new List<SkippedItem>();

var emitters = new IEmitter[]
{
    new EnumEmitter(),
    new StructEmitter(),
    new CallbackEmitter(),
    new MethodEmitter(),
};

foreach (var emitter in emitters)
{
    string code     = emitter.Emit(context, skipped);
    string filename = emitter.OutputFileName;
    string outPath  = Path.Combine(outputDir, filename);
    File.WriteAllText(outPath, code);
    Console.WriteLine($"  Wrote:  {filename}  ({code.Length:N0} chars)");
}

// ── Skipped items report ─────────────────────────────────────────────────────
if (skipped.Count > 0)
{
    string skippedPath = Path.Combine(outputDir, "ManifoldGen.skipped.json");
    string skippedJson = System.Text.Json.JsonSerializer.Serialize(
        skipped, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(skippedPath, skippedJson);
    Console.WriteLine($"\n  WARNING: {skipped.Count} unsupported constructs skipped — see ManifoldGen.skipped.json");
    foreach (var s in skipped.Take(10))
        Console.WriteLine($"    - [{s.Category}] {s.Name}: {s.Reason}");
    if (skipped.Count > 10)
        Console.WriteLine($"    ... and {skipped.Count - 10} more");
}

Console.WriteLine($"\nGeneration complete. 0 errors.");
return 0;
