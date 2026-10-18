using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BestestGame.Models;

public class Game
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    [Range(1, 9999, ErrorMessage = "Release year must be between 1 and 9999.")]
    public int? ReleaseYear { get; set; }
    public List<IncludedTitle> IncludedTitles { get; set; } = new();
    public int Points { get; set; } = 0;
    /// <summary>
    /// File name of this entry's picture in the covers folder beside the database, if it has one.
    /// A collection keeps none of its own: its included titles carry theirs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CoverImage { get; set; }

    /// <summary>The picture that stands for this entry: its own, or for a collection that of its first included title that has one.</summary>
    [JsonIgnore]
    public string? Cover => (IncludedTitles ?? []).Select(title => title.CoverImage).FirstOrDefault(file => !string.IsNullOrEmpty(file)) ?? CoverImage;
}
