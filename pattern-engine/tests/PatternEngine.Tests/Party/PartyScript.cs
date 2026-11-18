using PatternEngine.Domains.Party;
using PatternEngine.Plugins.Agents;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Party;

public static class PartyScript
{
    public static readonly PartyPack Pack = PartyPack.Load(Repo.Path("src", "PatternEngine", "Domains", "Party"), Repo.Path("pe.settings.yaml"));

    private static readonly Dictionary<string, Features.Compose.Domain.Shared.Rubric> ByAsk =
        Pack.Cases.SelectMany(c => c.Quests).SelectMany(q => q.Rubrics).GroupBy(r => r.Ask).ToDictionary(g => g.Key, g => g.First());

    /// <summary>
    /// A grader that answers each rubric with the oracle truth. It finds each rubric by its ask (the case files and the
    /// decomposer use the same words), and the heroes by the lines of the set.
    /// </summary>
    public static object OracleGrader(AgentCall call)
    {
        if (call.Input.Contains("# Sets", StringComparison.Ordinal))
        {
            var whole = Pack.Cases.Single(c => c.Many && call.Input.Contains($"## {c.Quests[0].Id}\n", StringComparison.Ordinal)).Anchor;
            var parties = call.Input.Split("# Sets")[1].Split("# Rubrics")[0].Split("\n## ").Skip(1)
                .Select(block => block.Contains("\nnone", StringComparison.Ordinal) ? null : (IReadOnlyList<string>)Heroes(block))
                .ToList();
            return new { verdicts = whole.Visible.Select(r => new { rubric = r.Id, verdict = Oracle.PoolTruth(r, parties), reason = "oracle" }).ToArray() };
        }

        var party = Heroes(call.Input.Split("# Set")[1].Split("# Rubrics")[0]).Select(id => Pack.Heroes.Get(id)!).ToList();
        var rubrics = call.Input.Split("# Rubrics")[1].Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf(": ", StringComparison.Ordinal) + 2)..])
            .Select(ask => ByAsk.TryGetValue(ask, out var r) ? r : FromAsk(ask));
        return new { verdicts = rubrics.Select(r => new { rubric = r.Id, verdict = Oracle.Truth(r, party), reason = "oracle" }).ToArray() };
    }

    /// <summary>A rubric that no case file has, for example a decomposed stat amount. The ask has the words of Decomposer.Anchor.</summary>
    private static Features.Compose.Domain.Shared.Rubric FromAsk(string ask)
    {
        var words = ask.TrimEnd('?').Split(' ');
        var amount = () => int.Parse(words[^3], System.Globalization.CultureInfo.InvariantCulture);
        var anchor = ask.StartsWith("Is the total cost", StringComparison.Ordinal) ? Decomposer.Anchor("x", "x", [], [], amount())
            : ask.StartsWith("Is the total ", StringComparison.Ordinal) ? Decomposer.Anchor("x", "x", [], [(words[3], amount())], null)
            : Decomposer.Anchor("x", "x", [words[^1]], [], null);
        return anchor.Rubrics[0];
    }

    private static List<string> Heroes(string block) =>
        block.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..l.IndexOf(':', StringComparison.Ordinal)]).ToList();
}
