using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace PortFlow.Shared.Config;

/// <summary>
/// Một chỗ duy nhất cấu hình JSON (= JacksonConfig): dùng chung cho MVC, event (outbox/RabbitMQ) và idempotency
/// để bản phát lại giống từng byte bản gốc.
/// </summary>
public static class JsonConfig
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = null;
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ReadOnlySetConverterFactory());
        options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        return options;
    }
}

/// <summary>Đọc/ghi <c>IReadOnlySet&lt;T&gt;</c> (= <c>Set&lt;T&gt;</c> của Java) dưới dạng mảng JSON.</summary>
internal sealed class ReadOnlySetConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlySet<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(ReadOnlySetConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class ReadOnlySetConverter<T> : JsonConverter<IReadOnlySet<T>>
    {
        public override IReadOnlySet<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonSerializer.Deserialize<List<T>>(ref reader, options)?.ToHashSet();

        public override void Write(Utf8JsonWriter writer, IReadOnlySet<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.AsEnumerable(), options);
    }
}
