using Orchestrator.Core.Contract;
using Orchestrator.Core.Policy;

namespace Orchestrator.Store;

public static class Home
{
    public const string Solution = "Orchestrator.slnx";

    public static string Root() =>
        Environment.GetEnvironmentVariable("ORCH_HOME") is { Length: > 0 } home ? home : FindUp(AppContext.BaseDirectory) ?? FindUp(Environment.CurrentDirectory) ?? Environment.CurrentDirectory;

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

    static string? FindUp(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Solution))) return dir.FullName;
        return null;
    }
}
