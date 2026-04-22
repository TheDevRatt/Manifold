// ManifoldGen — Steam API JSON model
// Deserializes steam_api.json into typed C# objects.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManifoldGen;

// ── Top-level model ──────────────────────────────────────────────────────────

public sealed class SteamApiModel
{
    [JsonPropertyName("interfaces")]
    public List<SteamInterface>? Interfaces { get; set; }

    [JsonPropertyName("structs")]
    public List<SteamStruct>? Structs { get; set; }

    [JsonPropertyName("callback_structs")]
    public List<SteamCallbackStruct>? CallbackStructs { get; set; }

    [JsonPropertyName("enums")]
    public List<SteamEnum>? Enums { get; set; }

    [JsonPropertyName("typedefs")]
    public List<SteamTypedef>? Typedefs { get; set; }

    [JsonPropertyName("consts")]
    public List<SteamConst>? Consts { get; set; }

    public static SteamApiModel Deserialize(string json)
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas         = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
        };
        return JsonSerializer.Deserialize<SteamApiModel>(json, opts)
               ?? throw new InvalidOperationException("Failed to deserialize steam_api.json");
    }
}

// ── Interfaces ───────────────────────────────────────────────────────────────

public sealed class SteamInterface
{
    [JsonPropertyName("classname")]
    public string? ClassName { get; set; }

    [JsonPropertyName("methods")]
    public List<SteamMethod>? Methods { get; set; }

    [JsonPropertyName("enums")]
    public List<SteamEnum>? Enums { get; set; }
}

public sealed class SteamMethod
{
    [JsonPropertyName("methodname")]
    public string? MethodName { get; set; }

    [JsonPropertyName("methodname_flat")]
    public string? MethodNameFlat { get; set; }

    [JsonPropertyName("returntype")]
    public string? ReturnType { get; set; }

    [JsonPropertyName("params")]
    public List<SteamParam>? Params { get; set; }

    [JsonPropertyName("callresult")]
    public string? CallResult { get; set; }

    [JsonPropertyName("callback")]
    public string? Callback { get; set; }
}

public sealed class SteamParam
{
    [JsonPropertyName("paramname")]
    public string? ParamName { get; set; }

    [JsonPropertyName("paramtype")]
    public string? ParamType { get; set; }

    [JsonPropertyName("paramtype_flat")]
    public string? ParamTypeFlat { get; set; }

    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    [JsonPropertyName("out_array_count")]
    public string? OutArrayCount { get; set; }

    [JsonPropertyName("out_array_call")]
    public string? OutArrayCall { get; set; }

    [JsonPropertyName("array_count")]
    public string? ArrayCount { get; set; }

    [JsonPropertyName("out_string_count")]
    public string? OutStringCount { get; set; }

    [JsonPropertyName("out_struct")]
    public string? OutStruct { get; set; }
}

// ── Structs ──────────────────────────────────────────────────────────────────

public sealed class SteamStruct
{
    [JsonPropertyName("struct")]
    public string? Name { get; set; }

    [JsonPropertyName("fields")]
    public List<SteamField>? Fields { get; set; }

    [JsonPropertyName("enums")]
    public List<SteamEnum>? Enums { get; set; }
}

public sealed class SteamCallbackStruct
{
    [JsonPropertyName("struct")]
    public string? Name { get; set; }

    [JsonPropertyName("callback_id")]
    public int CallbackId { get; set; }

    [JsonPropertyName("fields")]
    public List<SteamField>? Fields { get; set; }

    [JsonPropertyName("enums")]
    public List<SteamEnum>? Enums { get; set; }
}

public sealed class SteamField
{
    [JsonPropertyName("fieldname")]
    public string? FieldName { get; set; }

    [JsonPropertyName("fieldtype")]
    public string? FieldType { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}

// ── Enums ────────────────────────────────────────────────────────────────────

public sealed class SteamEnum
{
    [JsonPropertyName("enumname")]
    public string? EnumName { get; set; }

    [JsonPropertyName("fqname")]
    public string? FqName { get; set; }

    [JsonPropertyName("values")]
    public List<SteamEnumValue>? Values { get; set; }
}

public sealed class SteamEnumValue
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

// ── Typedefs ─────────────────────────────────────────────────────────────────

public sealed class SteamTypedef
{
    [JsonPropertyName("typedef")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

// ── Consts ───────────────────────────────────────────────────────────────────

public sealed class SteamConst
{
    [JsonPropertyName("constname")]
    public string? Name { get; set; }

    [JsonPropertyName("consttype")]
    public string? Type { get; set; }

    [JsonPropertyName("constval")]
    public string? Value { get; set; }
}
