using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;

namespace Orchestrator.Tray;

/// <summary>
/// The files of the tray page: the host page and tray.css of this project, and the styles, scripts and images of Orchestrator.Web.
/// A published tray has them in its wwwroot folder. A build has a static asset manifest
/// (Orchestrator.Tray.staticwebassets.runtime.json) that points at the source folders; then this provider reads them there.
/// </summary>
public static class StaticFiles
{
    public static IFileProvider Provider()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(folder)) return new PhysicalFileProvider(folder);
        var manifest = Path.Combine(AppContext.BaseDirectory, typeof(StaticFiles).Assembly.GetName().Name + ".staticwebassets.runtime.json");
        return File.Exists(manifest) ? new ManifestProvider(manifest) : new NullFileProvider();
    }

    /// <summary>The files of a static asset manifest: a tree of names, each one an asset in a content root or a pattern of a root.</summary>
    sealed class ManifestProvider : IFileProvider
    {
        readonly string[] roots;
        readonly JsonElement tree;

        public ManifestProvider(string manifest)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            roots = [.. json.RootElement.GetProperty("ContentRoots").EnumerateArray().Select(r => r.GetString() ?? "")];
            tree = json.RootElement.GetProperty("Root").Clone();
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            var parts = subpath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var node = tree;
            for (var i = 0; i <= parts.Length; i++)
            {
                // A pattern of a node serves each file below it from the folder of its content root.
                if (Pattern(node, parts[i..]) is { } found) return found;
                if (i == parts.Length) break;
                if (!node.TryGetProperty("Children", out var children) || children.ValueKind != JsonValueKind.Object || !children.TryGetProperty(parts[i], out node))
                    return new NotFoundFileInfo(subpath);
            }
            if (node.TryGetProperty("Asset", out var asset) && asset.ValueKind == JsonValueKind.Object)
            {
                var file = Path.Combine(roots[asset.GetProperty("ContentRootIndex").GetInt32()], asset.GetProperty("SubPath").GetString() ?? "");
                if (File.Exists(file)) return new PhysicalFileInfo(new FileInfo(file));
            }
            return new NotFoundFileInfo(subpath);
        }

        IFileInfo? Pattern(JsonElement node, string[] rest)
        {
            if (rest.Length == 0 || !node.TryGetProperty("Patterns", out var patterns) || patterns.ValueKind != JsonValueKind.Array) return null;
            foreach (var p in patterns.EnumerateArray())
            {
                var file = Path.Combine([roots[p.GetProperty("ContentRootIndex").GetInt32()], .. rest]);
                if (File.Exists(file)) return new PhysicalFileInfo(new FileInfo(file));
            }
            return null;
        }

        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }
}
