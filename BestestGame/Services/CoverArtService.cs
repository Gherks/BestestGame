using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BestestGame.Models;

namespace BestestGame.Services;

/// <summary>
/// Finds cover art on IGDB and keeps a local copy beside the database. Every outside request is made
/// here, on the server: pages only ever load covers from this application.
/// </summary>
public sealed partial class CoverArtService
{
    public sealed record Candidate(string ImageId, string Name, int? Year);
    public enum Outcome { Added, NeedsChoice, NotFound, UploadMissing }
    public sealed record FetchResult(Outcome Outcome, IReadOnlyList<Candidate> Candidates);

    /// <summary>A failure the Games page can show as it is.</summary>
    public sealed class CoverArtException(string message, Exception? inner = null) : Exception(message, inner);

    public const int UploadLimit = 10 * 1024 * 1024;

    // IGDB allows four requests a second; staying under it avoids rejected lookups.
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(280);

    private readonly IConfiguration _configuration;
    private readonly GameService _games;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private string? _tokenOwner;
    private DateTimeOffset _tokenExpires;
    private DateTimeOffset _lastRequest;

    public CoverArtService(IConfiguration configuration, GameService games, HttpClient? http = null)
    {
        _configuration = configuration;
        _games = games;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    // Read on every use, so credentials saved while the application runs take effect at once.
    private string? ClientId => Setting("Igdb:ClientId");
    private string? ClientSecret => Setting("Igdb:ClientSecret");
    private string TokenUrl => Setting("Igdb:TokenUrl") ?? "https://id.twitch.tv/oauth2/token";
    private string ApiUrl => Setting("Igdb:ApiUrl") ?? "https://api.igdb.com/v4";
    private string ImageUrl => Setting("Igdb:ImageUrl") ?? "https://images.igdb.com/igdb/image/upload";
    private string? Setting(string key) => _configuration[key] is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    public bool IsConfigured => ClientId is not null && ClientSecret is not null;

    /// <summary>What to look up for an entry: a collection is represented by its first included title.</summary>
    public static (string Title, int? Year) Subject(Game game)
    {
        var (title, year) = game.IncludedTitles is { Count: > 0 } included
            ? (included[0].Title, included[0].ReleaseYear ?? game.ReleaseYear)
            : (game.Title, game.ReleaseYear);
        // Stored titles can end in a year or a legacy list of parts, which IGDB's names do not carry.
        var open = title.LastIndexOf('(');
        if (open > 0 && title.TrimEnd().EndsWith(')')) title = title[..open];
        return (title.Trim(), year);
    }

    public bool HasCoverFile(Game game)
        => !string.IsNullOrEmpty(game.CoverImage) && File.Exists(Path.Combine(_games.CoversDirectory, game.CoverImage));

    /// <summary>
    /// Gives an entry a cover when one candidate is clearly right, and otherwise reports the candidates
    /// for a person to choose from. A recorded cover whose file has gone is downloaded again.
    /// </summary>
    public async Task<FetchResult> FetchAsync(Game game, CancellationToken cancellation = default)
    {
        if (StoredImageId(game) is { } recorded)
        {
            await SetCoverAsync(game.Id, recorded, cancellation);
            return new(Outcome.Added, []);
        }
        // An uploaded picture is a person's choice. If its file is not here it cannot be fetched again,
        // and it is not replaced by a lookup.
        if (!string.IsNullOrEmpty(game.CoverImage)) return new(Outcome.UploadMissing, []);
        var (title, year) = Subject(game);
        var candidates = await SearchAsync(title, cancellation);
        if (Confident(candidates, title, year) is not { } match)
            return new(candidates.Count > 0 ? Outcome.NeedsChoice : Outcome.NotFound, candidates);
        await SetCoverAsync(game.Id, match.ImageId, cancellation);
        return new(Outcome.Added, candidates);
    }

    public async Task<IReadOnlyList<Candidate>> SearchAsync(string title, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(title)) return [];
        var query = $"search \"{title.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"; fields name,first_release_date,cover.image_id; where cover != null; limit 12;";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{ApiUrl}/games")
        {
            Content = new StringContent(query, Encoding.UTF8, "text/plain")
        }, cancellation);
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            return document.RootElement.EnumerateArray()
                .Select(game => (Game: game, ImageId: game.TryGetProperty("cover", out var cover) && cover.ValueKind == JsonValueKind.Object &&
                    cover.TryGetProperty("image_id", out var id) ? id.GetString() : null))
                .Where(entry => entry.ImageId is not null && ImageIdPattern().IsMatch(entry.ImageId))
                .Select(entry => new Candidate(entry.ImageId!,
                    entry.Game.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    entry.Game.TryGetProperty("first_release_date", out var date) && date.TryGetInt64(out var seconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(seconds).Year : null))
                .ToList();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new CoverArtException("IGDB sent a reply this application could not read.", exception);
        }
    }

    /// <summary>
    /// The candidate that can be used without asking: its name is the same once case, accents and
    /// punctuation are set aside, and its year agrees when the entry has one.
    /// </summary>
    public static Candidate? Confident(IReadOnlyList<Candidate> candidates, string title, int? year)
    {
        var wanted = Normalize(title);
        var named = candidates.Where(candidate => Normalize(candidate.Name) == wanted).ToList();
        if (year is not { } known) return named.Count == 1 ? named[0] : null;
        // Release years differ between regions and platforms by a year at most.
        return named.Where(candidate => candidate.Year is { } released && Math.Abs(released - known) <= 1)
            .OrderBy(candidate => Math.Abs(candidate.Year!.Value - known)).FirstOrDefault()
            ?? (named.Count == 1 && named[0].Year is null ? named[0] : null);
    }

    private static string Normalize(string text) => string.Concat(text.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
        .Select(char.ToLowerInvariant));

    /// <summary>Downloads the chosen image and records it on the entry in the selected tournament.</summary>
    public async Task SetCoverAsync(Guid gameId, string imageId, CancellationToken cancellation = default)
    {
        var bytes = await ImageAsync(imageId, "t_cover_big_2x", cancellation);
        // The image id is part of the name, so a changed cover is a new address for the browser.
        var file = $"{gameId:N}-{imageId}.jpg";
        Directory.CreateDirectory(_games.CoversDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_games.CoversDirectory, file), bytes, cancellation);
        if (!_games.SetCover(gameId, file))
            throw new CoverArtException("That entry is no longer in the selected tournament.");
    }

    /// <summary>
    /// Stores a picture supplied by hand as an entry's cover. Its kind is read from the file itself, so a
    /// mislabelled or non-picture upload is refused before anything is written.
    /// </summary>
    public async Task SetUploadedCoverAsync(Guid gameId, Stream picture, CancellationToken cancellation = default)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        for (int read; (read = await picture.ReadAsync(chunk, cancellation)) > 0;)
        {
            if (buffer.Length + read > UploadLimit) throw new CoverArtException("That picture is larger than 10 MB.");
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray();
        var extension = PictureExtension(bytes) ?? throw new CoverArtException("That file is not a JPEG, PNG or WebP picture.");
        // The name can never be taken for an IGDB image, and differs for every upload so browsers reload it.
        var file = $"{gameId:N}.upload-{DateTime.UtcNow.Ticks:x}.{extension}";
        Directory.CreateDirectory(_games.CoversDirectory);
        var path = Path.Combine(_games.CoversDirectory, file);
        await File.WriteAllBytesAsync(path, bytes, cancellation);
        if (_games.SetCover(gameId, file)) return;
        File.Delete(path);
        throw new CoverArtException("That entry is no longer in the selected tournament.");
    }

    private static string? PictureExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith<byte>([0xFF, 0xD8, 0xFF])) return "jpg";
        if (bytes.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "png";
        return bytes.Length > 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) ? "webp" : null;
    }

    public bool ClearCover(Guid gameId) => _games.SetCover(gameId, null);

    /// <summary>A small picture of a candidate, relayed so the browser never contacts IGDB itself.</summary>
    public Task<byte[]> PreviewAsync(string imageId, CancellationToken cancellation = default)
        => ImageAsync(imageId, "t_cover_big", cancellation);

    private async Task<byte[]> ImageAsync(string imageId, string size, CancellationToken cancellation)
    {
        if (!ImageIdPattern().IsMatch(imageId)) throw new CoverArtException("That is not an IGDB image.");
        try
        {
            using var response = await _http.GetAsync($"{ImageUrl}/{size}/{imageId}.jpg", cancellation);
            if (!response.IsSuccessStatusCode) throw new CoverArtException($"IGDB could not supply that image ({(int)response.StatusCode}).");
            return await response.Content.ReadAsByteArrayAsync(cancellation);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new CoverArtException("IGDB's image server could not be reached.", exception);
        }
    }

    private static string? StoredImageId(Game game)
        => game.CoverImage is { } file && StoredFilePattern().Match(file) is { Success: true } match ? match.Groups[1].Value : null;

    // One request at a time, spaced out, with the Twitch token obtained and renewed as needed.
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> request, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var token = await TokenAsync(cancellation);
                var wait = _lastRequest + RequestSpacing - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellation);
                using var message = request();
                message.Headers.Add("Client-ID", ClientId);
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _http.SendAsync(message, cancellation);
                _lastRequest = DateTimeOffset.UtcNow;
                if (response.IsSuccessStatusCode) return response;
                var status = response.StatusCode;
                response.Dispose();
                // A token can be revoked before it expires; ask for a new one once.
                if (status == HttpStatusCode.Unauthorized && attempt == 0) { _token = null; continue; }
                throw new CoverArtException(status == HttpStatusCode.TooManyRequests
                    ? "IGDB is receiving requests too quickly. Try again in a moment."
                    : $"IGDB refused the request ({(int)status}).");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new CoverArtException("IGDB could not be reached.", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> TokenAsync(CancellationToken cancellation)
    {
        if (ClientId is not { } id || ClientSecret is not { } secret)
            throw new CoverArtException("Cover art needs IGDB credentials. Add them to igdb.json beside the database.");
        if (_token is not null && _tokenOwner == id && DateTimeOffset.UtcNow < _tokenExpires) return _token;
        using var response = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = id, ["client_secret"] = secret, ["grant_type"] = "client_credentials"
        }), cancellation);
        if (!response.IsSuccessStatusCode)
            throw new CoverArtException($"Twitch did not accept the IGDB credentials ({(int)response.StatusCode}). Check the client ID and secret in igdb.json.");
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            _token = document.RootElement.GetProperty("access_token").GetString() ?? throw new JsonException();
            // Renewed a minute early, so a request never sets out with a token about to lapse.
            _tokenExpires = DateTimeOffset.UtcNow.AddSeconds(document.RootElement.GetProperty("expires_in").GetInt64() - 60);
            _tokenOwner = id;
            return _token;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new CoverArtException("Twitch sent a reply this application could not read.", exception);
        }
    }

    [GeneratedRegex("^[a-z0-9_]{1,40}$")]
    private static partial Regex ImageIdPattern();
    [GeneratedRegex("^[0-9a-f]{32}-([a-z0-9_]{1,40})\\.jpg$")]
    private static partial Regex StoredFilePattern();
}
