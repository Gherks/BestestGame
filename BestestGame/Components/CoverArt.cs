using BestestGame.Models;

namespace BestestGame.Components;

// Where an entry's stored picture is served from.
public static class CoverArt
{
    public static string? Url(Game? game)
        => string.IsNullOrEmpty(game?.CoverImage) ? null : $"/covers/{Uri.EscapeDataString(game.CoverImage)}";
}
