using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;

namespace Orchestrator.Cli;

/// <summary>The Stream API on the command line: init, post, events (query), read, watch (subscribe) and nudge.</summary>
public static class EventCommands
{
    static readonly JsonSerializerOptions Line = new(StoreJson.Options) { WriteIndented = false };
    static readonly HashSet<string> PostOptions = ["to", "from", "thread", "needs", "reply", "of", "via", "json", "session"];

    public static int Init(CommandContext c)
    {
        c.Store.Events.Open();
        Home.CheckVersion(c.Store.Repositories, c.Store.Clock.GetUtcNow());
        var policy = Home.LoadPolicy(c.Store.Repositories);
        c.Output.WriteLine($"Store {c.Store.Root} is ready: version {StoreVersion.Current}, {policy.Levels.Count} policy levels.");
        return 0;
    }

    public static int Post(CommandContext c)
    {
        var a = c.Args;
        var kind = a.Required(1, "kind");
        var body = a.Option("json") is { } json
            ? Core.Templates.Render.Fields(JsonSerializer.Deserialize<JsonElement>(json == "-" ? Console.In.ReadToEnd() : File.ReadAllText(json)))
            : new Dictionary<string, object?>();
        foreach (var (name, value) in a.Options.Where(o => !PostOptions.Contains(o.Key))) body[name] = value;
        var to = Party(c, a.RequiredOption("to"));
        var from = a.Option("from") is { } f ? Party(c, f) : DefaultSender(c);
        var refs = new Dictionary<string, object?> { [RefKeys.Reply] = a.Option("reply"), [RefKeys.Via] = a.Option("via") };
        var e = c.Store.Events.Post(new PostInput(kind, from, to, body)
        {
            Session = a.Option("session") ?? Parties.SessionId(to) ?? Parties.SessionId(from),
            Thread = a.Option("thread"),
            Needs = a.Option("needs"),
            Refs = refs,
        });
        c.Output.WriteLine(e.Id);
        return 0;
    }

    public static int Read(CommandContext c)
    {
        var id = c.Args.Required(1, "id");
        var e = c.Store.Events.Find(id) ?? throw new MessageException($"no envelope {id}");
        if (c.Args.Flag("for") || c.Args.Option("render") is not null)
        {
            c.Output.WriteLine(c.Store.Renderer.Text(e, c.Args.Option("render")));
            return 0;
        }
        c.Output.WriteLine(JsonSerializer.Serialize(new { envelope = e, body = c.Store.Events.Body(e) }, StoreJson.Options));
        return 0;
    }

    public static int Nudge(CommandContext c)
    {
        var to = Party(c, c.Args.Required(1, "party"));
        c.Store.Events.Nudge(to, c.Args.Positional.ElementAtOrDefault(2));
        c.Output.WriteLine($"Nudged {to}.");
        return 0;
    }

    public static int Events(CommandContext c)
    {
        var filter = Filter(c);
        var titles = new Dictionary<string, string>();
        var found = c.Store.Query(filter);
        foreach (var e in found) Print(c, e, titles);
        if (!c.Args.Flag("follow")) return 0;
        var last = found.Select(e => e.Id).Order(Core.Ordering.Names.Order).LastOrDefault();
        using var watcher = c.Store.Repositories.Watch();
        var next = filter with { Since = null, Newest = false, Limit = null, ThreadHeads = false, After = last ?? (c.Store.Clock.GetUtcNow() - TimeSpan.FromSeconds(2)).ToUnixTimeMilliseconds().ToString() };
        var seen = new HashSet<string>();
        while (true)
        {
            watcher.Wait(Core.Storage.StreamSignal.DefaultSweep).GetAwaiter().GetResult();
            foreach (var e in c.Store.Query(next))
                if (seen.Add(e.Id)) Print(c, e, titles);
        }
    }

    /// <summary>
    /// The push of a subscription as one JSON line for each new envelope. For a session it prints the envelopes with no ack,
    /// with the text for the receiver, so the plugin needs no second call. The cursor of the session stays shared.
    /// </summary>
    public static int Watch(CommandContext c)
    {
        var a = c.Args;
        var to = Party(c, a.RequiredOption("to"));
        var kinds = a.List("kind")?.ToHashSet();
        var thread = a.Option("thread");
        var once = a.Flag("once");
        var list = a.Flag("list");
        var timeout = double.TryParse(a.Option("timeout"), out var seconds) ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
        var sweep = TimeSpan.FromSeconds(c.Store.Policy.Delivery.SweepSeconds);
        var end = timeout is { } t ? DateTime.UtcNow + t : (DateTime?)null;
        var printed = new HashSet<string>();
        var start = (c.Store.Clock.GetUtcNow() - TimeSpan.FromSeconds(2)).ToUnixTimeMilliseconds().ToString();
        using var watcher = c.Store.Repositories.Watch();
        while (true)
        {
            var candidates = Parties.SessionId(to) is { } session
                ? c.Store.Undelivered(session)
                : c.Store.Events.After(start).Where(e => e.To == to).ToList();
            foreach (var e in candidates)
            {
                if (kinds is { Count: > 0 } && !kinds.Contains(e.Kind)) continue;
                if (thread is not null && e.Thread != thread) continue;
                if (!printed.Add(e.Id)) continue;
                var text = c.Store.Templates.Has(e.Kind) ? c.Store.Renderer.Text(e, a.Option("render")) : e.Subject;
                c.Output.WriteLine(JsonSerializer.Serialize(new { e.Id, e.Kind, e.From, e.To, e.Thread, e.Needs, e.Subject, e.Refs, text }, Line));
                c.Output.Flush();
                if (once) return 0;
            }
            if (list) return 0;
            var wait = end is { } until ? until - DateTime.UtcNow : sweep;
            if (wait <= TimeSpan.Zero) return 1;
            watcher.Wait(wait < sweep ? wait : sweep).GetAwaiter().GetResult();
        }
    }

    /// <summary>The typed query of the options: the keys of <see cref="StreamQueryText.Keys"/>, for example --view inbox or --needs review.</summary>
    public static StreamQuery Filter(CommandContext c) =>
        StreamQueryText.Parse(key => (c.Args.Options.ContainsKey(key), c.Args.Option(key)), c.Store.Clock.GetUtcNow(), c.Args.Positional.ElementAtOrDefault(1));

    /// <summary>An address: user, supervisor, docs, policy, session:&lt;id&gt;, or the first characters of a session id.</summary>
    public static string Party(CommandContext c, string text)
    {
        if (text is Parties.User or Parties.Supervisor or Parties.Docs or Parties.Policy) return text;
        var id = Parties.SessionId(text) ?? text;
        return Parties.Session(c.Messages.RequireSession(id));
    }

    /// <summary>The sender of a command: the session that runs it, if the store knows it, else the user.</summary>
    public static string DefaultSender(CommandContext c) =>
        c.Provider.CallerSessionId() is { Length: > 0 } id && c.Store.IsEnabled(id)
            ? Parties.Session(id)
            : Parties.User;

    static void Print(CommandContext c, Event e, Dictionary<string, string> titles)
    {
        if (c.Args.Flag("json"))
        {
            c.Output.WriteLine(JsonSerializer.Serialize(e, Line));
            c.Output.Flush();
            return;
        }
        var time = Core.Leases.Times.Parse(e.At)?.ToLocalTime().ToString("HH:mm:ss") ?? e.At;
        c.Output.WriteLine($"{time} {e.Kind,-15} {PartyName(c, e.From, titles)} → {PartyName(c, e.To, titles)}: {e.Subject}");
        if (c.Args.Flag("body") && c.Store.Events.Body(e) is { } body) c.Output.WriteLine($"    {body.GetRawText().ReplaceLineEndings("\n    ")}");
        c.Output.Flush();
    }

    static string PartyName(CommandContext c, string party, Dictionary<string, string> titles)
    {
        if (Parties.SessionId(party) is not { } id) return party;
        if (!titles.TryGetValue(id, out var title)) titles[id] = title = c.Store.Session(id).Title ?? SessionCommands.Short(id);
        return $"\"{title}\"";
    }
}
