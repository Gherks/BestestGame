using System.Net;
using System.Text;
using System.Text.Json;
using BestestGame.Models;
using BestestGame.Services;
using Microsoft.Extensions.Configuration;

static class CoverArtChecks
{
    // Stands in for Twitch and IGDB, recording what the service asks of them.
    private sealed class Igdb : HttpMessageHandler
    {
        public readonly List<string> Requests = [];
        public readonly List<string> Queries = [];
        public string Games = "[]";
        public bool RejectNextQuery;
        public int Tokens;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            if (path == "/token")
            {
                var form = await request.Content!.ReadAsStringAsync(cancellation);
                if (!form.Contains("client_secret=right")) return new(HttpStatusCode.Forbidden);
                Tokens++;
                return Json($"{{\"access_token\":\"token-{Tokens}\",\"expires_in\":5000,\"token_type\":\"bearer\"}}");
            }
            if (path == "/v4/games")
            {
                if (request.Headers.GetValues("Client-ID").Single() != "client" ||
                    request.Headers.Authorization?.ToString() != $"Bearer token-{Tokens}") return new(HttpStatusCode.BadRequest);
                if (RejectNextQuery) { RejectNextQuery = false; return new(HttpStatusCode.Unauthorized); }
                Queries.Add(await request.Content!.ReadAsStringAsync(cancellation));
                return Json(Games);
            }
            return path.StartsWith("/images/") && !path.Contains("missing")
                ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(path)) }
                : new(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    public static async Task RunAsync(string directory)
    {
        var path = CheckData.PathFor(Path.Combine(directory, "covers"), "data");
        var solo = new Game { Title = "Dark Souls", ReleaseYear = 2011 };
        var collection = new Game { Title = "Metro", IncludedTitles = [new() { Title = "Metro 2033", ReleaseYear = 2010 }, new() { Title = "Metro Exodus" }] };
        var legacy = new Game { Title = "Assassin's Creed (1, 2)" };
        var tournament = new Tournament { Name = "Covers", Games = [solo, collection, legacy] };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Dictionary<string, string?> settings = new()
        {
            ["DatabasePath"] = path, ["Igdb:TokenUrl"] = "https://stub/token", ["Igdb:ApiUrl"] = "https://stub/v4", ["Igdb:ImageUrl"] = "https://stub/images"
        };
        var games = new GameService(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), null!);
        games.Import(new GameDatabase { CurrentTournamentId = tournament.Id, Tournaments = [tournament] });
        Check(!CheckData.Snapshot(games).Contains("CoverImage") && JsonSerializer.Deserialize<Game>("{\"Title\":\"Old\"}")!.CoverImage is null,
            "Entries without a cover are exported exactly as before, and older data loads without one");
        var igdb = new Igdb();
        CoverArtService Service() => new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), games, new HttpClient(igdb));

        var covers = Service();
        Check(!covers.IsConfigured && await Fails(() => covers.SearchAsync("Dark Souls")) && igdb.Requests.Count == 0,
            "Without credentials nothing is requested from outside");
        settings["Igdb:ClientId"] = "client";
        settings["Igdb:ClientSecret"] = "wrong";
        covers = Service();
        Check(covers.IsConfigured && await Fails(() => covers.SearchAsync("Dark Souls")) && igdb.Queries.Count == 0,
            "Rejected credentials are reported before any lookup is made");
        settings["Igdb:ClientSecret"] = "right";
        covers = Service();

        Check(CoverArtService.Subject(solo) == ("Dark Souls", 2011) && CoverArtService.Subject(collection) == ("Metro 2033", 2010) &&
            CoverArtService.Subject(legacy) == ("Assassin's Creed", null),
            "A collection is looked up by its first included title, and trailing parentheses are left out");

        CoverArtService.Candidate[] found = [new("co1", "DARK SOULS", 2011), new("co2", "Dark Souls: Remastered", 2018), new("co3", "Dark Souls", 2018)];
        Check(CoverArtService.Confident(found, "Dark Souls", 2011)?.ImageId == "co1" && CoverArtService.Confident(found, "Dark Souls", 2012)?.ImageId == "co1" &&
            CoverArtService.Confident(found, "dark-souls", 2017)?.ImageId == "co3",
            "A same-named candidate within a year of the entry is used, ignoring case and punctuation");
        Check(CoverArtService.Confident(found, "Dark Souls", 2000) is null && CoverArtService.Confident(found, "Dark Souls", null) is null &&
            CoverArtService.Confident(found, "Dark Souls II", 2014) is null,
            "A wrong year, several same-named games without a year, or a different name all need a person to choose");
        Check(CoverArtService.Confident([new("co4", "Pokémon Red", null)], "Pokemon Red", 1996)?.ImageId == "co4" &&
            CoverArtService.Confident([new("co5", "Celeste", 2018)], "Celeste", null)?.ImageId == "co5",
            "Accents are set aside, and a single same-named candidate is enough when a year is unknown");

        igdb.Games = "[{\"id\":1,\"name\":\"Dark Souls\",\"first_release_date\":1316649600,\"cover\":{\"id\":9,\"image_id\":\"co1x78\"}}," +
            "{\"id\":2,\"name\":\"No cover\"},{\"id\":3,\"name\":\"Bad id\",\"cover\":{\"image_id\":\"../x\"}}]";
        var candidates = await covers.SearchAsync("Dark \"Souls\"");
        Check(candidates.SequenceEqual([new CoverArtService.Candidate("co1x78", "Dark Souls", 2011)]) && igdb.Queries[^1].StartsWith("search \"Dark \\\"Souls\\\"\";"),
            "A lookup quotes the title safely and keeps only results with a usable cover");
        await covers.SearchAsync("Dark Souls");
        Check(igdb.Tokens == 1, "One token serves several lookups");
        igdb.RejectNextQuery = true;
        Check((await covers.SearchAsync("Dark Souls")).Count == 1 && igdb.Tokens == 2, "A token refused early is replaced and the lookup repeated once");

        var result = await covers.FetchAsync(solo);
        var stored = games.GetGames().Single(game => game.Id == solo.Id).CoverImage;
        Check(result.Outcome == CoverArtService.Outcome.Added && stored == $"{solo.Id:N}-co1x78.jpg" &&
            File.ReadAllText(Path.Combine(games.CoversDirectory, stored)) == "/images/t_cover_big_2x/co1x78.jpg" && covers.HasCoverFile(games.GetGames().Single(game => game.Id == solo.Id)),
            "A confident match is downloaded beside the database and recorded on the entry");
        igdb.Games = "[{\"id\":4,\"name\":\"Metro 2033 Redux\",\"first_release_date\":1408406400,\"cover\":{\"image_id\":\"co2abc\"}}]";
        result = await covers.FetchAsync(collection);
        Check(result.Outcome == CoverArtService.Outcome.NeedsChoice && result.Candidates.Single().ImageId == "co2abc" &&
            games.GetGames().Single(game => game.Id == collection.Id).CoverImage is null && igdb.Queries[^1].Contains("\"Metro 2033\""),
            "An uncertain match changes nothing and returns the candidates");
        igdb.Games = "[]";
        Check((await covers.FetchAsync(legacy)).Outcome == CoverArtService.Outcome.NotFound, "A title IGDB does not know is reported as not found");

        await covers.SetCoverAsync(collection.Id, "co2abc");
        await covers.SetCoverAsync(solo.Id, "co9new");
        Check(!File.Exists(Path.Combine(games.CoversDirectory, stored)) && games.GetGames().Single(game => game.Id == solo.Id).CoverImage == $"{solo.Id:N}-co9new.jpg",
            "Choosing another cover replaces the record and deletes the old picture");
        File.Delete(Path.Combine(games.CoversDirectory, $"{solo.Id:N}-co9new.jpg"));
        var queries = igdb.Queries.Count;
        Check((await covers.FetchAsync(games.GetGames().Single(game => game.Id == solo.Id))).Outcome == CoverArtService.Outcome.Added &&
            File.Exists(Path.Combine(games.CoversDirectory, $"{solo.Id:N}-co9new.jpg")) && igdb.Queries.Count == queries,
            "A recorded cover whose picture has gone is downloaded again without a new lookup");
        Check(await Fails(() => covers.SetCoverAsync(solo.Id, "missing")) && await Fails(() => covers.SetCoverAsync(solo.Id, "../escape")) &&
            await Fails(() => covers.SetCoverAsync(Guid.NewGuid(), "co1x78")) && games.GetGames().Single(game => game.Id == solo.Id).CoverImage == $"{solo.Id:N}-co9new.jpg",
            "A missing picture, an invalid image id or an unknown entry leaves the existing cover in place");

        // Pictures supplied by hand, for games IGDB does not have.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9];
        byte[] webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, 7];
        string? Stored(Game game) => games.GetGames().Single(entry => entry.Id == game.Id).CoverImage;
        var requests = igdb.Requests.Count;
        await covers.SetUploadedCoverAsync(legacy.Id, new MemoryStream(png));
        var uploaded = Stored(legacy)!;
        Check(uploaded.StartsWith($"{legacy.Id:N}.upload-") && uploaded.EndsWith(".png") &&
            File.ReadAllBytes(Path.Combine(games.CoversDirectory, uploaded)).SequenceEqual(png) && igdb.Requests.Count == requests,
            "An uploaded picture is stored beside the database under its real kind, without any outside request");
        await Task.Delay(2);
        await covers.SetUploadedCoverAsync(legacy.Id, new MemoryStream(webp));
        Check(Stored(legacy)!.EndsWith(".webp") && Stored(legacy) != uploaded && !File.Exists(Path.Combine(games.CoversDirectory, uploaded)),
            "A second upload gets a new name and replaces the first picture");
        uploaded = Stored(legacy)!;
        Check(await Fails(() => covers.SetUploadedCoverAsync(legacy.Id, new MemoryStream("<html>not a picture"u8.ToArray()))) &&
            await Fails(() => covers.SetUploadedCoverAsync(legacy.Id, new MemoryStream(new byte[CoverArtService.UploadLimit + 1]))) &&
            await Fails(() => covers.SetUploadedCoverAsync(Guid.NewGuid(), new MemoryStream(jpeg))) &&
            Stored(legacy) == uploaded && Directory.GetFiles(games.CoversDirectory, "*.upload-*").Length == 1,
            "A file that is not a picture, one over the size limit, or an unknown entry is refused and leaves nothing behind");
        File.Delete(Path.Combine(games.CoversDirectory, uploaded));
        requests = igdb.Requests.Count;
        Check((await covers.FetchAsync(games.GetGames().Single(game => game.Id == legacy.Id))).Outcome == CoverArtService.Outcome.UploadMissing &&
            Stored(legacy) == uploaded && igdb.Requests.Count == requests,
            "Fetching leaves an uploaded cover's record alone when its picture is missing, and looks nothing up");

        Check(covers.ClearCover(solo.Id) && games.GetGames().Single(game => game.Id == solo.Id).CoverImage is null &&
            !File.Exists(Path.Combine(games.CoversDirectory, $"{solo.Id:N}-co9new.jpg")), "Clearing a cover removes the record and the picture");
        var collectionFile = Path.Combine(games.CoversDirectory, $"{collection.Id:N}-co2abc.jpg");
        Check(File.Exists(collectionFile) && games.RemoveGame(collection.Id) && !File.Exists(collectionFile), "Removing an entry removes its picture");
        Console.WriteLine("20 cover art checks passed.");
    }

    private static async Task<bool> Fails(Func<Task> action)
    {
        try { await action(); return false; }
        catch (Exception exception) when (exception is CoverArtService.CoverArtException or ArgumentException) { return true; }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
