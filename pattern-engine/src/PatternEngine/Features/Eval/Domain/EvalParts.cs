using System.Text.RegularExpressions;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Features.Eval.Domain;

/// <summary>
/// The user in an eval. It answers each question from the hidden facts of the case, by key.
/// A question about a fact that the case does not have gets "no preference".
/// </summary>
public sealed class SimulatedUser(Case c) : IUserChannel
{
    public Task<string?> AskAsync(Question question, CancellationToken ct)
    {
        var hidden = c.Hidden ?? [];
        if (hidden.TryGetValue(question.Key, out var value) || hidden.TryGetValue(question.About, out value))
        {
            return Task.FromResult<string?>(value);
        }

        var byOption = question.Options.FirstOrDefault(o => hidden.Values.Contains(o, StringComparer.OrdinalIgnoreCase));
        return Task.FromResult<string?>(byOption ?? Answers.NoPreference);
    }
}

/// <summary>The files of one run in its sandbox folder, before the engine starts.</summary>
public sealed record SandboxSetup(string Dir, string PatternsDir);

/// <summary>
/// An ablation turns one part of the system off, to prove that the evals test that part. Ablations are named in --ablate.
/// </summary>
public interface IAblation
{
    string Name { get; }

    EngineSettings Apply(EngineSettings settings, SandboxSetup sandbox);
}

public sealed class NoNoFit : IAblation
{
    public string Name => "no-nofit";

    public EngineSettings Apply(EngineSettings settings, SandboxSetup sandbox) => settings with { AllowNoFit = false };
}

public sealed class NoEscalation : IAblation
{
    public string Name => "no-escalation";

    public EngineSettings Apply(EngineSettings settings, SandboxSetup sandbox) => settings.WithoutEscalation();
}

public sealed class NoQuestions : IAblation
{
    public string Name => "no-questions";

    public EngineSettings Apply(EngineSettings settings, SandboxSetup sandbox) => settings with { Questions = settings.Questions with { AskMissingRequired = false } };
}

/// <summary>Removes each do_not_use_when block from the pattern files of the sandbox.</summary>
public sealed partial class NoDontUse : IAblation
{
    public string Name => "no-dont-use";

    public EngineSettings Apply(EngineSettings settings, SandboxSetup sandbox)
    {
        foreach (var file in Directory.GetFiles(sandbox.PatternsDir, "*.md"))
        {
            File.WriteAllText(file, Block().Replace(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal), string.Empty));
        }

        return settings;
    }

    [GeneratedRegex(@"do_not_use_when:\n(?:  .*\n)+")]
    private static partial Regex Block();
}

public static class Ablations
{
    public static IReadOnlyList<IAblation> All { get; } = [new NoNoFit(), new NoEscalation(), new NoQuestions(), new NoDontUse()];

    public static IReadOnlyList<IAblation> Named(IEnumerable<string> names) =>
        names.Select(n => All.FirstOrDefault(a => a.Name == n) ?? throw new InvalidOperationException($"no ablation \"{n}\" (ablations: {string.Join(", ", All.Select(a => a.Name))})")).ToList();
}
