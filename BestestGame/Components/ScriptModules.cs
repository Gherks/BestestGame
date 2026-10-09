using Microsoft.Extensions.FileProviders;

namespace BestestGame.Components;

/// <summary>
/// Addresses of the application's own script modules. Each carries the time of the newest change to any
/// of them, so a browser can never pair a page with a copy of a script it stored before that change.
/// The modules share one version because they import each other.
/// </summary>
public static class ScriptModules
{
    /// <summary>The web root, set once at startup. Without it, as in the regression checks, addresses carry no version.</summary>
    public static IFileProvider? Files { get; set; }

    public static string Url(string name)
    {
        var newest = Files?.GetDirectoryContents("js").Where(file => !file.IsDirectory)
            .Select(file => file.LastModified.UtcTicks).DefaultIfEmpty().Max() ?? 0;
        return newest == 0 ? $"./js/{name}" : $"./js/{name}?v={newest:x}";
    }
}
