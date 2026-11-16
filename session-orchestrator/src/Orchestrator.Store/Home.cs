using Orchestrator.Core.Contract;
using Orchestrator.Core.Policy;

namespace Orchestrator.Store;

public static class Home
{
    public const string Solution = "Orchestrator.slnx";

    public static string Root() =>
        Environment.GetEnvironmentVariable("ORCH_HOME") is { Length: > 0 } home ? home : FindUp(AppContext.BaseDirectory) ?? Built() ?? Environment.CurrentDirectory;

    static string? Built() =>
        typeof(Home).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false).OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "OrchHome")?.Value is { } path && File.Exists(Path.Combine(path, Solution)) ? Path.GetFullPath(path) : null;

    public static string StoreRoot() =>
        Environment.GetEnvironmentVariable("ORCH_STORE") is { Length: > 0 } store ? Path.GetFullPath(store) : Path.Combine(Root(), "store");

    public static string Config(string name) => Path.Combine(Root(), "config", name);

    public static string Templates() => Path.Combine(Root(), "templates");

    public static Policy LoadPolicy(string storeRoot)
    {
        var path = Path.Combine(storeRoot, StoreFiles.Policy);
        var source = File.Exists(path) ? path : Config("policy.default.json");
        var policy = Policy.Parse(File.ReadAllText(source), source);
        if (!File.Exists(path)) Files.WriteJson(path, policy);
        return policy;
    }

    public static StoreVersion CheckVersion(string storeRoot, DateTimeOffset now)
    {
        var path = Path.Combine(storeRoot, StoreFiles.Version);
        if (Files.ReadJson<StoreVersion>(path) is { } found)
        {
            if (found.Version > StoreVersion.Current)
                throw new InvalidDataException($"store {storeRoot} has version {found.Version}, newer than this supervisor ({StoreVersion.Current}). Update the orchestrator.");
            return found;
        }
        var version = new StoreVersion(StoreVersion.Current, Core.Leases.Times.Format(now));
        Files.WriteJson(path, version);
        return version;
    }

    static string? FindUp(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Solution))) return dir.FullName;
        return null;
    }
}
