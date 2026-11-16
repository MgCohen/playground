using Microsoft.Extensions.DependencyInjection;
using PatternEngine.Domains.Shapes;
using PatternEngine.Features.Cases.Domain;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Eval.Domain;
using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.GraderTrust.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Features.Packs.Domain.Shared;
using PatternEngine.Features.Trace.Domain.Shared;
using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Messages;
using PatternEngine.Plugins.Settings;

namespace PatternEngine.App.Host;

/// <summary>
/// The composition root. Each system is a list of named parts: packs, case sources, graders, trust checks, judges.
/// To add a part, register it here; the settings choose which parts run.
/// </summary>
public static class Composition
{
    public static ServiceProvider Build(EngineRoot root, Action<IServiceCollection>? replace = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(root);
        services.AddSingleton(_ => Settings.Load(Path.Combine(root.Dir, EngineRoot.SettingsFile)));

        services.AddSingleton<IDomainPack, ShapesPack>();
        services.AddSingleton<IDomainPack, Domains.Circuits.CircuitsPack>();

        services.AddSingleton<ICaseSource, GeneratorSource>();
        services.AddSingleton<ICaseSource, HandSource>();
        services.AddSingleton<IReadOnlyList<ICaseSource>>(sp => sp.GetServices<ICaseSource>().ToList());

        services.AddSingleton(GraderSet.Default);

        services.AddSingleton<ITrustCheck, FixtureCheck>();
        services.AddSingleton<ITrustCheck, ReferenceCheck>();
        services.AddSingleton<ITrustCheck, MutationCheck>();

        services.AddSingleton<PackRegistry>();
        services.AddSingleton<IJudgeFactory, CliJudgeFactory>();
        services.AddSingleton<EvalRunner>();
        services.AddHandlers(typeof(Composition).Assembly);
        replace?.Invoke(services);
        return services.BuildServiceProvider();
    }
}

/// <summary>The judge from the settings: judge.kind cli runs claude -p with the subscription login.</summary>
public sealed class CliJudgeFactory(PackRegistry packs) : IJudgeFactory
{
    public IJudge Create(PackContext pack, string? model)
    {
        var judge = pack.Settings.Section("judge");
        var kind = judge.Text("kind");
        if (kind != "cli")
        {
            throw new InvalidOperationException($"judge.kind \"{kind}\" cannot run from the command line; use cli");
        }

        var cli = judge.Section("cli");
        var agents = new AgentCatalog([pack.AgentsDir, packs.Root.Path(pack.Settings.Section("paths").Text("agents"))]);
        var fixedModel = cli.Text("model");
        return new CliJudge(agents, new CliJudgeSettings(cli.Text("executable"), string.IsNullOrEmpty(model) ? (fixedModel.Length > 0 ? fixedModel : null) : model, cli.Int("retries"), cli.Int("timeout_seconds")));
    }
}

/// <summary>A person at the console answers the questions of the engine.</summary>
public sealed class ConsoleUser : IUserChannel
{
    public Task<string?> AskAsync(Question question, CancellationToken ct)
    {
        Console.WriteLine($"? {question.Text}");
        Console.Write("> ");
        return Task.FromResult(Console.ReadLine());
    }
}
