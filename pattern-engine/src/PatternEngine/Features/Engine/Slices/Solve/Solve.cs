using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Messages;

namespace PatternEngine.Features.Engine.Slices.Solve;

public sealed record SolveCommand(string Request, string? Domain, string? Model, IUserChannel User) : ICommand<EngineRunResult>;

/// <summary>Answers one request. The run gets its own folder under results/solve.</summary>
public sealed class SolveHandler(PackRegistry packs, IJudgeFactory judges) : IHandler<SolveCommand, EngineRunResult>
{
    public async Task<Result<EngineRunResult>> HandleAsync(SolveCommand command, CancellationToken ct)
    {
        var pack = packs.Load(command.Domain);
        var dir = Path.Combine(packs.Root.Path(pack.Settings.Section("paths").Text("results")), "solve", DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss", System.Globalization.CultureInfo.InvariantCulture));
        EngineRun.Prepare(pack, dir);
        return await EngineRun.RunAsync(pack, command.Request, dir, EngineSettings.From(pack.Settings), judges.Create(pack, command.Model), command.User, ct);
    }
}
