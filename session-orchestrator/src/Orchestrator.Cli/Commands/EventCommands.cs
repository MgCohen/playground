using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;

namespace Orchestrator.Cli;

public static class EventCommands
{
    static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(1);

    public static int Events(CommandContext c)
    {
        var a = c.Args;
        var store = c.Store;
        var filter = new EventFilter
        {
            Since = a.Option("since") is { } since ? Durations.Since(since, store.Clock.GetUtcNow()) : null,
            Kinds = a.List("kind")?.ToHashSet(),
            Session = a.Option("session"),
            From = a.Option("from"),
            Open = a.Flag("open"),
            Ledger = a.Flag("ledger"),
        };
        var titles = new Dictionary<string, string>();
        string? last = null;
        foreach (var e in store.Query(filter))
        {
            Print(c, e, titles);
            last = e.Id;
        }
        if (!a.Flag("follow")) return 0;
        var from = filter with { Since = store.Clock.GetUtcNow() - TimeSpan.FromSeconds(2) };
        while (true)
        {
            Thread.Sleep(FollowInterval);
            foreach (var e in store.Query(from))
            {
                if (last is not null && string.CompareOrdinal(e.Id, last) <= 0) continue;
                Print(c, e, titles);
                last = e.Id;
            }
        }
    }

    static void Print(CommandContext c, Event e, Dictionary<string, string> titles)
    {
        if (c.Args.Flag("json"))
        {
            c.Output.WriteLine(JsonSerializer.Serialize(e, new JsonSerializerOptions(StoreJson.Options) { WriteIndented = false }));
            return;
        }
        var time = Core.Leases.Times.Parse(e.At)?.ToLocalTime().ToString("HH:mm:ss") ?? e.At;
        c.Output.WriteLine($"{time} {e.Kind,-15} {Party(c, e.From, titles)} → {Party(c, e.To, titles)}: {e.Subject}");
        if (c.Args.Flag("body") && c.Store.Events.Body(e) is { } body) c.Output.WriteLine($"    {body.GetRawText().ReplaceLineEndings("\n    ")}");
    }

    static string Party(CommandContext c, string party, Dictionary<string, string> titles)
    {
        if (!party.StartsWith("session:")) return party;
        var id = party["session:".Length..];
        if (!titles.TryGetValue(id, out var title)) titles[id] = title = c.Store.Meta(id).Title ?? SessionCommands.Short(id);
        return $"\"{title}\"";
    }
}
