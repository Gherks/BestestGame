using System.ComponentModel.DataAnnotations;

namespace BestestGame.Models;

public class Game
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    [Range(1, 9999, ErrorMessage = "Release year must be between 1 and 9999.")]
    public int? ReleaseYear { get; set; }
    public List<IncludedTitle> IncludedTitles { get; set; } = new();
    public int Points { get; set; } = 0;
}
