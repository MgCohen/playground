using System.Text.Json;
using System.Text.Json.Nodes;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Providers.Fake;
using Orchestrator.Store;

namespace Orchestrator.Web;

/// <summary>
/// The demo control pane (page <c>/demo</c>): each action writes one event with dummy text into a demo store, through the same
/// store functions as <c>orch new</c>, <c>orch post</c> and <c>orch answer</c>, then starts a tick. The pane works only on a demo
/// store with the fake provider: see <see cref="Refusal"/>.
/// </summary>
public sealed class DemoPane(FileStore store, IProvider provider, int port, Action tick)
{
    public const int RealPort = 7777;

    readonly Random random = new();

    /// <summary>Why the pane does not work here, or null when it works: the fake provider, not the real port, not the real store.</summary>
    public string? Refusal => Refuse(store.Root, port, provider);

    public static string? Refuse(string storeRoot, int port, IProvider provider)
    {
        if (provider is not FakeProvider) return $"The provider is {provider.Name}. The demo pane works only with the fake provider (ORCH_PROVIDER=fake).";
        if (port == RealPort) return $"Port {RealPort} is the port of the real supervisor.";
        var root = Normal(storeRoot);
        foreach (var real in new[] { Home.DefaultStoreRoot(), Home.OldStoreRoot() })
            if (string.Equals(root, Normal(real), StringComparison.OrdinalIgnoreCase)) return $"{storeRoot} is the real store.";
        return null;
    }

    static string Normal(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    /// <summary>The enabled sessions, with their titles, for the session box of the page.</summary>
    public IReadOnlyList<(string Id, string Title)> Sessions() =>
        store.Sessions().Select(id => (id, store.Session(id).Title ?? id[..8])).OrderBy(s => s.Item2, StringComparer.OrdinalIgnoreCase).ToList();

    public string AddSession(string? parent)
    {
        Check();
        var n = random.Next(100, 1000);
        var cwd = parent is { Length: > 0 } p && store.Session(p).Work?.Cwd is { Length: > 0 } c ? c : Folder();
        var brief = new BriefFields { Title = $"Pane session {n}", Goal = Pick(Goals), Cwd = cwd, Parent = parent is { Length: > 0 } ? parent : null };
        var id = new Messages(store).NewSession(brief, "demo pane", brief.Parent is { } q ? Parties.Session(q) : Parties.User);
        return Done($"New session {brief.Title} ({id[..8]}){(brief.Parent is { } x ? $", child of {Name(x)}" : "")}.");
    }

    public string AddQuestion(string? session)
    {
        var id = Target(session);
        var (question, options) = Pick(Questions);
        store.Events.Post(new PostInput(EventKinds.Question, Parties.Session(id), Parties.User, new Dictionary<string, object?>
        {
            ["question"] = question, ["context"] = "Dummy data from the demo pane.", ["options"] = options,
            ["recommendation"] = $"{options[0]}: the first option.", ["risk"] = Pick(Risks), ["defaultOption"] = options[0], ["waitSeconds"] = 0,
        }) { Session = id, Needs = Needs.Answer });
        return Done($"Question from {Name(id)}: {question}");
    }

    /// <summary>Answers the oldest open question as the user.</summary>
    public string AddAnswer()
    {
        Check();
        if (store.OpenDecisions().FirstOrDefault() is not { } decision) return "No open question to answer.";
        var text = Pick(Answers);
        new Messages(store).Answer(decision.Session, decision.Id, text);
        return Done($"Answer to {Name(decision.Session)}: {text}");
    }

    public string AddReport(string? session)
    {
        var id = Target(session);
        var blocked = random.Next(4) == 0;
        var summary = blocked ? "Blocked: the dummy step needs a fact from the user." : Pick(Reports);
        var fields = new Dictionary<string, object?> { ["state"] = blocked ? ReportStates.Blocked : ReportStates.Done, ["summary"] = summary };
        fields[blocked ? "blockers" : "done"] = new[] { blocked ? "A dummy blocker." : "A dummy result." };
        store.Events.Post(new PostInput(EventKinds.Report, Parties.Session(id), Parties.User, fields) { Session = id, Needs = Needs.Ack });
        return Done($"Report ({fields["state"]}) from {Name(id)}: {summary}");
    }

    public string AddUpdate(string? session)
    {
        var id = Target(session);
        var progress = random.Next(1, 10) * 10;
        store.Events.Post(new PostInput(EventKinds.Update, Parties.Session(id), Parties.User, new Dictionary<string, object?>
        {
            ["step"] = $"Step {progress / 20 + 1} of 5", ["progress"] = progress, ["summary"] = Pick(Updates),
        }) { Session = id });
        return Done($"Update from {Name(id)}: {progress} %.");
    }

    /// <summary>A message from the session to its parent, or to the user when it has no parent.</summary>
    public string AddMessage(string? session)
    {
        var id = Target(session);
        var to = store.Session(id).Parent is { Length: > 0 } parent ? Parties.Session(parent) : Parties.User;
        var text = Pick(Texts);
        store.Events.Post(new PostInput(EventKinds.Message, Parties.Session(id), to, new Dictionary<string, object?> { ["text"] = text }) { Session = id });
        return Done($"Message from {Name(id)} to {(to == Parties.User ? "the user" : Name(Parties.SessionId(to)!))}: {text}");
    }

    /// <summary>Turns the failure of each turn of the session on or off in the fake behavior file, then sends the session a message so that a turn runs.</summary>
    public string ToggleFailure(string? session)
    {
        var id = Target(session);
        if (((FakeProvider)provider).BehaviorFile is not { } file) return "The fake provider has no behavior file (ORCH_FAKE). The pane cannot make a turn fail.";
        var behavior = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? [] : [];
        if (behavior["sessions"] is not JsonObject sessions) behavior["sessions"] = sessions = [];
        var on = !sessions.ContainsKey(id);
        if (on) sessions[id] = new JsonObject { ["ok"] = false, ["error"] = "the demo pane made this turn fail" };
        else sessions.Remove(id);
        File.WriteAllText(file, behavior.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        new Messages(store).Send(id, on ? "Demo pane: this turn fails." : "Demo pane: turns work again.");
        return Done($"Turn failures for {Name(id)}: {(on ? "on" : "off")}.");
    }

    /// <summary>True when the turns of the session fail by the behavior file.</summary>
    public bool Fails(string? session) =>
        session is { Length: > 0 } && provider is FakeProvider { BehaviorFile: { } file } && File.Exists(file)
        && JsonNode.Parse(File.ReadAllText(file))?["sessions"]?[session] is not null;

    /// <summary>Random events: sessions, questions, answers, reports, updates and messages.</summary>
    public IReadOnlyList<string> Burst(int count)
    {
        Check();
        Func<string>[] actions = [() => AddSession(null), () => AddQuestion(null), AddAnswer, () => AddReport(null), () => AddUpdate(null), () => AddMessage(null)];
        return Enumerable.Range(0, count).Select(_ => actions[random.Next(actions.Length)]()).ToList();
    }

    void Check()
    {
        if (Refusal is { } reason) throw new MessageException(reason);
    }

    /// <summary>The selected session, else a random enabled session. With no session, the pane makes one.</summary>
    string Target(string? session)
    {
        Check();
        if (session is { Length: > 0 }) return session;
        var all = store.Sessions();
        if (all.Count > 0) return all[random.Next(all.Count)];
        AddSession(null);
        return store.Sessions()[0];
    }

    string Folder()
    {
        var folder = Path.Combine(Path.GetDirectoryName(Normal(store.Root)) ?? store.Root, "work", "pane");
        Directory.CreateDirectory(folder);
        return folder.Replace('\\', '/');
    }

    string Name(string id) => store.Session(id).Title ?? id[..8];

    string Done(string line)
    {
        tick();
        return line;
    }

    T Pick<T>(IReadOnlyList<T> list) => list[random.Next(list.Count)];

    static readonly string[] Goals = ["Write one dummy file and report done.", "Check one dummy fact and send an update.", "Ask one dummy question, then report."];
    static readonly (string, string[])[] Questions =
    [
        ("Which color goes in color.txt?", ["RED", "BLUE"]),
        ("Which fruit name do we use?", ["LIME", "KIWI"]),
        ("May I do the next dummy step?", ["yes", "no"]),
        ("Which number does this session give to the sum?", ["7", "12"]),
    ];
    static readonly string[] Risks = ["low", "medium", "high"];
    static readonly string[] Answers = ["The first option.", "yes", "BLUE", "Use 12, and keep the file short."];
    static readonly string[] Reports = ["The dummy file is written.", "The dummy check passes.", "The dummy step is done."];
    static readonly string[] Updates = ["The dummy build runs.", "Half of the dummy list is done.", "The dummy tests run."];
    static readonly string[] Texts = ["A dummy note for the parent.", "The dummy step took longer than planned.", "A dummy fact: the file has 3 lines."];
}
