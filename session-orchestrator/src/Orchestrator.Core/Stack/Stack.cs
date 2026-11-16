using System.Text.RegularExpressions;

namespace Orchestrator.Core.Stack;

public static class VersionStates
{
    public const string Planned = "planned";
    public const string Started = "started";
    public const string Open = "open";
    public const string Ready = "ready";
    public const string Closed = "closed";
}

public sealed record StackVersion(int Version, string Branch, string Base)
{
    public string? BaseDecision { get; init; }
    public int? Pr { get; init; }
    public string? Url { get; init; }
    public string State { get; init; } = VersionStates.Planned;
    public int? Replaces { get; init; }
}

public sealed record StackEntry(string Decision, string Session)
{
    public List<StackVersion> Versions { get; init; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public StackVersion? Newest => Versions.Count > 0 ? Versions[^1] : null;

    [System.Text.Json.Serialization.JsonIgnore]
    public StackVersion? Current => Versions.LastOrDefault(v => v.State != VersionStates.Planned);

    public StackVersion? Previous(StackVersion version) => Versions.LastOrDefault(v => v.Version < version.Version);

    public StackEntry With(StackVersion version) =>
        this with { Versions = [.. Versions.Where(v => v.Version != version.Version).Append(version).OrderBy(v => v.Version)] };
}

public static partial class StackNames
{
    public const string Prefix = "orch/decision-";

    public static string VersionId(string decision, int version) => version <= 1 ? decision : $"{decision}-v{version}";

    public static string Branch(string decision, int version) => Prefix + VersionId(decision, version);

    public static (string Decision, int Version)? Parse(string branch)
    {
        var match = BranchPattern().Match(branch);
        if (!match.Success) return null;
        return (match.Groups[1].Value, match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1);
    }

    [GeneratedRegex(@"^orch/decision-(.+?)(?:-v(\d+))?$")]
    private static partial Regex BranchPattern();
}
