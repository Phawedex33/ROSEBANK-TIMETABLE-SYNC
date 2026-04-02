using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimetableSync.Api.Services;

public sealed class FlexibleTimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] AcceptedFormats = ["HH:mm", "HH:mm:ss"];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("A time value is required.");
        }

        // Accept both browser-friendly HH:mm values and full HH:mm:ss values from .NET clients.
        if (TimeOnly.TryParseExact(value, AcceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        throw new JsonException($"Invalid time value '{value}'. Expected HH:mm or HH:mm:ss.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }
}
