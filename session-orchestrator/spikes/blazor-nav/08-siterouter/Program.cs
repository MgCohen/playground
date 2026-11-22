using Microsoft.AspNetCore.Http.HttpResults;
using NavSpike.Shared;
using Spike.SiteRouter.Components;
using Spike.SiteRouter.Components.Pages;
using Spike.SiteRouter.Nav;

var builder = WebApplication.CreateBuilder(args);
// The spike runs as Production: serve the static files of the shared library from the build output.
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSpikeBase();
// The page table: the only place with URL strings. The Picker has no URL.
builder.Services.AddNav(pages => pages.Map<Home>("/").Map<Counter>("/counter"));

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
// No component has @page, so no endpoint has a route. The fallback renders App for each URL, and SiteHost reads the URL.
app.MapFallback(() => new RazorComponentResult<App>());
app.Run();
