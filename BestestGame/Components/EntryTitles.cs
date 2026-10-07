using BestestGame.Models;

namespace BestestGame.Components;

// Shared display-only formatting preserves legacy collection labels and years.
public static class EntryTitles
{
    public readonly record struct TitleDisplay(string Title, string? Subtitle);

    public static TitleDisplay GetTitleDisplay(Game game)
    {
        if (game.IncludedTitles is { Count: > 0 })
            return new(FormatTitleWithYear(game.Title, game.ReleaseYear),
                string.Join(", ", game.IncludedTitles.Select(t => FormatTitleWithYear(t.Title, t.ReleaseYear))));

        // A stored year distinguishes titles such as "Prey (2017)" from legacy
        // titles whose parentheses were used to describe a group of games.
        if (game.ReleaseYear.HasValue)
            return new(FormatTitleWithYear(game.Title, game.ReleaseYear), null);

        return GetTitleDisplay(game.Title);
    }

    public static string FormatTitleWithYear(string title, int? year)
    {
        if (year is null)
            return title;

        var suffix = $" ({year})";
        return title.TrimEnd().EndsWith(suffix, StringComparison.Ordinal) ? title : title + suffix;
    }

    public static TitleDisplay GetTitleDisplay(string gameTitle)
    {
        if (string.IsNullOrWhiteSpace(gameTitle))
        {
            return new(gameTitle, null);
        }

        var trimmedTitle = gameTitle.Trim();
        if (!trimmedTitle.EndsWith(")", StringComparison.Ordinal))
        {
            return new(trimmedTitle, null);
        }

        var openingParenthesisIndex = trimmedTitle.LastIndexOf('(');
        if (openingParenthesisIndex <= 0 || openingParenthesisIndex >= trimmedTitle.Length - 1)
        {
            return new(trimmedTitle, null);
        }

        var title = trimmedTitle[..openingParenthesisIndex].TrimEnd();
        if (string.IsNullOrWhiteSpace(title))
        {
            return new(trimmedTitle, null);
        }

        var parenthesisContent = trimmedTitle[(openingParenthesisIndex + 1)..^1];
        if (string.IsNullOrWhiteSpace(parenthesisContent))
        {
            return new(title, null);
        }

        var subtitleParts = parenthesisContent
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => $"{title} {part}")
            .ToList();

        if (subtitleParts.Count == 0)
        {
            return new(title, null);
        }

        return new(title, string.Join(", ", subtitleParts));
    }

}
