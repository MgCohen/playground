using Orchestrator.Store;
using Orchestrator.Supervisor;
using Orchestrator.Web;

var builder = WebApplication.CreateBuilder(args);
var root = Home.StoreRoot();
var port = int.TryParse(Environment.GetEnvironmentVariable("ORCH_PORT"), out var p) ? p : 7777;
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Services.AddSingleton(new FileStore(root));
builder.Services.AddSingleton<ITurnRunner>(new ClaudeRunner(root));
builder.Services.AddSingleton<IDocsChecker, ClaudeDocsChecker>();
builder.Services.AddSingleton(_ => new SupervisorOptions(4, 3, 2, Home.LoadPolicy(root)));
builder.Services.AddSingleton<Supervisor>();
builder.Services.AddHostedService<TickService>();

var app = builder.Build();
app.MapOrchestratorApi();
var store = app.Services.GetRequiredService<FileStore>();
var policy = app.Services.GetRequiredService<SupervisorOptions>().Policy;
store.Log($"supervisor started: store {root}, port {port}, levels {string.Join(", ", policy.Levels.Select(l => l.Name))}");
Console.WriteLine($"orch supervisor: store {root}, http://127.0.0.1:{port}/api/sessions");
app.Run();

public partial class Program;
