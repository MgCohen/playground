using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Features.Engine.Domain;

/// <summary>
/// The ask step: code asks the user for each required param that the request does not give. No agent decides this:
/// the pattern declares the input, and the decision has no value for it. The engine does not guess.
/// </summary>
public static class AskStep
{
    public static async Task RunAsync(SolveContext s, WorkNode n, CancellationToken ct)
    {
        if (!s.Settings.Questions.AskMissingRequired || n.Pattern is null || s.Catalog.Get(n.Pattern) is not { } p)
        {
            return;
        }

        foreach (var def in p.Params.Where(d => d.Required && !n.Params.ContainsKey(d.Key)))
        {
            var question = new Question
            {
                Node = n.Id,
                About = QuestionTopics.Input,
                Key = def.Key,
                Text = $"Which {def.Key.Replace('_', ' ')} do you want for the {p.Id}?{(def.Values.Count > 0 ? $" Options: {string.Join(", ", def.Values)}." : string.Empty)}",
                Options = [.. def.Values],
            };
            var answer = await AskAsync(s, question, ct);
            var value = Match(def, answer);
            if (value is null)
            {
                n.Status = NodeStatus.Asking;
                n.Reason = $"needs the input \"{def.Key}\"";
                return;
            }

            n.Params[def.Key] = value;
        }
    }

    /// <summary>Sends one question to the user and records it. After the last round, the question stays open.</summary>
    public static async Task<string?> AskAsync(SolveContext s, Question question, CancellationToken ct)
    {
        int asked;
        lock (s.Questions)
        {
            asked = s.Questions.Count(q => q.Answer is not null);
        }

        var answer = asked < s.Settings.Questions.MaxRounds ? await s.User.AskAsync(question, ct) : null;
        lock (s.Questions)
        {
            s.Questions.Add(question with { Answer = Answers.IsEmpty(answer) ? null : answer!.Trim() });
        }

        return Answers.IsEmpty(answer) ? null : answer!.Trim();
    }

    /// <summary>The value of a param in a free-text answer: an exact option, or the one option that the answer names.</summary>
    public static string? Match(ParamDef def, string? answer)
    {
        if (answer is null)
        {
            return null;
        }

        var a = answer.Trim().ToLowerInvariant();
        if (def.Values.Count == 0)
        {
            return a;
        }

        if (def.Values.Contains(a))
        {
            return a;
        }

        var named = def.Values.Where(v => System.Text.RegularExpressions.Regex.IsMatch(a, $@"\b{System.Text.RegularExpressions.Regex.Escape(v)}\b")).ToList();
        return named.Count == 1 ? named[0] : null;
    }
}
