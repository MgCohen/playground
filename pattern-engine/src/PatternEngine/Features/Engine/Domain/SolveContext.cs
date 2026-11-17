using System.Text.Json;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Features.Engine.Domain;

/// <summary>The state of one request: the work graph, the catalog (with proposed patterns), the agent gateway and the user channel.</summary>
public sealed class SolveContext(PackContext pack, EngineSettings settings, AgentGateway agents, IUserChannel user, string workDir)
{
    private readonly object gate = new();

    public PackContext Pack { get; } = pack;

    public EngineSettings Settings { get; } = settings;

    public AgentGateway Agents { get; } = agents;

    public IUserChannel User { get; } = user;

    public string WorkDir { get; } = workDir;

    public string PatternsDir => Path.Combine(WorkDir, "patterns");

    public PatternCatalog Catalog { get; private set; } = pack.Catalog;

    public List<WorkNode> Nodes { get; } = [];

    public List<Question> Questions { get; } = [];

    public List<string> Notes { get; } = [];

    public int Attempts { get; set; }

    /// <summary>The spec and the compose trace, when engine.decider is compose. Null for the status decider.</summary>
    public ComposeRecord? Compose { get; set; }

    /// <summary>The nodes of a joined solution: the root first, then each remainder item. Empty when the output has one pattern.</summary>
    public List<WorkNode> Solution { get; } = [];

    /// <summary>The ids of the composed patterns that a top node can use, lowest level first.</summary>
    public IReadOnlyList<string> TopIds =>
        Catalog.Active.Where(p => p.Kind != PatternKind.Leaf).OrderBy(p => p.Level).Select(p => p.Id).ToList();

    public WorkNode Add(string text, IReadOnlyList<string> allowed, int depth, int? parent)
    {
        lock (gate)
        {
            var node = new WorkNode(Nodes.Count, text, allowed, depth, parent);
            Nodes.Add(node);
            return node;
        }
    }

    public void AddPattern(Pattern pattern)
    {
        lock (gate)
        {
            Catalog = Catalog.With(pattern);
        }
    }

    public void Note(string note)
    {
        lock (gate)
        {
            Notes.Add(note);
        }
    }

    public string PatternText(string id)
    {
        var file = Path.Combine(PatternsDir, $"{id}.md");
        return File.Exists(file) ? File.ReadAllText(file) : string.Empty;
    }

    /// <summary>The candidate rows that an agent sees for one node. Code chooses them; the agent does not read files.</summary>
    public string Candidates(IEnumerable<string> ids)
    {
        var rows = ids.Select(Catalog.Get).OfType<Pattern>().Select(p => new
        {
            id = p.Id,
            kind = p.Kind.ToString().ToLowerInvariant(),
            use_when = p.UseWhen,
            do_not_use_when = p.DoNotUseWhen.Select(d => new { when = d.When, use = d.Use }),
            variants = p.Variants.Select(v => new { id = v.Id, when = v.When }),
            @params = p.Params.ToDictionary(d => d.Key, d => new { values = d.Values, @default = d.Default, required = d.Required }),
            inputs = p.Inputs.Select(x => new { name = x.Name, type = x.Type }),
            outputs = p.Outputs.Select(x => new { name = x.Name, type = x.Type }),
            child_patterns = Catalog.ChildPatterns(p),
        });
        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }
}
