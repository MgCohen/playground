using PatternEngine.Features.Catalog.Domain;
using PatternEngine.Features.Catalog.Domain.Shared;

namespace PatternEngine.Tests.Catalog;

/// <summary>One broken catalog for each validator rule. Each test checks that only the expected rule fires.</summary>
public sealed class CatalogRuleTests
{
    private const string Leaf = """
        ---
        id: part
        kind: leaf
        level: 2
        status: stable
        primitive: rect
        ---
        ## Intent
        A part.
        """;

    private const string Good = """
        ---
        id: thing
        kind: object
        level: 1
        status: stable
        phrase: a thing
        use_when: [The request names a thing.]
        variants:
          - { id: plain, when: Always. }
        parts:
          - { ref: part, count: 1 }
        ---
        ## Intent
        A thing.

        ## Rules
        - Do: one part.
        """;

    [Test]
    public void A_good_catalog_has_no_findings()
    {
        Assert.That(Check(("thing.md", Good), ("part.md", Leaf)), Is.Empty);
    }

    [TestCase("kind: object", "kind: animal", "PE101")]
    [TestCase("level: 1", "level: one", "PE101")]
    [TestCase("status: stable", "status: done", "PE101")]
    [TestCase("id: thing", "id: other", "PE102")]
    [TestCase("## Intent\nA thing.", "## Notes\nA thing.", "PE103")]
    [TestCase("phrase: a thing\n", "", "PE105")]
    [TestCase("## Rules\n- Do: one part.", "", "PE105")]
    [TestCase("  - { id: plain, when: Always. }", "  - { id: plain, when: Always. }\n  - { id: plain, when: Never. }", "PE106")]
    [TestCase("{ ref: part, count: 1 }", "{ ref: nothing, count: 1 }", "PE107")]
    [TestCase("{ ref: part, count: 1 }", "{ ref: part, count: many }", "PE107")]
    [TestCase("{ ref: part, count: 1 }", "{ ref: part, count: 1, only: big }", "PE107")]
    [TestCase("use_when:", "do_not_use_when:\n  - { when: Never., use: ghost }\nuse_when:", "PE108")]
    [TestCase("use_when:", "related: [ghost]\nuse_when:", "PE109")]
    [TestCase("use_when:", "params:\n  color: { part: lid, values: [red] }\nuse_when:", "PE110")]
    [TestCase("use_when:", "params:\n  color: { part: part, required: true, default: red, values: [red] }\nuse_when:", "PE110")]
    [TestCase("use_when:", "inputs:\n  - { name: a, type: bit }\n  - { name: a, type: bit }\nuse_when:", "PE111")]
    [TestCase("use_when:", "inputs:\n  - { name: a }\nuse_when:", "PE111")]
    public void A_broken_pattern_fires_its_rule(string find, string replace, string code)
    {
        var broken = Good.Replace(find, replace, StringComparison.Ordinal);
        var codes = Check(("thing.md", broken), ("part.md", Leaf)).Where(f => f.Severity == Severity.Error).Select(f => f.Code).Distinct();
        Assert.That(codes, Is.EqualTo(new[] { code }));
    }

    [Test]
    public void A_leaf_needs_a_primitive_and_no_parts()
    {
        var broken = Leaf.Replace("primitive: rect", "parts:\n  - { ref: thing, count: 1 }", StringComparison.Ordinal);
        var codes = Check(("thing.md", Good), ("part.md", broken)).Select(f => f.Code).Distinct();
        Assert.That(codes, Does.Contain("PE104"));
    }

    [Test]
    public void A_part_with_a_lower_level_fires_the_part_rule()
    {
        var low = Leaf.Replace("level: 2", "level: 1", StringComparison.Ordinal);
        Assert.That(Check(("thing.md", Good), ("part.md", low)).Select(f => f.Code), Does.Contain("PE107"));
    }

    [Test]
    public void Rules_that_point_at_each_other_give_a_warning()
    {
        var a = Good.Replace("use_when:", "do_not_use_when:\n  - { when: It is big., use: other }\nuse_when:", StringComparison.Ordinal);
        var b = Good.Replace("id: thing", "id: other", StringComparison.Ordinal)
            .Replace("use_when:", "do_not_use_when:\n  - { when: It is small., use: thing }\nuse_when:", StringComparison.Ordinal);
        var findings = Check(("thing.md", a), ("other.md", b), ("part.md", Leaf));
        Assert.That(findings.Single(f => f.Code == "PE112").Severity, Is.EqualTo(Severity.Warning));
    }

    [Test]
    public void A_cycle_in_parts_is_an_error()
    {
        var a = Good.Replace("{ ref: part, count: 1 }", "{ ref: other, count: 1 }", StringComparison.Ordinal);
        var b = Good.Replace("id: thing", "id: other", StringComparison.Ordinal).Replace("level: 1", "level: 2", StringComparison.Ordinal)
            .Replace("{ ref: part, count: 1 }", "{ ref: thing, count: 1 }", StringComparison.Ordinal);
        Assert.That(Check(("thing.md", a), ("other.md", b)).Select(f => f.Code), Does.Contain("PE113"));
    }

    [Test]
    public void Each_rule_has_a_unique_code()
    {
        Assert.That(CatalogRules.All.Select(r => r.Code), Is.Unique);
    }

    [Test]
    public void The_hash_changes_when_a_pattern_changes()
    {
        var one = new PatternCatalog([PatternParser.Parse(Good, "thing.md"), PatternParser.Parse(Leaf, "part.md")]);
        var two = new PatternCatalog([PatternParser.Parse(Good + "\nMore.", "thing.md"), PatternParser.Parse(Leaf, "part.md")]);
        Assert.That(one.Get("part")!.Hash, Is.EqualTo(two.Get("part")!.Hash));
        Assert.That(one.Get("thing")!.Hash, Is.Not.EqualTo(two.Get("thing")!.Hash));
        Assert.That(one.Hash, Is.Not.EqualTo(two.Hash));
    }

    private static IReadOnlyList<Finding> Check(params (string File, string Text)[] files) =>
        CatalogRules.Validate(new PatternCatalog(files.Select(f => PatternParser.Parse(f.Text, f.File))));
}
