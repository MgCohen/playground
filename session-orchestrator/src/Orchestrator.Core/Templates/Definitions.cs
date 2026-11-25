using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Storage;

namespace Orchestrator.Core.Templates;

/// <summary>
/// One catalog of typed definitions (modes, workflows) on <see cref="LayeredDefinitions"/>: list, get, check, add and
/// remove. A definition is the JSON marker file of its catalog, plus optional render files.
/// </summary>
public sealed class DefinitionCatalog<T>(LayeredDefinitions definitions, Func<string, Definition, IReadOnlyList<string>> check) where T : class
{
    public Catalog Catalog => definitions.Catalog;

    public IReadOnlyList<TemplateInfo> List() => definitions.List();

    /// <summary>The definition that applies, or null. A definition that does not parse gives a <see cref="TemplateException"/>.</summary>
    public T? Get(string name) => definitions.Get(name) is { } d ? Parse(name, d) : null;

    /// <summary>The files of the definition that applies, or null.</summary>
    public Definition? Files(string name) => definitions.Get(name);

    /// <summary>The errors of one definition: the spec schema of the catalog and the rules of the kind.</summary>
    public IReadOnlyList<string> Check(string name, Definition definition) => check(name, definition);

    public TemplateInfo Add(string name, Definition definition, bool replace = false)
    {
        var errors = check(name, definition);
        if (errors.Count > 0) throw new TemplateException($"{name} is not a valid {Catalog.Name[..^1]}: {string.Join("; ", errors)}");
        return definitions.Add(name, definition, replace);
    }

    public void Remove(string name) => definitions.Remove(name);

    T Parse(string name, Definition definition) =>
        StoreJson.Read<T>(definition[Catalog.Marker] ?? throw new TemplateException($"{name} has no {Catalog.Marker}"))
        ?? throw new TemplateException($"{name}/{Catalog.Marker} does not parse");

    /// <summary>The common checks: the name, the marker file against its spec schema, and the "name" field.</summary>
    internal static JsonElement? CheckMarker(Catalog catalog, Json.Schema.JsonSchema schema, string name, Definition definition, List<string> errors)
    {
        if (!Catalog.IsName(name)) errors.Add($"{name}: the name must match ^[a-z][a-z0-9-]{{1,31}}$");
        if (definition[catalog.Marker] is not { } text)
        {
            errors.Add($"{catalog.Marker}: missing");
            return null;
        }
        var root = TemplateCheck.Evaluate(schema, text, catalog.Marker, errors);
        if (root is { } r && r.TryGetProperty("name", out var n) && n.GetString() != name)
            errors.Add($"{catalog.Marker}: name is \"{n.GetString()}\", but the folder is \"{name}\"");
        foreach (var path in definition.Files.Keys.Where(p => p != catalog.Marker && !(p.StartsWith("render/") && p.EndsWith(".md"))))
            errors.Add($"{path}: not a file of a {catalog.Name[..^1]} ({catalog.Marker} and render/*.md only)");
        return root;
    }
}

/// <summary>
/// A session mode (S1): a named preset of the four axes. <c>templates/modes/&lt;name&gt;/mode.json</c>, with an optional
/// <c>render/session.md</c> (the mode text of the settings block). A new mode is a new folder; a new axis value needs code
/// in the reader of that axis (S4).
/// </summary>
public sealed record ModeDefinition(string Name, int Version, string Interaction, string Autonomy, string Route, string Visibility, string Describe)
{
    public static readonly IReadOnlyList<string> Interactions = ["task", "stepwise", "conversation"];
    public static readonly IReadOnlyList<string> Autonomies = ["auto", "ask", "approve"];
    public static readonly IReadOnlyList<string> Routes = ["parent", "user"];
    public static readonly IReadOnlyList<string> Visibilities = ["public", "private"];

    /// <summary>The checks of one mode definition. The spec schema <c>_spec/mode.schema.json</c> holds the axis values.</summary>
    public static IReadOnlyList<string> Check(string name, Definition definition, TemplateSpec spec)
    {
        var errors = new List<string>();
        DefinitionCatalog<ModeDefinition>.CheckMarker(Catalog.Modes, spec.Mode, name, definition, errors);
        return errors;
    }
}

/// <summary>
/// A workflow definition (C1): a graph of steps with one entry step and a definition of done. Loops are allowed, and
/// there is no fixed exit step. <c>templates/workflows/&lt;name&gt;/workflow.json</c>. Workflows are made by configuration;
/// the runner (C2) reads this shape.
/// </summary>
public sealed record WorkflowDefinition(string Name, int Version, string Purpose, string Entry, string Done, IReadOnlyList<WorkflowStep> Steps)
{
    public WorkflowStep? Step(string id) => Steps.FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// The checks of one workflow definition: the spec schema <c>_spec/workflow.schema.json</c>, then the graph rules.
    /// <paramref name="isKind"/> tells if a message kind exists; null skips the template check.
    /// </summary>
    public static IReadOnlyList<string> Check(string name, Definition definition, TemplateSpec spec, Func<string, bool>? isKind)
    {
        var errors = new List<string>();
        if (DefinitionCatalog<WorkflowDefinition>.CheckMarker(Catalog.Workflows, spec.Workflow, name, definition, errors) is null) return errors;
        var workflow = StoreJson.Read<WorkflowDefinition>(definition[Catalog.Workflows.Marker]!)!;
        errors.AddRange(CheckGraph(workflow, isKind));
        return errors;
    }

    static IEnumerable<string> CheckGraph(WorkflowDefinition w, Func<string, bool>? isKind)
    {
        var ids = w.Steps.Select(s => s.Id).ToList();
        foreach (var id in ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key)) yield return $"steps: the id \"{id}\" is not unique";
        if (!ids.Contains(w.Entry)) yield return $"entry: no step \"{w.Entry}\"";
        foreach (var step in w.Steps)
        {
            foreach (var next in step.Next ?? [])
                if (!ids.Contains(next.Step)) yield return $"steps.{step.Id}.next: no step \"{next.Step}\"";
            if (step.Executor == WorkflowStep.Ask && step.Session is not null) yield return $"steps.{step.Id}: an ask step has no session";
            if (step.Executor != WorkflowStep.Agent && step.Session is not null) yield return $"steps.{step.Id}: only an agent step names a session";
            if (step.Executor == WorkflowStep.Tool && string.IsNullOrEmpty(step.ToolName)) yield return $"steps.{step.Id}: a tool step names its tool";
            if (step.Executor != WorkflowStep.Tool && step.ToolName is not null) yield return $"steps.{step.Id}: only a tool step names a tool";
            if (isKind is null) continue;
            foreach (var kind in (step.Templates ?? []).Concat((step.Messages ?? []).Select(m => m.Kind)).Distinct())
                if (!isKind(kind)) yield return $"steps.{step.Id}: no message template \"{kind}\"";
        }
        if (!w.Steps.Any(s => s.Done)) yield return "steps: no step can end the work (\"done\": true)";
        if (ids.Contains(w.Entry))
        {
            var reached = new HashSet<string>();
            var queue = new Queue<string>([w.Entry]);
            while (queue.TryDequeue(out var id))
                if (reached.Add(id) && w.Step(id) is { } s)
                    foreach (var next in s.Next ?? []) queue.Enqueue(next.Step);
            foreach (var id in ids.Distinct().Where(i => !reached.Contains(i))) yield return $"steps.{id}: the entry step does not reach it";
        }
    }
}

/// <summary>
/// One node of a workflow. The conditions (<see cref="When"/>, <see cref="Exit"/>, <see cref="NextStep.When"/>) are
/// text for now: the agent reads them. C2 can make them checks that a tool runs.
/// </summary>
public sealed record WorkflowStep(string Id, string Title, string Executor, string Instructions)
{
    public const string Agent = "agent";
    public const string Ask = "ask";
    public const string Tool = "tool";

    /// <summary>Agent steps only: the session that does the step. "owner", "new", { "session": id } or { "type": name }. Default: owner.</summary>
    public JsonElement? Session { get; init; }

    /// <summary>Tool steps only: the name of the deterministic tool that the runner calls.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tool")]
    public string? ToolName { get; init; }

    /// <summary>The entry condition of the step.</summary>
    public string? When { get; init; }

    public IReadOnlyList<string>? Input { get; init; }

    public IReadOnlyList<string>? Output { get; init; }

    /// <summary>The exit condition of the step.</summary>
    public string? Exit { get; init; }

    /// <summary>The message templates that the step uses.</summary>
    public IReadOnlyList<string>? Templates { get; init; }

    public StepApproval? Approval { get; init; }

    public IReadOnlyList<StepMessage>? Messages { get; init; }

    public IReadOnlyList<NextStep>? Next { get; init; }

    /// <summary>True: this step can end the work. It checks the definition of done of the workflow.</summary>
    public bool Done { get; init; }
}

/// <summary>The approval of a step: who gives it, and the strategy (required, skippable, ignored).</summary>
public sealed record StepApproval(string By, string Strategy);

/// <summary>A message that a step sends: the kind, the receiver and when.</summary>
public sealed record StepMessage(string Kind, string To)
{
    public string? When { get; init; }
}

/// <summary>A next step and the condition to go there. No condition: always valid.</summary>
public sealed record NextStep(string Step)
{
    public string? When { get; init; }
}
