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
var provider = ProviderFactory.Create(new ProviderOptions(root, Home.Root(), pluginDir) { OrchDir = Home.OrchDir() });
builder.Services.AddSingleton(provider);
builder.Services.AddSingleton<ITurnRunner>(new ProviderTurns(provider));
builder.Services.AddSingleton<IDocsChecker>(new DocsChecker(provider));
var import = Environment.GetEnvironmentVariable("ORCH_IMPORT") is { Length: > 0 } flag ? flag == "1" : string.Equals(root, Home.DefaultStoreRoot(), StringComparison.OrdinalIgnoreCase);
builder.Services.AddSingleton(services => new SupervisorOptions(4, 3, 2, Home.LoadPolicy(root))
{
    Import = import ? id => new Lifecycle(services.GetRequiredService<FileStore>(), provider).Import(id) : null,
    StateChecker = new StateChecker(provider),
    Provider = provider,
});
builder.Services.AddSingleton<Supervisor>();
builder.Services.AddSingleton<Board>();
builder.Services.AddSingleton(services => new DemoPane(services.GetRequiredService<FileStore>(), provider, port, () => services.GetRequiredService<Supervisor>().Tick()));
builder.Services.AddSingleton(services => new Orchestrator.Store.Remote.StoreService(services.GetRequiredService<FileStore>().Repositories));
builder.Services.AddScoped<PathTrail>();
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
app.MapStoreApi();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
var store = app.Services.GetRequiredService<FileStore>();
var policy = app.Services.GetRequiredService<SupervisorOptions>().Policy;
store.Log($"supervisor started: store {root}, port {port}, levels {string.Join(", ", policy.Levels.Select(l => l.Name))}");
Console.WriteLine($"orch supervisor: store {root}, http://{bind}:{port}/");
app.Run();

public partial class Program;
