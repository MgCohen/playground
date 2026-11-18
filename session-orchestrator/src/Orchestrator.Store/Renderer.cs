using Orchestrator.Core.Events;
using Orchestrator.Core.Policy;
using Orchestrator.Core.Templates;

namespace Orchestrator.Store;

/// <summary>
/// The primitive Read with a receiver: the one renderer of the text that a receiver gets for an envelope.
/// The plugin calls it through orch watch or orch read --for, so a live delivery and a supervisor turn get the same text.
/// </summary>
public sealed class Renderer(FileStore store)
{
    public const string Separator = "\n\n---\n\n";

    /// <summary>The text of <paramref name="e"/> for its receiver. <paramref name="name"/> picks another render, for example "tool".</summary>
    public string Text(Event e, string? name = null)
    {
        var templates = store.Templates;
        var data = store.Events.Fields(e);
        if (!templates.Has(e.Kind)) return data.TryGetValue("text", out var raw) && raw is string text ? text : e.Subject;
        var toSession = Parties.SessionId(e.To) is not null;
        var render = name;
        switch (e.Kind)
        {
            case EventKinds.Brief:
                render ??= "session";
                break;
            case EventKinds.Message:
                if (e.From != Parties.User) data["sender"] = SenderName(e.From);
                render ??= "session";
                break;
            case EventKinds.Question or EventKinds.Plan when toSession:
                AddChild(e, data);
                data["decision"] = e.Thread ?? e.Id;
                if (data.TryGetValue("options", out var options) && options is List<object?> list) data["options"] = string.Join("; ", list);
                if (e.Kind == EventKinds.Question)
                    data["defaultText"] = Texts.DefaultText(store.Policy, data.TryGetValue("risk", out var risk) ? risk as string ?? "" : "", data.TryGetValue("defaultOption", out var d) ? d as string : null);
                render ??= "parent";
                break;
            case EventKinds.Report when toSession:
                AddChild(e, data);
                render ??= "parent";
                break;
            case EventKinds.Answer:
                var decision = data.TryGetValue("decision", out var id) ? id as string ?? e.Thread ?? "" : e.Thread ?? "";
                if (!data.ContainsKey("question")) data["question"] = store.Decision(decision)?.Question ?? "question";
                data["note"] = store.Texts.AnswerNote(data.GetValueOrDefault("by") as string, data.GetValueOrDefault("onDefault") as string, decision,
                    data.GetValueOrDefault("reason") as string, data.GetValueOrDefault("source") as string);
                render ??= "session";
                break;
            case EventKinds.Review:
                render ??= data.GetValueOrDefault("target") as string == "plan" ? "plan" : data.GetValueOrDefault("verdict") as string;
                break;
            case EventKinds.Notice:
                render ??= data.GetValueOrDefault("kind") as string;
                break;
            default:
                // An added kind: render/session.md for a session, if the template has it.
                if (toSession && templates[e.Kind].HasRender("session")) render ??= "session";
                break;
        }
        var template = templates[e.Kind];
        if (render is not null && template.HasRender(render)) return templates.Render(e.Kind, data, render);
        return template.HasRender() ? templates.Render(e.Kind, data) : e.Subject;
    }

    /// <summary>The renders of several envelopes, as one turn.</summary>
    public string Join(IEnumerable<Event> envelopes) => string.Join(Separator, envelopes.Select(e => Text(e)));

    void AddChild(Event e, Dictionary<string, object?> data)
    {
        var child = Parties.SessionId(e.From) ?? e.Session ?? e.From;
        var title = store.Meta(child).Title ?? Short(child);
        data["child"] = child;
        data["sender"] = title;
        data.TryAdd("title", title);
    }

    string SenderName(string party) =>
        Parties.SessionId(party) is { } id ? $"session \"{store.Meta(id).Title ?? Short(id)}\" ({id})" : party;

    static string Short(string id) => id.Length > 8 ? id[..8] : id;
}
