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
    /// <summary>File name of this entry's picture in the covers folder beside the database, if it has one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CoverImage { get; set; }
}
