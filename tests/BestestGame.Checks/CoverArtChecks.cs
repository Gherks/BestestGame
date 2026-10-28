using System.Net;
using System.Text;
using System.Text.Json;
using BestestGame.Components;
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
        // Pictures of the titles inside a collection.
        Game Collection() => games.GetGames().Single(game => game.Id == collection.Id);
        string Name(string image) => $"{collection.Id:N}-{image}.jpg";
        bool Kept(string image) => File.Exists(Path.Combine(games.CoversDirectory, Name(image)));
        Check(Collection() is { CoverImage: null } pictured && pictured.IncludedTitles[0].CoverImage == Name("co2abc") && pictured.Cover == Name("co2abc"),
            "A cover given to a collection belongs to its first included title and stands for the collection");
        Check(CoverArtService.Slots(solo).Single().Title is null && CoverArtService.Slots(collection).Select(slot => slot.Title).SequenceEqual(new int?[] { 0, 1 }) &&
            CoverArtService.Subject(collection, 1) == ("Metro Exodus", null), "Every included title can hold a picture and is looked up by its own name");
        igdb.Games = "[{\"id\":5,\"name\":\"Metro Exodus\",\"cover\":{\"image_id\":\"co3exo\"}}]";
        result = await covers.FetchAsync(CoverArtService.Slots(Collection())[1]);
        Check(result.Outcome == CoverArtService.Outcome.Added && igdb.Queries[^1].Contains("\"Metro Exodus\"") && Kept("co3exo") &&
            Collection().IncludedTitles[1].CoverImage == Name("co3exo") && Collection().Cover == Name("co2abc"),
            "Fetching gives an included title its own cover and leaves the collection's alone");
        Check(covers.ClearCover(collection.Id, 0) && !Kept("co2abc") && Collection().Cover == Name("co3exo") &&
            !covers.HasCoverFile(Collection()) && covers.HasCoverFile(CoverArtService.Slots(Collection())[1]),
            "A collection whose first title has no cover is shown with the first title that has one");
        await covers.SetCoverAsync(collection.Id, "co3exo", 0);
        Check(covers.ClearCover(collection.Id, 1) && Kept("co3exo") && Collection().IncludedTitles[0].CoverImage == Name("co3exo") && Collection().IncludedTitles[1].CoverImage is null,
            "A picture two titles share stays until the last of them lets go of it");
        Check(!games.SetCover(collection.Id, "x.jpg", 2) && !games.SetCover(collection.Id, "x.jpg", -1) && !games.SetCover(solo.Id, "x.jpg", 0) &&
            await Fails(() => covers.SetCoverAsync(collection.Id, "co2abc", 5)) && !Kept("co2abc"),
            "A title that is not there cannot be given a cover, and nothing is left behind");
        await covers.SetCoverAsync(collection.Id, "co2abc", 1);
        var held = Collection().IncludedTitles;
        games.UpdateGameDetails(collection.Id, null, [held[1], new() { Title = "Metro: Last Light", CoverImage = $"{solo.Id:N}-other.jpg" }, held[0]]);
        Check(Collection().IncludedTitles.Select(title => title.CoverImage).SequenceEqual([Name("co2abc"), null, Name("co3exo")]) && Collection().Cover == Name("co2abc"),
            "Editing a collection keeps each title's picture with it, and a title cannot claim a picture its entry did not hold");
        games.UpdateGameDetails(collection.Id, null, [Collection().IncludedTitles[2]]);
        Check(!Kept("co2abc") && Kept("co3exo") && Collection().IncludedTitles.Single().CoverImage == Name("co3exo"), "The picture of a removed title is deleted");
        games.UpdateGameDetails(collection.Id, 2010, []);
        Check(Collection() is { IncludedTitles.Count: 0 } single && single.CoverImage == Name("co3exo") && Kept("co3exo"),
            "An entry that stops being a collection keeps the picture that stood for it");
        games.UpdateGameDetails(collection.Id, null, collection.IncludedTitles);
        Check(Collection() is { CoverImage: null } again && again.IncludedTitles[0].CoverImage == Name("co3exo") && again.IncludedTitles[1].CoverImage is null,
            "An entry that becomes a collection hands its picture to its first title");
        var trilogy = new Game { Title = "Trilogy", IncludedTitles = [new() { Title = "One", ReleaseYear = 2001 },
            new() { Title = "Two", ReleaseYear = 2002, CoverImage = "two.jpg" }, new() { Title = "Three", ReleaseYear = 2003, CoverImage = "three.jpg" }] };
        var releases = ReleaseRankings.GetEntries([trilogy, new Game { Title = "Alone", ReleaseYear = 2001, CoverImage = "alone.jpg" }])
            .ToDictionary(entry => entry.Title, entry => entry.CoverImage);
        Check(trilogy.Cover == "two.jpg" && releases["One"] == "two.jpg" && releases["Two"] == "two.jpg" && releases["Three"] == "three.jpg" && releases["Alone"] == "alone.jpg",
            "Game of the year shows a release's own picture, and the one that stands for its collection when it has none");
        var copy = JsonSerializer.Deserialize<Game>(JsonSerializer.Serialize(trilogy))!;
        Check(copy.IncludedTitles.Select(title => title.CoverImage).SequenceEqual([null, "two.jpg", "three.jpg"]) &&
            JsonSerializer.Serialize(trilogy.IncludedTitles[0]) == "{\"Title\":\"One\",\"ReleaseYear\":2001}",
            "Exported data carries each title's picture, and a title without one is written as before");

        await covers.SetCoverAsync(collection.Id, "co2abc");
        await covers.SetCoverAsync(collection.Id, "co3exo", 1);
        var collectionFile = Path.Combine(games.CoversDirectory, $"{collection.Id:N}-co2abc.jpg");
        Check(File.Exists(collectionFile) && Kept("co3exo") && games.RemoveGame(collection.Id) && !File.Exists(collectionFile) && !Kept("co3exo"),
            "Removing an entry removes its picture and those of its included titles");
        Console.WriteLine("32 cover art checks passed.");
        await ReleaseYearChecks(games, covers, igdb);
    }

    private static async Task ReleaseYearChecks(GameService games, CoverArtService covers, Igdb igdb)
    {
        CoverArtService.Release[] found = [new("Resident Evil 4", 2005), new("Resident Evil 4", 2023), new("Resident Evil 4: Separate Ways", 2023), new("Celeste", 2018)];
        Check(CoverArtService.ConfidentYear(found, "Celeste") == 2018 && CoverArtService.ConfidentYear([new("Pokémon Red", 1996), new("POKEMON RED", 1996)], "Pokemon Red") == 1996,
            "A year is certain when every game under exactly the title's name came out that year");
        Check(CoverArtService.ConfidentYear(found, "Resident Evil 4") is null && CoverArtService.ConfidentYear(found, "Resident Evil") is null && CoverArtService.ConfidentYear([], "Celeste") is null,
            "A remake under the same name, a different name or no result leaves the year to a person");
        Check(CoverArtService.YearChoices([new("Resident Evil 4: Separate Ways", 2023), .. found], "Resident Evil 4")
                .Select(release => (release.Name, release.Year)).SequenceEqual([("Resident Evil 4", 2005), ("Resident Evil 4", 2023), ("Resident Evil 4: Separate Ways", 2023), ("Celeste", 2018)]),
            "Choices list the exact name first, each name and year once");

        igdb.Games = "[{\"id\":1,\"name\":\"League of Legends\",\"first_release_date\":1256774400},{\"id\":2,\"name\":\"Undated\"}]";
        var releases = await covers.SearchReleasesAsync("League of \"Legends\"");
        Check(releases.SequenceEqual([new CoverArtService.Release("League of Legends", 2009)]) && igdb.Queries[^1].StartsWith("search \"League of \\\"Legends\\\"\";") &&
            igdb.Queries[^1].Contains("where first_release_date != null") && !igdb.Queries[^1].Contains("cover"),
            "A year lookup quotes the title safely, does not need a cover, and keeps only dated results");

        var tournament = games.CreateTournament("Years");
        games.ImportGames([new Game { Title = "League of Legends" }, new Game { Title = "Prey (2017)" }, new Game { Title = "Metro (2033, Exodus)" },
            new Game { Title = "Halo", IncludedTitles = [new() { Title = "Halo 2", ReleaseYear = 2004 }, new() { Title = "Halo 3 (Xbox)" }] },
            new Game { Title = "Dated", ReleaseYear = 2001 }]);
        Game Get(string title) => games.GetGames().Single(game => game.Title == title);
        var gaps = games.GetGames().SelectMany(ReleaseYears.Missing).ToList();
        Check(gaps.Select(gap => (gap.Stored, gap.Subject)).SequenceEqual([("League of Legends", "League of Legends"), ("Prey (2017)", "Prey"), ("Halo 3 (Xbox)", "Halo 3")]) &&
            ReleaseYears.IsLegacyCollection(Get("Metro (2033, Exodus)")) && !ReleaseYears.IsLegacyCollection(Get("Prey (2017)")),
            "Undated single games and included titles need a year; a legacy collection is left out");
        Check(ReleaseYears.WrittenYear("Prey (2017)") == 2017 && ReleaseYears.WrittenYear("Metro (2033, Exodus)") is null && ReleaseYears.WrittenYear("Prey") is null,
            "A year written at the end of a title is read from it");

        var league = Get("League of Legends");
        Check(games.FillReleaseYear(league.Id, null, "League of Legends", 2009) && Get("League of Legends").ReleaseYear == 2009,
            "A single game's year is filled in");
        Check(!games.FillReleaseYear(league.Id, null, "League of Legends", 2010) && Get("League of Legends").ReleaseYear == 2009,
            "A year already recorded is never replaced");
        var halo = Get("Halo");
        Check(!games.FillReleaseYear(halo.Id, 1, "Halo 3", 2007) && !games.FillReleaseYear(halo.Id, null, "Halo", 2001) && !games.FillReleaseYear(halo.Id, 5, "Halo 3 (Xbox)", 2007),
            "A title that has changed, a collection's own year or a title that is not there is left alone");
        Check(games.FillReleaseYear(halo.Id, 1, "Halo 3 (Xbox)", 2007) && Get("Halo").IncludedTitles.Select(title => title.ReleaseYear).SequenceEqual(new int?[] { 2004, 2007 }) &&
            Get("Halo").ReleaseYear is null, "An included title's year is filled in without touching the collection or its other titles");
        var before = CheckData.Snapshot(games);
        try { games.FillReleaseYear(Get("Prey (2017)").Id, null, "Prey (2017)", 0); } catch (ArgumentOutOfRangeException) { }
        Check(CheckData.Snapshot(games) == before && !games.FillReleaseYear(Guid.NewGuid(), null, "League of Legends", 2009),
            "An invalid year or an unknown entry changes nothing");
        games.DeleteTournament(tournament.Id);
        Console.WriteLine("Release year checks passed.");
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
