using System.Globalization;
using Orchestrator.Core.Events;
using Orchestrator.Store;

namespace Orchestrator.Web;

/// <summary>The group of a card in the filter chips and in the ring of the home page.</summary>
public enum CardGroup { Questions, Reviews, Reports, Blocked }

/// <summary>What a card action does when the user swipes or taps it.</summary>
public enum CardDo
{
    /// <summary>Accept the card: the default, an approve, or an ack.</summary>
    Accept,
    /// <summary>Accept the card with a fixed text (yes).</summary>
    AcceptText,
    /// <summary>Refuse the card with a fixed text (no).</summary>
    RefuseText,
    /// <summary>Open the answer sheet with the keyboard.</summary>
    Type,
    /// <summary>Open the answer sheet with the mic on.</summary>
    Speak,
}

public sealed record CardAction(string Label, string Icon, CardDo Do, string? Text = null);

/// <summary>One card of the pile: an open inbox card, with the actions of its type.</summary>
public sealed record CardView(Card Card, string Type, CardGroup Group, string Label, string Title, CardAction Right, CardAction Left)
{
    public string Id => Card.Envelope.Id;
    public string Session => Card.Session;
    public string SessionTitle => Card.Title;
    public string Kind => Card.Kind;
    public IReadOnlyDictionary<string, object?> Fields => Card.Fields;
    public DateTimeOffset? At => Ui.Time(Card.Envelope);
    public DateTimeOffset? DefaultAt => Card.DefaultAt is { } at ? Orchestrator.Core.Leases.Times.Parse(at) : null;
    public IReadOnlyList<string> Options => Ui.List(Fields.GetValueOrDefault("options"));
    public string? DefaultOption => Fields.GetValueOrDefault("defaultOption") as string;

    /// <summary>The text that the sheet sends goes to the card as a refuse (an answer, a change, or a reply).</summary>
    public string SheetLabel => Type switch
    {
        "review" or "plan" => "Your direction for the change",
        "report" or "blocked" => "Message to the session",
        _ => "Your answer",
    };

    public string SendLabel => Type switch
    {
        "review" or "plan" => "Send change",
        "report" or "blocked" => "Send reply",
        _ => "Send answer",
    };
}

/// <summary>The fixed parts of the UI: card types, kinds, icons and times.</summary>
public static class Ui
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The card type sets the labels and the acts of the right and the left action.</summary>
    public static CardView Classify(Card card)
    {
        var f = card.Fields;
        var title = Text(f.GetValueOrDefault("question")) is { Length: > 0 } q ? q
            : Text(f.GetValueOrDefault("summary")) is { Length: > 0 } s ? s
            : Text(f.GetValueOrDefault("title")) is { Length: > 0 } t ? t
            : card.Envelope.Subject;
        var options = List(f.GetValueOrDefault("options"));
        var def = f.GetValueOrDefault("defaultOption") as string;
        var speak = new CardAction("Speak", "mic", CardDo.Speak);
        var type = new CardAction("Type", "pen", CardDo.Type);

        if (card.Kind == EventKinds.Decision && card.Needs == Needs.Answer)
        {
            if (options.Count == 2 && options.Any(o => o.Equals("yes", StringComparison.OrdinalIgnoreCase)) && options.Any(o => o.Equals("no", StringComparison.OrdinalIgnoreCase)))
            {
                var yes = options.First(o => o.Equals("yes", StringComparison.OrdinalIgnoreCase));
                var no = options.First(o => o.Equals("no", StringComparison.OrdinalIgnoreCase));
                return new(card, "yesno", CardGroup.Questions, "Decision", title, new("Yes", "check", CardDo.AcceptText, yes), new("No", "x", CardDo.RefuseText, no));
            }
            return def is { Length: > 0 }
                ? new(card, "default", CardGroup.Questions, "Decision", title, new("Accept", "check", CardDo.Accept), new("Other", "pen", CardDo.Type))
                : new(card, "opinion", CardGroup.Questions, "Decision · opinion", title, speak, type);
        }
        if (card.Kind == EventKinds.Query)
            return new(card, "question", CardGroup.Questions, "Question", title, speak, type);
        if (card.Needs == Needs.Review)
        {
            var plan = card.Kind == EventKinds.Plan;
            return new(card, plan ? "plan" : "review", CardGroup.Reviews, plan ? "Plan" : "Review · default", title, new("Approve", "check", CardDo.Accept), new("Change", "pen", CardDo.Type));
        }
        var blocked = card.Kind is EventKinds.Blocked or EventKinds.DeliveryBlocked || Text(f.GetValueOrDefault("state")) == "blocked";
        return blocked
            ? new(card, "blocked", CardGroup.Blocked, "Blocked", title, new("Seen", "check", CardDo.Accept), new("Reply", "pen", CardDo.Type))
            : new(card, "report", CardGroup.Reports, "Report", title, new("Got it", "check", CardDo.Accept), new("Reply", "pen", CardDo.Type));
    }

    /// <summary>The icon and the color token of a kind, for the rings of the cards and the stream.</summary>
    public static (string Icon, string Color) Look(string kind, string? state = null) => kind switch
    {
        EventKinds.Decision or EventKinds.Query => ("q", "new"),
        EventKinds.Plan or EventKinds.Review => ("review", "new"),
        EventKinds.Answer => ("pen", "accent"),
        EventKinds.Report when state == "blocked" => ("alert", "failed"),
        EventKinds.Report => ("doc", "done"),
        EventKinds.Blocked or EventKinds.DeliveryBlocked or EventKinds.TurnFail => ("alert", "failed"),
        EventKinds.Brief => ("brief", "muted"),
        EventKinds.Message => ("chat", "accent"),
        EventKinds.Update => ("stream", "muted"),
        _ when kind.StartsWith("pr.") || kind.StartsWith("branch.") || kind.StartsWith("merge.") => ("git", "muted"),
        _ => ("dot", "muted"),
    };

    public static string KindLabel(string kind) => kind switch
    {
        EventKinds.Decision => "Decision",
        EventKinds.Query => "Question",
        EventKinds.DeliveryBlocked => "Blocked",
        _ => kind.Length == 0 ? kind : char.ToUpperInvariant(kind[0]) + kind[1..].Replace('.', ' '),
    };

    public static DateTimeOffset? Time(Event e) => Orchestrator.Core.Leases.Times.Parse(e.At);

    public static string Ago(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } time) return "";
        var age = now - time;
        return age.TotalMinutes < 1 ? "now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min" : age.TotalDays < 1 ? $"{(int)age.TotalHours} h" : $"{(int)age.TotalDays} d";
    }

    public static string Clock(DateTimeOffset? at) => at?.ToLocalTime().ToString("HH:mm", Inv) ?? "";

    /// <summary>The time left as m:ss, or h:mm for one hour and more.</summary>
    public static string Left(TimeSpan left)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}h{left.Minutes:00}" : $"{(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    public static string Text(object? value) => value switch
    {
        null => "",
        string s => s,
        System.Collections.IEnumerable items => string.Join(", ", items.Cast<object?>().Select(Text)),
        _ => Convert.ToString(value, Inv) ?? "",
    };

    public static IReadOnlyList<string> List(object? value) => value switch
    {
        null => [],
        string s when s.Length > 0 => [s],
        string => [],
        System.Collections.IEnumerable items => items.Cast<object?>().Select(Text).Where(t => t.Length > 0).ToList(),
        _ => [Text(value)],
    };


    /// <summary>The label, the color token and the progress of a session row.</summary>
    public static (string Label, string Color, double Progress) Look(SessionNode n)
    {
        var progress = n.Update?.Progress is { } p ? Math.Clamp(p > 1 ? p / 100 : p, 0, 1) : n.Report?.State == "done" ? 1 : .15;
        if (n.Control == Orchestrator.Core.Contract.ControlStates.Archived) return ("archived", "muted", progress);
        if (n.Control == Orchestrator.Core.Contract.ControlStates.Stopped) return ("stopped", "muted", progress);
        if (n.Blocked is not null || n.Report?.State == "blocked") return ("blocked", "failed", progress);
        if (n.Running || n.State == "running") return ("running", "pending", progress);
        if (n.Report?.State == "done") return ("done", "done", 1);
        if (n.State == "new") return ("new", "new", progress);
        return ("idle", "new", progress);
    }

    /// <summary>The second line of a session row: the last update, the report, or the folder.</summary>
    public static string Line(SessionNode n) =>
        n.Blocked is { } b ? b
        : n.Update is { } u ? (u.Step is { Length: > 0 } step ? $"{step}: {u.Summary}" : u.Summary)
        : n.Report is { } r ? r.Summary ?? r.State ?? ""
        : n.Meta.Cwd ?? "";

    public static string Name(SessionNode n) => n.Meta.Title ?? n.Meta.Id[..Math.Min(8, n.Meta.Id.Length)];

    public static IEnumerable<SessionNode> Flatten(IEnumerable<SessionNode> nodes) => nodes.SelectMany(n => Flatten(n.Children).Prepend(n));

    public static string Num(double value) => value.ToString("0.###", Inv);

    /// <summary>Stroke paths of the icons (24 × 24).</summary>
    public static readonly IReadOnlyDictionary<string, string> Icons = new Dictionary<string, string>
    {
        ["home"] = "<path d=\"M5 12H3l9-9 9 9h-2\"/><path d=\"M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-7\"/>",
        ["cards"] = "<rect x=\"5\" y=\"4\" width=\"14\" height=\"16\" rx=\"3\"/><path d=\"M8 2h8\"/>",
        ["stream"] = "<path d=\"M4 7h16M4 12h10M4 17h13\"/>",
        ["tree"] = "<circle cx=\"6\" cy=\"5\" r=\"2\"/><circle cx=\"18\" cy=\"12\" r=\"2\"/><circle cx=\"18\" cy=\"19\" r=\"2\"/><path d=\"M6 7v10a2 2 0 0 0 2 2h8M6 12h10\"/>",
        ["gear"] = "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M12 2v3M12 19v3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M2 12h3M19 12h3M4.9 19.1l2.1-2.1M17 7l2.1-2.1\"/>",
        ["q"] = "<path d=\"M9 9a3 3 0 1 1 4 2.8c-.7.3-1 .9-1 1.7V15M12 18h.01\"/>",
        ["check"] = "<path d=\"M5 12l5 5 9-11\"/>",
        ["x"] = "<path d=\"M18 6L6 18M6 6l12 12\"/>",
        ["pen"] = "<path d=\"M4 20h4L19 9l-4-4L4 16v4z\"/>",
        ["down"] = "<path d=\"M12 5v14M6 13l6 6 6-6\"/>",
        ["alert"] = "<path d=\"M12 4l9 16H3z\"/><path d=\"M12 10v4M12 17h.01\"/>",
        ["doc"] = "<path d=\"M7 3h7l5 5v13H7z\"/><path d=\"M14 3v5h5\"/>",
        ["review"] = "<path d=\"M4 12l4 4 12-12\"/><path d=\"M4 20h16\"/>",
        ["chevL"] = "<path d=\"M15 6l-6 6l6 6\"/>",
        ["chevD"] = "<path d=\"M6 9l6 6l6-6\"/>",
        ["chevR"] = "<path d=\"M9 6l6 6l-6 6\"/>",
        ["mic"] = "<rect x=\"9\" y=\"3\" width=\"6\" height=\"11\" rx=\"3\"/><path d=\"M5 11a7 7 0 0 0 14 0M12 18v3\"/>",
        ["link"] = "<path d=\"M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1\"/><path d=\"M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1\"/>",
        ["git"] = "<circle cx=\"6\" cy=\"6\" r=\"2\"/><circle cx=\"6\" cy=\"18\" r=\"2\"/><circle cx=\"18\" cy=\"8\" r=\"2\"/><path d=\"M6 8v8M18 10c0 4-6 3-10 6\"/>",
        ["search"] = "<circle cx=\"10\" cy=\"10\" r=\"7\"/><path d=\"M21 21l-6-6\"/>",
        ["brief"] = "<path d=\"M4 7h16v12H4z\"/><path d=\"M9 7V5h6v2\"/>",
        ["filter"] = "<path d=\"M4 6h16M7 12h10M10 18h4\"/>",
        ["lock"] = "<rect x=\"5\" y=\"11\" width=\"14\" height=\"9\" rx=\"2\"/><path d=\"M8 11V8a4 4 0 0 1 8 0v3\"/>",
        ["chat"] = "<path d=\"M4 5h16v11H9l-5 4z\"/>",
        ["dot"] = "<circle cx=\"12\" cy=\"12\" r=\"2\"/>",
        ["plus"] = "<path d=\"M12 5v14M5 12h14\"/>",
        ["stop"] = "<rect x=\"6\" y=\"6\" width=\"12\" height=\"12\" rx=\"2\"/>",
        ["play"] = "<path d=\"M8 5v14l11-7z\"/>",
        ["archive"] = "<path d=\"M4 4h16v4H4zM5 8v12h14V8M10 12h4\"/>",
    };
}
