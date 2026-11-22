using NavSpike.Shared;
using ReactiveUI;
using ReactiveUI.Builder;
using Spike.ReactiveUI;
using Spike.ReactiveUI.Components;
using Spike.ReactiveUI.Components.Pages;

var builder = WebApplication.CreateBuilder(args);
// The spike runs as Production: serve the static files of the shared library from the build output.
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSpikeBase();
// One screen (with its RoutingState) for each circuit.
builder.Services.AddScoped<AppScreen>();
builder.Services.AddScoped<IScreen>(sp => sp.GetRequiredService<AppScreen>());

// ReactiveUI: the Blazor platform services and the view model to view map of the ViewLocator.
RxAppBuilder.CreateReactiveUIBuilder()
    .WithBlazor()
    .ConfigureViewLocator(locator =>
    {
        locator.Map<HomeViewModel, Home>();
        locator.Map<CounterViewModel, Counter>();
    })
    .BuildApp();

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
