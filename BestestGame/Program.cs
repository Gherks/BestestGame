using System.Text.Json;
using BestestGame.Components;
using BestestGame.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// "--export-json <file>" writes everything stored as one file in the format used before SQLite, then exits.
// It can run beside the live application, and is the way back to a release that still reads data.json.
if (builder.Configuration["export-json"] is { Length: > 0 } exportPath)
{
    var source = GameService.DatabasePath(builder.Configuration, builder.Environment);
    if (!File.Exists(source))
    {
        // Run from somewhere else, the settings that name the database are not found.
        Console.Error.WriteLine($"There is no database at {source}. Run this from the application's folder, or set DatabasePath.");
        Environment.ExitCode = 1;
        return;
    }
    var stored = new GameService(builder.Configuration, builder.Environment).Export();
    File.WriteAllText(exportPath, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Exported {stored.Tournaments.Count} tournaments to {Path.GetFullPath(exportPath)}");
    return;
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Cover pictures and the IGDB credentials that fetch them sit beside the database: outside the
// repository and the release folders, and kept across deployments.
var dataDirectory = Path.GetDirectoryName(GameService.DatabasePath(builder.Configuration, builder.Environment))!;
var coversDirectory = Path.Combine(dataDirectory, "covers");
Directory.CreateDirectory(coversDirectory);
builder.Configuration.AddJsonFile(new PhysicalFileProvider(dataDirectory), "igdb.json", optional: true, reloadOnChange: true);

builder.Services.AddSingleton<GameService>();
builder.Services.AddSingleton(services => new CoverArtService(
    services.GetRequiredService<IConfiguration>(), services.GetRequiredService<GameService>()));
builder.Services.AddScoped<TournamentSelectionNotifications>();

var app = builder.Build();
ScriptModules.Files = app.Environment.WebRootFileProvider;

// Open the database now, so one that cannot be opened or moved in from data.json stops the start
// instead of failing on the first page.
app.Services.GetRequiredService<GameService>();
// Closing the last connection folds the write-ahead log back into the database file.
app.Lifetime.ApplicationStopped.Register(SqliteConnection.ClearAllPools);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else if (!OperatingSystem.IsWindows())
{
    ParentProcessWatch.StopWhenOrphaned(app.Lifetime);
}

app.UseHttpsRedirection();

// Scripts and styles keep their file names from one release to the next. Without this the browser guesses
// how long its copy stays good, and can pair a new page with an old script; now it asks each time, and an
// unchanged file costs only a short "not modified" answer.
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = file => file.Context.Response.Headers.CacheControl = "no-cache" });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(coversDirectory), RequestPath = "/covers" });
app.UseAntiforgery();

app.MapGet("/healthz", () => Results.Text(builder.Configuration["DeploymentId"] ?? "development"));

// Candidate pictures shown while choosing a cover are relayed, so the browser never contacts IGDB.
app.MapGet("/covers/preview/{imageId}", async (string imageId, CoverArtService covers, CancellationToken cancellation) =>
{
    try { return Results.File(await covers.PreviewAsync(imageId, cancellation), "image/jpeg"); }
    catch (CoverArtService.CoverArtException) { return Results.NotFound(); }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
