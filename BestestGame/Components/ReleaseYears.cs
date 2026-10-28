using System.Text.RegularExpressions;
using BestestGame.Models;
using BestestGame.Services;

namespace BestestGame.Components;

/// <summary>Which titles of an entry still have no release year, and what to look each of them up by.</summary>
public static partial class ReleaseYears
{
    /// <summary>A title without a year: a single game, or one included title of a collection, counted from zero.</summary>
    public sealed record Gap(Game Game, int? Title)
    {
        /// <summary>The title as stored, which <see cref="GameService.FillReleaseYear"/> checks before writing.</summary>
        public string Stored => Title is { } index ? Game.IncludedTitles[index].Title : Game.Title;
        /// <summary>The name to search IGDB for, without trailing parentheses.</summary>
        public string Subject => CoverArtService.Subject(Game, Title).Title;
    }

    /// <summary>
    /// The titles of an entry that count as having no year in Game of the year: each undated included title of a
    /// collection, or an undated single game. A legacy collection, whose parts are listed in its title's
    /// parentheses, is left out, because a year would turn that list into part of its name.
    /// </summary>
    public static IReadOnlyList<Gap> Missing(Game game)
    {
        if (game.IncludedTitles is { Count: > 0 } titles)
            return titles.Select((title, index) => (title, index)).Where(item => item.title.ReleaseYear is null)
                .Select(item => new Gap(game, item.index)).ToList();
        return game.ReleaseYear is null && !IsLegacyCollection(game) ? [new Gap(game, null)] : [];
    }

    /// <summary>A single entry whose title lists the parts of a collection in parentheses, such as "Metro (2033, Exodus)".</summary>
    public static bool IsLegacyCollection(Game game)
        => game.IncludedTitles is not { Count: > 0 } && game.ReleaseYear is null && WrittenYear(game.Title) is null &&
            EntryTitles.GetTitleDisplay(game.Title).Subtitle is not null;

    /// <summary>A year already written at the end of a title, as in "Prey (2017)".</summary>
    public static int? WrittenYear(string title)
        => YearSuffix().Match(title) is { Success: true } match && int.Parse(match.Groups[1].Value) is var year and >= 1 ? year : null;

    [GeneratedRegex(@"\((\d{4})\)\s*$")]
    private static partial Regex YearSuffix();
}
