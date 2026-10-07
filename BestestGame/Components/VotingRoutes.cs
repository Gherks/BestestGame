namespace BestestGame.Components;

public static class VotingRoutes
{
    public static string Rankings(int? year = null)
        => year is { } value ? $"/rankings?year={value}" : "/rankings";

    public static string Focus(Guid? entry, int? year = null)
        => entry is { } id ? $"/vote?focus={id}" + (year is { } value ? $"&year={value}" : "") : "/vote";

    public static string? LegacyDestination(int? year, Guid? focus, string page = "vote")
    {
        // Query parameters can reach a departing component before it is disposed.
        // A GOTY/Rankings year must not trigger Voting's compatibility redirect.
        var path = page.Split('?', '#')[0].Trim('/');
        return path.Equals("vote", StringComparison.OrdinalIgnoreCase) && year is not null && focus is null
            ? Rankings(year) : null;
    }
}
