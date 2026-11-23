using NavSpike.Shared;
using Spike.Masa.Components;

var builder = WebApplication.CreateBuilder(args);
// The spike runs as Production: serve the static files of the shared library from the build output.
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMasaBlazor().AddMobileComponents();
builder.Services.AddSpikeBase();

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
