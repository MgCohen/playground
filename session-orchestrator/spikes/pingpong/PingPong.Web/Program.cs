using PingPong.Core;
using PingPong.Ui;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:" + (Environment.GetEnvironmentVariable("PP_WEB_PORT") ?? "7932"));
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(new GameClient(Environment.GetEnvironmentVariable("PP_ENGINE") ?? "http://127.0.0.1:7931/", "web"));
builder.Services.AddScoped<Head>();
var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<PingPong.Web.App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(Routes).Assembly);
app.Run();
