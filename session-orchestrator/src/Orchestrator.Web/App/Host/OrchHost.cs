namespace Orchestrator.Web.App.Host;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umpire.Kernels;
using Umpire.Vsa.Composition;

public sealed class OrchHost : AppHost
{
    public static void Add(IServiceCollection services, IConfiguration configuration) =>
        new OrchHost().Compose(services, configuration, new VsaKernel(), new AppTypes(null, typeof(OrchHost).Assembly));
}
