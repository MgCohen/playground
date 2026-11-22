using Microsoft.AspNetCore.Components.Server.Circuits;
using NavSpike.Shared;
using Spike.Official.Components;
using Spike.Official.Lab;

var builder = WebApplication.CreateBuilder(args);
// The spike runs as Production: serve the static files of the shared library from the build output.
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSpikeBase();
builder.Services.AddScoped<Activity>();
builder.Services.AddScoped<CircuitHandler, ActivityHandler>();

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
