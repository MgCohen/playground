using System.Reflection;

namespace Orchestrator.Web.Mobile;

/// <summary>
/// The phone has no repository folder. The templates and the policy are resources of the app.
/// At each start, this class writes them to the data folder of the app and sets ORCH_HOME (see Home.Root).
/// </summary>
public static class HomeFolder
{
    const string Prefix = "home/";

    public static void Write(string dataFolder)
    {
        var home = Path.Combine(dataFolder, "home");
        var assembly = typeof(HomeFolder).Assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            // A Windows build writes the resource names with a backslash.
            var path = name.Replace('\\', '/');
            if (!path.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            var target = Path.Combine(home, path[Prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = assembly.GetManifestResourceStream(name)!;
            using var file = File.Create(target);
            source.CopyTo(file);
        }
        Environment.SetEnvironmentVariable("ORCH_HOME", home);
    }
}
