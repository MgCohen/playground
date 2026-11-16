using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Eval.Domain;
using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.Report.Domain;
using PatternEngine.Features.Trace.Domain.Shared;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Catalog;

/// <summary>Trial metrics and notifications, from scripted results. No model.</summary>
public sealed class PromotionTests
{
    private const string Trial = """
        ---
        id: hut
        kind: object
        level: 2
        status: trial
        phrase: a hut
        use_when: [The request names a hut.]
        variants:
          - { id: plain, when: Always. }
        parts:
          - { ref: body, count: 1 }
        ---
        ## Intent
        A hut.

        ## Rules
        - Do: one body.
        """;

    private static readonly PromotionSettings Settings = new(4, 0.9, 0.6);

    [Test]
    public void The_status_line_is_not_part_of_the_version()
    {
        Assert.That(PatternParser.Hash(Trial), Is.EqualTo(PatternParser.Hash(Trial.Replace("status: trial", "status: stable", StringComparison.Ordinal))));
        Assert.That(PatternParser.Hash(Trial), Is.Not.EqualTo(PatternParser.Hash(Trial.Replace("A hut.", "A small hut.", StringComparison.Ordinal))));
    }

    [TestCase(3, 3, TrialStates.Collecting)]
    [TestCase(5, 5, TrialStates.ReadyForStable)]
    [TestCase(5, 2, TrialStates.Failing)]
    [TestCase(5, 4, TrialStates.Collecting)]
    public void The_state_comes_from_the_runs_of_the_current_version(int runs, int passes, string state)
    {
        var catalog = new PatternCatalog([PatternParser.Parse(Trial, "hut.md")]);
        var hash = catalog.Get("hut")!.Hash;
        var metric = TrialMetrics.Of(catalog, [Result(runs, passes, hash), Result(10, 0, "old-version")], Settings).Single();
        Assert.That(metric.Runs, Is.EqualTo(runs));
        Assert.That(metric.State, Is.EqualTo(state));
    }

    [Test]
    public void A_threshold_gives_one_notification_for_each_version_and_state()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "notifications.md");
        var metrics = new[] { new TrialMetric("hut", "abc", 5, 5, TrialStates.ReadyForStable), new TrialMetric("tent", "def", 1, 1, TrialStates.Collecting) };
        Assert.That(TrialMetrics.Notify(metrics, file, DateTime.UnixEpoch), Has.Count.EqualTo(1));
        Assert.That(TrialMetrics.Notify(metrics, file, DateTime.UnixEpoch), Is.Empty);
        Assert.That(File.ReadAllText(file), Does.Contain("`hut` (abc): ready for stable"));
    }

    private static EvalResult Result(int runs, int passes, string hash) => new()
    {
        RunId = "r",
        Domain = "shapes",
        Mode = "dev",
        Model = "haiku",
        K = 1,
        Ablate = [],
        Graders = [],
        Catalog = "c",
        Cases = runs,
        Runs = Enumerable.Range(0, runs).Select(i => new RunRecord
        {
            Case = $"c{i}",
            Run = 1,
            Tags = [],
            Request = "r",
            Expect = new Expect { Outcome = Outcomes.Match },
            Trace = new RunTrace { Outcome = Outcomes.Match, Hashes = new Hashes { Catalog = "c", Patterns = new() { ["hut"] = hash } } },
            Grades = new GradeReport(new Dictionary<string, GraderOutcome>(), i < passes, true),
            Pass = i < passes,
            Agent = new RunStats(0, 0, 0, 0, null),
        }).ToList(),
    };
}
