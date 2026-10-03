using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BestestGame.Models;

[JsonConverter(typeof(IncludedTitleConverter))]
public class IncludedTitle
{
    public string Title { get; set; } = string.Empty;
    [Range(1, 9999, ErrorMessage = "Release year must be between 1 and 9999.")]
    public int? ReleaseYear { get; set; }
}

// Read legacy string entries as well as the new objects; always save objects.
public sealed class IncludedTitleConverter : JsonConverter<IncludedTitle>
{
    public override IncludedTitle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new IncludedTitle { Title = reader.GetString()! };

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new IncludedTitle
        {
            Title = root.GetProperty("Title").GetString() ?? string.Empty,
            ReleaseYear = root.TryGetProperty("ReleaseYear", out var year) && year.ValueKind != JsonValueKind.Null
                ? year.GetInt32() : null
        };
    }

    public override void Write(Utf8JsonWriter writer, IncludedTitle value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new { value.Title, value.ReleaseYear }, options);
}
