using BestestGame.Models;

namespace BestestGame.Services;

/// <summary>
/// Lays out every opponent of a focused entry along one bar in a fixed order.
/// Neighbours in the same queue group, or with the same saved result, form a segment. Results saved while
/// voting can be kept apart by the group they were voted in, so a group keeps its place once voted on.
/// </summary>
public static class OpponentBar
{
    public enum Status { Pending, Won, Lost, Banned }

    /// <param name="Group">Queue position of a pending group; -1 when the opponents are not queued.</param>
    /// <param name="Start">Number of opponents to the left of this segment.</param>
    /// <param name="VotedIn">The group a won or lost segment was voted in; null for results from before.</param>
    public sealed record Segment(Status Status, int Group, int Start, IReadOnlyList<Guid> DuelIds, Guid? VotedIn = null);

    public static Guid OpponentId(Duel duel, Guid entryId) => duel.Game1Id == entryId ? duel.Game2Id : duel.Game1Id;

    public static Status StatusOf(Duel duel, Guid entryId, IReadOnlySet<Guid> bannedGameIds) => duel.IsCompleted
        ? duel.WinnerId == entryId ? Status.Won : Status.Lost
        : bannedGameIds.Contains(duel.Game1Id) || bannedGameIds.Contains(duel.Game2Id) ? Status.Banned : Status.Pending;

    /// <summary>
    /// One entry's duels with their opponents from highest to lowest ranked. The point an
    /// opponent won from this entry is left out of its standing, so saving a result never
    /// moves that opponent along the bar.
    /// </summary>
    public static Duel[] StandingOrder(IReadOnlyList<Game> games, IEnumerable<Duel> duels, Guid entryId)
    {
        var own = duels.Where(duel => duel.Game1Id == entryId || duel.Game2Id == entryId).ToArray();
        var beatEntry = own.Where(duel => duel.IsCompleted && duel.WinnerId == OpponentId(duel, entryId))
            .Select(duel => OpponentId(duel, entryId)).ToHashSet();
        var order = games.OrderByDescending(game => game.Points - (beatEntry.Contains(game.Id) ? 1 : 0))
            .Select((game, rank) => (game.Id, rank)).ToDictionary(item => item.Id, item => item.rank);
        return own.Where(duel => order.ContainsKey(duel.Game1Id) && order.ContainsKey(duel.Game2Id))
            .OrderBy(duel => order[OpponentId(duel, entryId)]).ToArray();
    }

    /// <param name="votedIn">Duels voted on this session, each with a key for the group it was voted in.</param>
    public static IReadOnlyList<Segment> Build(IEnumerable<(Guid DuelId, Status Status)> opponents, Guid[][] groups,
        IReadOnlyDictionary<Guid, Guid>? votedIn = null)
    {
        var groupOf = new Dictionary<Guid, int>();
        for (var index = 0; index < groups.Length; index++)
            foreach (var id in groups[index])
                groupOf.TryAdd(id, index);

        var segments = new List<Segment>();
        var run = new List<Guid>();
        var position = 0;
        foreach (var (duelId, status) in opponents)
        {
            var group = status == Status.Pending ? groupOf.GetValueOrDefault(duelId, -1) : -1;
            Guid? voted = status is Status.Won or Status.Lost && votedIn?.TryGetValue(duelId, out var key) == true ? key : null;
            // A group whose opponents are no longer neighbours is drawn as several segments.
            if (segments.Count == 0 || segments[^1].Status != status || segments[^1].Group != group || segments[^1].VotedIn != voted)
                segments.Add(new(status, group, position, run = [], voted));
            run.Add(duelId);
            position++;
        }
        return segments;
    }
}
