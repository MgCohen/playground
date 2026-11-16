using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Grading.Domain.Shared;
using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Features.Grading.Domain;

/// <summary>
/// Checks the questions of a run against the case: each needed question was asked, no question asked for a fact that the case
/// does not hide, and each answer is in the result. A case with no hidden facts and no expected questions passes only with no questions.
/// </summary>
public sealed class QuestionsGrader : IGrader
{
    public string Name => "questions";

    public Verdict Grade(GradeInput input)
    {
        var trace = input.Trace;
        if (trace is null)
        {
            return Verdict.Skip;
        }

        var c = input.Case;
        var asks = c.Expect.Asks ?? [];
        var hidden = c.Hidden ?? [];
        bool Covers(Question q, string name) => q.Key == name || q.About == name;
        var failures = new List<string>();
        foreach (var need in asks.Where(a => !trace.Questions.Any(q => Covers(q, a))))
        {
            failures.Add($"did not ask for \"{need}\"");
        }

        foreach (var q in trace.Questions.Where(q => !asks.Any(a => Covers(q, a)) && !hidden.Keys.Any(h => Covers(q, h))))
        {
            failures.Add($"extra question about \"{q.Key}\" ({q.About}): {q.Text}");
        }

        if (c.Expect.Outcome != Outcomes.Ask)
        {
            foreach (var q in trace.Questions.Where(q => q.Answer is not null && hidden.ContainsKey(q.Key) && q.About == QuestionTopics.Input))
            {
                if (!HasParam(trace.Tree, q.Key, hidden[q.Key]))
                {
                    failures.Add($"the answer \"{q.Key}: {hidden[q.Key]}\" is not in the result");
                }
            }
        }

        return asks.Count == 0 && hidden.Count == 0 && trace.Questions.Count == 0 ? Verdict.Pass : Verdict.From(failures);
    }

    private static bool HasParam(TreeNode? t, string key, string value) =>
        t is not null && ((t.Params?.TryGetValue(key, out var v) == true && string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) || (t.Parts ?? []).Any(p => HasParam(p, key, value)));
}
