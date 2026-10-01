using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessOS.Pharmacy.Licensing;

internal static class ProtectedStateJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ReadOnlyStringSetConverter());
        return options;
    }

    private sealed class ReadOnlyStringSetConverter : JsonConverter<IReadOnlySet<string>>
    {
        public override IReadOnlySet<string> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("A JSON array is required for the protected string set.");
            }

            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return values;
                }

                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("Protected string-set values must be JSON strings.");
                }

                var value = reader.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }

            throw new JsonException("The protected string set was not terminated.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            IReadOnlySet<string> value,
            JsonSerializerOptions options)
        {
            writer.WriteStartArray();

            foreach (var item in value)
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();
        }
    }
}
