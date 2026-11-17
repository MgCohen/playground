using Orchestrator.Store;
using Orchestrator.Supervisor;
using Orchestrator.Web;
using Orchestrator.Web.Components;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
var root = Home.StoreRoot();
var port = int.TryParse(Environment.GetEnvironmentVariable("ORCH_PORT"), out var p) ? p : 7777;
var bind = Environment.GetEnvironmentVariable("ORCH_BIND") is { Length: > 0 } host ? host : "127.0.0.1";
builder.WebHost.UseUrls($"http://{bind}:{port}");
builder.Services.AddSingleton(new FileStore(root));
var pluginDir = Environment.GetEnvironmentVariable("ORCH_PLUGIN_DIR") is { Length: > 0 } dir ? Path.GetFullPath(dir) : null;
builder.Services.AddSingleton<ITurnRunner>(new ClaudeRunner(root, pluginDir));
builder.Services.AddSingleton<IDocsChecker, ClaudeDocsChecker>();
var import = Environment.GetEnvironmentVariable("ORCH_IMPORT") is { Length: > 0 } flag ? flag == "1" : root == Path.Combine(Home.Root(), "store");
builder.Services.AddSingleton(services => new SupervisorOptions(4, 3, 2, Home.LoadPolicy(root))
{
    Import = import ? id => new Lifecycle(services.GetRequiredService<FileStore>()).Import(id) : null,
    StateChecker = new ClaudeStateChecker(pluginDir),
});
builder.Services.AddSingleton<Supervisor>();
builder.Services.AddSingleton<Board>();
builder.Services.AddSingleton<StreamHub>();
builder.Services.AddSingleton(services => new SignIn(services.GetRequiredService<IConfiguration>()["ORCH_TOKEN"]));
builder.Services.AddHostedService<TickService>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

Home.CheckVersion(root, DateTimeOffset.UtcNow);
var app = builder.Build();
app.UseMiddleware<SignInMiddleware>();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapOrchestratorApi();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
var store = app.Services.GetRequiredService<FileStore>();
var policy = app.Services.GetRequiredService<SupervisorOptions>().Policy;
store.Log($"supervisor started: store {root}, port {port}, levels {string.Join(", ", policy.Levels.Select(l => l.Name))}");
Console.WriteLine($"orch supervisor: store {root}, http://{bind}:{port}/");
app.Run();

public partial class Program;
