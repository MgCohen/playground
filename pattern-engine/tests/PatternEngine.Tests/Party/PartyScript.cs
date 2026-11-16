using PatternEngine.Domains.Party;
using PatternEngine.Plugins.Agents;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Party;

public static class PartyScript
{
    public static readonly PartyPack Pack = PartyPack.Load(Repo.Path("src", "PatternEngine", "Domains", "Party"), Repo.Path("pe.settings.yaml"));

    /// <summary>
    /// A grader that answers each rubric with the oracle truth. It finds the quest by the longest context in the input,
    /// and the heroes by the lines of the set.
    /// </summary>
    public static object OracleGrader(AgentCall call)
    {
        var quest = Pack.Cases.SelectMany(c => c.Quests).Where(q => call.Input.Contains(q.Context, StringComparison.Ordinal)).MaxBy(q => q.Context.Length)!;
        var set = call.Input.Split("# Set")[1].Split("# Rubrics")[0].Split('\n')
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..l.IndexOf(':', StringComparison.Ordinal)]).ToList();
        var party = set.Select(id => Pack.Heroes.Get(id)!).ToList();
        return new { verdicts = quest.Visible.Select(r => new { rubric = r.Id, verdict = Oracle.Truth(r, party), reason = "oracle" }).ToArray() };
    }
}
