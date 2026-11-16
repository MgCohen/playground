using PatternEngine.Features.Catalog.Domain.Shared;

namespace PatternEngine.Features.Catalog.Domain;

public enum Severity
{
    Error,
    Warning,
}

public sealed record Finding(string Code, Severity Severity, string Where, string Message)
{
    public override string ToString() => $"{Where}: {Message} ({Code})";
}

/// <summary>One check of the catalog. Each rule is a class, so a rule can be added, removed or tested alone.</summary>
public interface ICatalogRule
{
    string Code { get; }

    IEnumerable<Finding> Check(PatternCatalog catalog);
}

/// <summary>A rule that checks one pattern at a time.</summary>
public abstract class PatternRule : ICatalogRule
{
    public abstract string Code { get; }

    public IEnumerable<Finding> Check(PatternCatalog catalog) =>
        catalog.All.Where(Applies).SelectMany(p => Check(p, catalog));

    protected virtual bool Applies(Pattern p) => p.ParseErrors.Count == 0 || this is ParseRule;

    protected abstract IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog);

    protected Finding Error(Pattern p, string message) => new(Code, Severity.Error, Where(p), message);

    protected Finding Warning(Pattern p, string message) => new(Code, Severity.Warning, p.Id, message);

    private static string Where(Pattern p) => string.IsNullOrEmpty(p.File) ? p.Id : p.File;
}

public sealed class ParseRule : PatternRule
{
    public override string Code => "PE101";

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog) => p.ParseErrors.Select(e => Error(p, e));
}

public sealed class IdRule : PatternRule
{
    public override string Code => "PE102";

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog)
    {
        if (string.IsNullOrEmpty(p.Id))
        {
            yield return Error(p, "no id");
        }
        else if (p.File != $"{p.Id}.md")
        {
            yield return Error(p, $"file name must be {p.Id}.md");
        }

        if (catalog.All.Count(x => x.Id == p.Id) > 1)
        {
            yield return Error(p, $"two files have the id \"{p.Id}\"");
        }
    }
}

public sealed class IntentRule : PatternRule
{
    public override string Code => "PE103";

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog)
    {
        if (string.IsNullOrEmpty(p.Sections.GetValueOrDefault("Intent")))
        {
            yield return Error(p, "needs an \"## Intent\" section");
        }
    }
}

public sealed class LeafRule : PatternRule
{
    public override string Code => "PE104";

    protected override bool Applies(Pattern p) => base.Applies(p) && p.Kind == PatternKind.Leaf;

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog)
    {
        if (string.IsNullOrEmpty(p.Primitive))
        {
            yield return Error(p, "a leaf needs \"primitive\"");
        }

        if (p.Parts.Count > 0)
        {
            yield return Error(p, "a leaf has no parts");
        }
    }
}

public sealed class ComposedRule : PatternRule
{
    public override string Code => "PE105";

    protected override bool Applies(Pattern p) => base.Applies(p) && p.Kind != PatternKind.Leaf;

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog)
    {
        if (p.UseWhen.Count == 0)
        {
            yield return Error(p, "needs \"use_when\"");
        }

        if (string.IsNullOrEmpty(p.Phrase))
        {
            yield return Error(p, "needs \"phrase\" (used by the case generator)");
        }

        if (p.Parts.Count == 0)
        {
            yield return Error(p, "needs \"parts\"");
        }

        if (string.IsNullOrEmpty(p.Sections.GetValueOrDefault("Rules")))
        {
            yield return Error(p, "needs a \"## Rules\" section");
        }
    }
}

public sealed class VariantRule : PatternRule
{
    public override string Code => "PE106";

    protected override bool Applies(Pattern p) => base.Applies(p) && p.Kind != PatternKind.Leaf;

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog)
    {
        var ids = p.Variants.Select(v => v.Id).ToList();
        if (ids.Count == 0)
        {
            yield return Error(p, "needs at least one variant");
        }

        if (ids.Distinct().Count() != ids.Count)
        {
            yield return Error(p, "variant ids must be unique");
        }
    }
}

public sealed class PartRule : PatternRule
{
    public override string Code => "PE107";

    protected override bool Applies(Pattern p) => base.Applies(p) && p.Kind != PatternKind.Leaf;

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog)
    {
        var ids = p.VariantIds.ToList();
        foreach (var part in p.Parts)
        {
            var target = catalog.Get(part.Ref);
            if (target is null)
            {
                yield return Error(p, $"part \"{part.Ref}\" does not exist");
                continue;
            }

            if (target.Level <= p.Level)
            {
                yield return Error(p, $"part \"{part.Ref}\" must have a higher level than {p.Level}");
            }

            if (part.Only is not null && !ids.Contains(part.Only))
            {
                yield return Error(p, $"part \"{part.Ref}\" uses unknown variant \"{part.Only}\"");
            }

            if (!PartCount.TryParse(part.Count, out _))
            {
                yield return Error(p, $"part \"{part.Ref}\": bad count \"{part.Count}\"");
            }
        }
    }
}

public sealed class InsteadRuleCheck : PatternRule
{
    public override string Code => "PE108";

    protected override bool Applies(Pattern p) => base.Applies(p) && p.Kind != PatternKind.Leaf;

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog)
    {
        foreach (var rule in p.DoNotUseWhen)
        {
            if (string.IsNullOrEmpty(rule.When) || string.IsNullOrEmpty(rule.Use))
            {
                yield return Error(p, "each do_not_use_when entry needs \"when\" and \"use\"");
            }
            else if (rule.Use != InsteadRule.NoFit && !catalog.Has(rule.Use))
            {
                yield return Error(p, $"do_not_use_when points to missing \"{rule.Use}\"");
            }
        }
    }
}

public sealed class RelatedRule : PatternRule
{
    public override string Code => "PE109";

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog)
    {
        foreach (var id in p.Related)
        {
            var other = catalog.Get(id);
            if (other is null)
            {
                yield return Error(p, $"related \"{id}\" does not exist");
            }
            else if (!other.Related.Contains(p.Id))
            {
                yield return Warning(p, $"related \"{id}\" does not link back");
            }
        }
    }
}

public sealed class ParamRule : PatternRule
{
    public override string Code => "PE110";

    protected override bool Applies(Pattern p) => base.Applies(p) && p.Kind != PatternKind.Leaf;

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog)
    {
        foreach (var param in p.Params)
        {
            if (param.Part is not null && !p.Parts.Any(pt => pt.Ref == param.Part))
            {
                yield return Error(p, $"param \"{param.Key}\" points to a part that is not in parts");
            }

            if (param.Required && param.Default is not null)
            {
                yield return Error(p, $"param \"{param.Key}\" is required, so it cannot have a default");
            }
        }
    }
}

public sealed class PortRule : PatternRule
{
    public override string Code => "PE111";

    protected override IEnumerable<Finding> Check(Pattern p, PatternCatalog catalog)
    {
        foreach (var (side, ports) in new[] { ("input", p.Inputs), ("output", p.Outputs) })
        {
            foreach (var port in ports.Where(x => string.IsNullOrEmpty(x.Name) || string.IsNullOrEmpty(x.Type)))
            {
                yield return Error(p, $"each {side} needs \"name\" and \"type\"");
            }

            foreach (var name in ports.GroupBy(x => x.Name).Where(g => g.Count() > 1).Select(g => g.Key))
            {
                yield return Error(p, $"{side} \"{name}\" is listed two times");
            }
        }
    }
}

/// <summary>Two do_not_use_when rules that point at each other: a decider can loop between them.</summary>
public sealed class RulePairRule : ICatalogRule
{
    public string Code => "PE112";

    public IEnumerable<Finding> Check(PatternCatalog catalog)
    {
        foreach (var p in catalog.All)
        {
            foreach (var rule in p.DoNotUseWhen)
            {
                var back = catalog.Get(rule.Use)?.DoNotUseWhen.Any(x => x.Use == p.Id) ?? false;
                if (back && string.CompareOrdinal(p.Id, rule.Use) < 0)
                {
                    yield return new Finding(Code, Severity.Warning, $"{p.Id} and {rule.Use}", "do_not_use_when rules point at each other. Make sure that the two conditions cannot both be true.");
                }
            }
        }
    }
}

public sealed class CycleRule : ICatalogRule
{
    public string Code => "PE113";

    public IEnumerable<Finding> Check(PatternCatalog catalog)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var findings = new List<Finding>();

        void Visit(string id, List<string> trail)
        {
            var s = state.GetValueOrDefault(id);
            if (s == 2)
            {
                return;
            }

            if (s == 1)
            {
                findings.Add(new Finding(Code, Severity.Error, "parts", $"cycle in parts: {string.Join(" -> ", trail.Append(id))}"));
                return;
            }

            state[id] = 1;
            foreach (var part in catalog.Get(id)?.Parts ?? [])
            {
                if (catalog.Has(part.Ref))
                {
                    Visit(part.Ref, [.. trail, id]);
                }
            }

            state[id] = 2;
        }

        foreach (var p in catalog.All)
        {
            Visit(p.Id, []);
        }

        return findings;
    }
}

public static class CatalogRules
{
    public static IReadOnlyList<ICatalogRule> All { get; } =
    [
        new ParseRule(), new IdRule(), new IntentRule(), new LeafRule(), new ComposedRule(), new VariantRule(), new PartRule(),
        new InsteadRuleCheck(), new RelatedRule(), new ParamRule(), new PortRule(), new RulePairRule(), new CycleRule(),
    ];

    public static IReadOnlyList<Finding> Validate(PatternCatalog catalog, IEnumerable<ICatalogRule>? rules = null) =>
        (rules ?? All).SelectMany(r => r.Check(catalog)).ToList();
}
