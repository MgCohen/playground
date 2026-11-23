using Bit.Brouter;
using NavSpike.Shared;
using Spike.Brouter.Components;

var builder = WebApplication.CreateBuilder(args);
// The spike runs as Production: serve the static files of the shared library from the build output.
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSpikeBase();
// View transitions on; our CSS (wwwroot/brouter.css) gives the motion for each data-brouter-nav value.
builder.Services.AddBitBrouterServices(o =>
{
    o.ViewTransitions = true;
    o.ViewTransitionDefaultAnimations = false;
});

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
