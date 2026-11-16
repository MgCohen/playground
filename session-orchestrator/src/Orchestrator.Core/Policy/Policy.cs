using Orchestrator.Core.Contract;

namespace Orchestrator.Core.Policy;

public sealed record Level(string Name, string Description, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)] double? TimeoutSeconds, bool CheckDocs);

public static class OnDefault
{
    public const string StackedPr = "stacked-pr";
    public const string Commit = "commit";
    public const string None = "none";
}

/// <summary>
/// The wait times of the delivery ladder: after <see cref="NudgeSeconds"/> with no ack the watchdog nudges the receiver,
/// after <see cref="PeerSeconds"/> more it posts delivery.blocked. Each subscriber sweeps the stream after <see cref="SweepSeconds"/> with no push.
/// </summary>
public sealed record Delivery(double NudgeSeconds = 15, double PeerSeconds = 60, double SweepSeconds = 60)
{
    public static readonly Delivery Default = new();
}

public sealed record Policy(IReadOnlyList<Level> Levels, IReadOnlyList<string> Docs, string OnDefault)
{
    public Delivery Delivery { get; init; } = Delivery.Default;

    public Level LevelOf(string? name) => Levels.FirstOrDefault(level => level.Name == name) ?? Levels[^1];

    public static Policy Parse(string text, string source)
    {
        var raw = StoreJson.Read<RawPolicy>(text) ?? throw new InvalidDataException($"policy {source}: not valid JSON");
        if (raw.Levels is not { Count: > 0 } levels) throw new InvalidDataException($"policy {source}: levels is empty");
        foreach (var level in levels)
        {
            if (string.IsNullOrEmpty(level.Name)) throw new InvalidDataException($"policy {source}: a level has no name");
            if (level.TimeoutSeconds is < 0) throw new InvalidDataException($"policy {source}: level {level.Name} has a bad timeoutSeconds");
        }
        return new Policy(levels, raw.Docs ?? [], raw.OnDefault ?? Core.Policy.OnDefault.StackedPr) { Delivery = raw.Delivery ?? Delivery.Default };
    }

    sealed record RawPolicy(List<Level>? Levels, List<string>? Docs, string? OnDefault, Delivery? Delivery);
}
