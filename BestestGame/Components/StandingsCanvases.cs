namespace BestestGame.Components;

/// <summary>
/// Which ranked entries stand on canvases above a standings table. Rankings and Game of the year share these
/// rules. They hold for any number of entries: a short list fills fewer canvases, a long one continues in the table.
/// </summary>
public static class StandingsCanvases
{
    /// <summary>A row of smaller canvases below the podium. The farther of two rows is the smaller one.</summary>
    public sealed record Row<T>(List<T> Places, int Columns, bool Far);

    public sealed record Arrangement<T>(List<T> Podium, List<Row<T>> Rows)
    {
        public int Count => Podium.Count + Rows.Sum(row => row.Places.Count);
    }

    // Under a podium of three, lists of these lengths would leave a canvas alone or their rows uneven. They put
    // two entries on the podium instead, the second taking the whole side of the leader, and share the rest
    // between the rows: the entries on the podium, then on each row below it.
    private static readonly Dictionary<int, int[]> Evened = new()
    {
        [3] = [2, 1], [4] = [2, 2], [5] = [2, 3], [7] = [2, 2, 3], [8] = [2, 3, 3], [10] = [2, 4, 4],
    };

    public static Arrangement<T> Arrange<T>(IReadOnlyList<T> ranked, Func<T, int> rankOf)
    {
        // Entries sharing a rank stand together: a list that one of these arrangements would part there
        // falls back to the arrangement by rank.
        if (Evened.TryGetValue(ranked.Count, out var sizes))
        {
            var starts = sizes.Select((_, index) => sizes.Take(index).Sum()).ToList();
            if (starts.Skip(1).All(start => rankOf(ranked[start - 1]) != rankOf(ranked[start])))
                return new(ranked.Take(sizes[0]).ToList(), sizes.Skip(1).Select((size, index) =>
                    new Row<T>(ranked.Skip(starts[index + 1]).Take(size).ToList(), size, index == 1)).ToList());
        }

        // By rank the top three stand on the podium. A tie that would put more than six entries there leaves
        // everything in the table.
        if (ranked.TakeWhile(entry => rankOf(entry) <= 3).ToList() is not { Count: > 0 and <= 6 } podium)
            return new([], []);

        // Below it, ranks 4–6 and 7–10 each stand on a row. A row that ties would swell past twice its size
        // stays in the table, and so does everything ranked after it.
        List<Row<T>> rows = [];
        var taken = podium.Count;
        foreach (var (lastRank, columns) in new[] { (6, 3), (10, 4) })
        {
            var places = ranked.Skip(taken).TakeWhile(entry => rankOf(entry) <= lastRank).ToList();
            if (places.Count > columns * 2) break;
            if (places.Count == 0) continue;
            rows.Add(new(places, columns, columns == 4));
            taken += places.Count;
        }
        return new(podium, rows);
    }
}
