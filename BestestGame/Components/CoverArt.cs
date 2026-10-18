using BestestGame.Models;

namespace BestestGame.Components;

// Where a stored picture is served from.
public static class CoverArt
{
    /// <summary>The picture that stands for an entry; a collection shows that of its first included title that has one.</summary>
    public static string? Url(Game? game) => Url(game?.Cover);

    public static string? Url(string? file)
        => string.IsNullOrEmpty(file) ? null : $"/covers/{Uri.EscapeDataString(file)}";
}
