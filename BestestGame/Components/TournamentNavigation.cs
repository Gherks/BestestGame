using Microsoft.AspNetCore.Components;

namespace BestestGame.Components;

public static class TournamentNavigation
{
    // A focus identifies a participant in the old tournament. A year remains
    // meaningful only on pages that browse the new tournament's release years.
    public static string SwitchDestination(NavigationManager navigation)
    {
        var path = navigation.ToBaseRelativePath(navigation.Uri).Split('?', '#')[0].Trim('/');
        var keepYear = path.Equals("rankings", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("goty", StringComparison.OrdinalIgnoreCase);
        var parameters = new Dictionary<string, object?> { ["focus"] = null };
        if (!keepYear) parameters["year"] = null;
        return navigation.GetUriWithQueryParameters(parameters);
    }
}
