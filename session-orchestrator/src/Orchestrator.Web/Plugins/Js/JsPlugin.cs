namespace Orchestrator.Web.Plugins.Js;

using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Web.Plugins.Js.Contracts;
using Orchestrator.Web.Plugins.Js.Handlers.Commands;
using Orchestrator.Web.Plugins.Js.Infrastructure;
using Umpire.Messages.Handlers;
using Umpire.Vsa.Composition;
using Umpire.Vsa.Handlers.Commands;

public sealed class JsPlugin : Plugin
{
    public override void Configure(IServiceCollection services)
    {
        services.AddSingleton<JsTrace>();
        services.AddSingleton<IJsTrace>(provider => provider.GetRequiredService<JsTrace>());
        services.AddScoped<JsCalls>();
        services.AddPipeline(new PipelineBuilder<JsCommand>()
            .Handlers(typeof(JsCommandHandler<>))
            .HooksOf(typeof(AfterCommandHook<,>))
            .BeforeStep<JsTraceStart>()
            .AfterStep<JsTraceEnd>());
    }
}
