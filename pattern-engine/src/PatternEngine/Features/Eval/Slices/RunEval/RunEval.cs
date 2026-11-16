using PatternEngine.Features.Eval.Domain;
using PatternEngine.Features.Report.Domain;
using PatternEngine.Plugins.Messages;

namespace PatternEngine.Features.Eval.Slices.RunEval;

public sealed record RunEvalCommand(string? Domain, EvalOptions Options, Action<string> Log) : ICommand<RunEvalResult>;

public sealed record RunEvalResult(EvalResult Result, string Report, Metrics.TargetCheck Targets);

/// <summary>Runs the eval, writes the result file, and checks the targets of the mode.</summary>
public sealed class RunEvalHandler(EvalRunner runner, Packs.Domain.PackRegistry packs) : IHandler<RunEvalCommand, RunEvalResult>
{
    public async Task<Result<RunEvalResult>> HandleAsync(RunEvalCommand command, CancellationToken ct)
    {
        var domain = command.Domain ?? packs.Settings.Text("default_domain");
        var result = await runner.RunAsync(domain, command.Options, command.Log, ct);
        var settings = packs.Load(domain).Settings;
        return new RunEvalResult(result, Metrics.Report(result), Metrics.CheckTargets(result, settings));
    }
}
