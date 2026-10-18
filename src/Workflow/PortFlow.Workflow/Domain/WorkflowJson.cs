using System.Text.Json;
using System.Text.Json.Serialization;
using PortFlow.Shared.Config;
using PortFlow.Shared.Domain;
using PortFlow.Workflow.Domain.Definition;

namespace PortFlow.Workflow.Domain;

/// <summary>
/// JSON của định nghĩa quy trình (cột <c>definition</c>, đọc chặt: thuộc tính lạ → V0) và biến ngữ cảnh
/// (cột <c>context_vars</c>, mọi số là decimal như BigDecimal của Java).
/// </summary>
public static class WorkflowJson
{
    public static readonly JsonSerializerOptions DefinitionOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static DefinitionContent ReadDefinition(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DefinitionContent>(json, DefinitionOptions)
                   ?? throw new JsonException("Definition is null");
        }
        catch (JsonException ex)
        {
            throw new DomainException(WorkflowError.WF_DEFINITION_INVALID,
                "Definition does not match the expected JSON structure",
                new Dictionary<string, object?>
                {
                    ["errors"] = new[]
                    {
                        new Dictionary<string, object?> { ["field"] = "$", ["code"] = "V0", ["message"] = ex.Message },
                    },
                });
        }
    }

    public static string Write(DefinitionContent content) => JsonSerializer.Serialize(content, DefinitionOptions);

    public static string WriteVars(IReadOnlyDictionary<string, object?> vars) =>
        JsonSerializer.Serialize(vars, JsonConfig.Options);

    public static Dictionary<string, object?> ReadVars(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, object?>();
        }
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? (Dictionary<string, object?>)FromElement(document.RootElement)!
            : new Dictionary<string, object?>();
    }

    /// <summary>Chuẩn hóa: mọi số → decimal, JsonElement → giá trị .NET; giữ thứ tự khóa.</summary>
    public static Dictionary<string, object?> NormalizeVars(IEnumerable<KeyValuePair<string, object?>>? vars)
    {
        var normalized = new Dictionary<string, object?>();
        if (vars is not null)
        {
            foreach (var (key, value) in vars)
            {
                normalized[key] = Normalize(value);
            }
        }
        return normalized;
    }

    public static object? Normalize(object? value) => value switch
    {
        null => null,
        decimal d => d,
        int or long or short or byte or sbyte or uint or ulong or ushort => Convert.ToDecimal(value),
        double or float => decimal.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture),
        JsonElement element => FromElement(element),
        IReadOnlyDictionary<string, object?> map => NormalizeVars(map),
        IDictionary<string, object?> map => NormalizeVars(map),
        string or bool => value,
        System.Collections.IEnumerable list => list.Cast<object?>().Select(Normalize).ToList(),
        _ => value,
    };

    private static object? FromElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => FromElement(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(FromElement).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
