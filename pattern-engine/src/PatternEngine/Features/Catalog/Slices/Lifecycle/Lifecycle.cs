using System.Text.RegularExpressions;
using PatternEngine.Features.Catalog.Domain;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Messages;

namespace PatternEngine.Features.Catalog.Slices.Lifecycle;

public enum LifecycleAction
{
    /// <summary>Copies a proposed pattern from a run into the catalog, with status proposed. It is not in the index yet.</summary>
    Propose,

    /// <summary>A person approves: proposed to trial. The pattern goes into the index and the engine uses it.</summary>
    Approve,

    /// <summary>A person makes the final call: trial to stable.</summary>
    Stable,

    /// <summary>A person makes the final call: the pattern leaves the catalog. The file moves to patterns/removed.</summary>
    Remove,
}

public sealed record LifecycleCommand(LifecycleAction Action, string Target, string? Domain) : ICommand<string>;

/// <summary>The steps of a pattern in its life. Each step is a command that a person runs; code checks the step and the catalog.</summary>
public sealed partial class LifecycleHandler(PackRegistry packs) : IHandler<LifecycleCommand, string>
{
    public Task<Result<string>> HandleAsync(LifecycleCommand command, CancellationToken ct)
    {
        var pack = packs.Load(command.Domain);
        var dir = Path.Combine(pack.Dir, "patterns");
        return Task.FromResult(command.Action switch
        {
            LifecycleAction.Propose => Propose(pack.Catalog, dir, command.Target),
            LifecycleAction.Approve => Move(pack.Catalog, dir, command.Target, PatternStatus.Proposed, PatternStatus.Trial),
            LifecycleAction.Stable => Move(pack.Catalog, dir, command.Target, PatternStatus.Trial, PatternStatus.Stable),
            _ => Remove(pack.Catalog, dir, command.Target),
        });
    }

    private static Result<string> Propose(PatternCatalog catalog, string dir, string file)
    {
        if (!File.Exists(file))
        {
            return Result<string>.Fail("PE201", $"no file {file}");
        }

        var text = Status().Replace(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal), "status: proposed", 1);
        var pattern = PatternParser.Parse(text, Path.GetFileName(file));
        if (catalog.Has(pattern.Id))
        {
            return Result<string>.Fail("PE202", $"the catalog already has \"{pattern.Id}\"");
        }

        var errors = CatalogRules.Validate(catalog.With(pattern)).Where(f => f.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
        {
            return Result<string>.Fail("PE203", $"the proposed pattern has errors: {string.Join("; ", errors)}");
        }

        File.WriteAllText(Path.Combine(dir, $"{pattern.Id}.md"), text);
        return $"{pattern.Id}: proposed. A person approves it with: pe approve {pattern.Id}";
    }

    private static Result<string> Move(PatternCatalog catalog, string dir, string id, PatternStatus from, PatternStatus to)
    {
        var p = catalog.Get(id);
        if (p is null)
        {
            return Result<string>.Fail("PE204", $"no pattern \"{id}\"");
        }

        if (p.Status != from)
        {
            return Result<string>.Fail("PE205", $"\"{id}\" is {Lower(p.Status)}, not {Lower(from)}");
        }

        var file = Path.Combine(dir, p.File);
        File.WriteAllText(file, Status().Replace(File.ReadAllText(file), $"status: {Lower(to)}", 1));
        return $"{id}: {Lower(from)} -> {Lower(to)}";
    }

    private static Result<string> Remove(PatternCatalog catalog, string dir, string id)
    {
        var p = catalog.Get(id);
        if (p is null)
        {
            return Result<string>.Fail("PE204", $"no pattern \"{id}\"");
        }

        var users = catalog.All.Where(x => x.Parts.Any(pt => pt.Ref == id) || x.DoNotUseWhen.Any(d => d.Use == id)).Select(x => x.Id).ToList();
        if (users.Count > 0)
        {
            return Result<string>.Fail("PE206", $"\"{id}\" is used by {string.Join(", ", users)}");
        }

        var removed = Path.Combine(dir, "removed");
        Directory.CreateDirectory(removed);
        File.Move(Path.Combine(dir, p.File), Path.Combine(removed, p.File), true);
        return $"{id}: removed (kept in patterns/removed)";
    }

    private static string Lower(PatternStatus s) => s.ToString().ToLowerInvariant();

    [GeneratedRegex(@"(?m)^status:\s*\w+")]
    private static partial Regex Status();
}
