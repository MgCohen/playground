using Orchestrator.Store;

namespace Orchestrator.Web;

/// <summary>The input of a new session, from the API and from the new session form.</summary>
public sealed record NewSession(string? Title, string? Cwd, string? Brief, string? Parent)
{
    public string? Goal { get; init; }
    public string? Scope { get; init; }
    public string? DoneWhen { get; init; }
    public List<string>? Skills { get; init; }
    public string? Agent { get; init; }
    public bool? Worktree { get; init; }
    public bool? AllowDefaults { get; init; }
    public bool? Background { get; init; }
    public string? Where { get; init; }

    /// <summary>The work stream (C1). Null: the stream of the parent.</summary>
    public string? Stream { get; init; }

    /// <summary>The session type: a custom agent (C1). Null: none.</summary>
    public string? Type { get; init; }

    /// <summary>The workflow of the session (C1). Null: none.</summary>
    public string? Workflow { get; init; }

    public BriefFields ToBrief() => new()
    {
        Title = Title, Goal = Goal ?? Brief, Scope = Scope, DoneWhen = DoneWhen, Skills = Skills,
        Agent = Agent, Cwd = Cwd, Worktree = Worktree, Parent = Parent, AllowDefaults = AllowDefaults, Background = Background, Where = Where,
        Stream = string.IsNullOrWhiteSpace(Stream) ? null : Stream,
        Type = string.IsNullOrWhiteSpace(Type) ? null : Type,
        Workflow = string.IsNullOrWhiteSpace(Workflow) ? null : Workflow,
    };
}
