using Bunit;
using Microsoft.Extensions.Configuration;
using Orchestrator.Web.App.Host;

namespace Orchestrator.Tests;

/// <summary>A bUnit context with the Umpire modules of Orchestrator.Web, as the two hosts compose them (U3).</summary>
public static class UiContext
{
    public static BunitContext New()
    {
        var context = new BunitContext();
        OrchHost.Add(context.Services, new ConfigurationBuilder().Build());
        return context;
    }
}
