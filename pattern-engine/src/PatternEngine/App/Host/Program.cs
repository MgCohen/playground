using PatternEngine.App.Host;
using PatternEngine.Features.Packs.Domain;

var root = EngineRoot.Find();
using var services = Composition.Build(root);
return await new Cli(services).RunAsync(args);
