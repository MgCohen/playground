using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;

var home = Home.Root();
var work = Directory.CreateTempSubdirectory("orch-e2e-").FullName;
var port = args.Length > 0 ? int.Parse(args[0]) : 7790;
var log = new List<(string Check, bool Ok, string Detail)>();
Console.WriteLine($"e2e folder: {work}");

var published = Path.Combine(work, "sup");
Run(home, "dotnet", "publish", "src/Orchestrator.Web", "-o", published, "-v", "q");
var repo = Path.Combine(work, "repo");
Directory.CreateDirectory(repo);
Run(repo, "git", "init", "-q", "-b", "main");
File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "# E2E\n\nRules: keep each file to one line.\n");
Run(repo, "git", "add", ".");
Run(repo, "git", "-c", "user.email=e2e@test", "-c", "user.name=e2e", "commit", "-q", "-m", "init");
var storeRoot = Path.Combine(work, "store");
Directory.CreateDirectory(storeRoot);
File.Copy(Path.Combine(home, "config", "policy.test.json"), Path.Combine(storeRoot, "policy.json"));
var store = new FileStore(storeRoot, writer: "e2e");
using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };

var supervisor = Start();
var brief = new
{
    title = "E2E root",
    cwd = repo.Replace('\\', '/'),
    goal = "Step 1: ask the user which color goes in color.txt (options RED and BLUE, risk high, no default option). " +
           "Step 2, after the answer: write the color into color.txt and commit it. Then ask which size goes in size.txt " +
           "(options SMALL and LARGE, risk medium, default LARGE). Follow the steps of the answer, write the size into size.txt and commit it.",
    doneWhen = "color.txt and size.txt are committed, and a done report names both values.",
    skills = new[] { "orch-ask", "orch-stack", "orch-report" },
    agent = "worker",
};
var created = await (await http.PostAsJsonAsync("/api/sessions", brief)).Content.ReadFromJsonAsync<JsonElement>();
var id = created.GetProperty("id").GetString()!;
Console.WriteLine($"session {id}");

var first = await WaitFor("the first decision", TimeSpan.FromMinutes(4), () => store.OpenDecisions(id).FirstOrDefault(d => d.Risk == "high"));
Stop(supervisor, "crash while the session waits for the user");
new Messages(store).Answer(id, first.Id, "GREEN");
Console.WriteLine("answered GREEN while the supervisor was down");
supervisor = Start();

await WaitFor("the turn with the answer", TimeSpan.FromMinutes(2), () => store.Events.All().LastOrDefault(e => e.Kind == EventKinds.TurnStart && e.Session == id && Refs(e, "answers").Contains(first.Id)));
await Task.Delay(TimeSpan.FromSeconds(4));
Stop(supervisor, "crash in the middle of a turn");
await Task.Delay(TimeSpan.FromSeconds(10));
supervisor = Start();

await WaitFor("the done report", TimeSpan.FromMinutes(8), () => store.Report(id) is { State: ReportStates.Done } r ? r : null);
await WaitFor("the end of the last turn", TimeSpan.FromMinutes(2), () => store.ReadJson<TurnMarker>(id, StoreFiles.Turn) is { Ended: not null } m ? m : null);
Stop(supervisor, "end of the test");

var events = store.Events.All().Where(e => e.Session == id).ToList();
var decisions = store.Decisions(id);
Check("two decisions", decisions.Count == 2, string.Join(", ", decisions.Select(d => $"{d.Risk} {d.Question}")));
Check("each decision has an answer", decisions.All(d => store.IsAnswered(id, d.Id)), "");
Check("each decision was delivered", decisions.All(d => store.IsDelivered(id, d.Id)), "");
var deliveries = events.Count(e => e.Kind == EventKinds.SessionInput && e.Subject.StartsWith($"Answer to decision {first.Id}"));
Check("the answer GREEN reached the session once", deliveries == 1, $"{deliveries} deliveries");
Check("the default of the medium decision applied", decisions.Any(d => store.Answer(id, d.Id)?.By == Parties.Policy), "");
var starts = events.Where(e => e.Kind == EventKinds.TurnStart).Select(e => e.Id).ToList();
var ends = events.Where(e => e.Kind is EventKinds.TurnEnd or EventKinds.TurnFail).Select(e => Ref(e, "turnStart")).ToHashSet();
Check("each turn start has an end", starts.All(ends.Contains), $"{starts.Count} turns, {starts.Count(s => !ends.Contains(s))} with no end");
Check("the report names GREEN", store.Report(id)?.Summary.Contains("GREEN", StringComparison.OrdinalIgnoreCase) == true, store.Report(id)?.Summary ?? "");
Check("no open decision", store.OpenDecisions(id).Count == 0, "");

Console.WriteLine();
foreach (var (check, ok, detail) in log) Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {check}{(detail.Length > 0 ? $"  ({detail})" : "")}");
Console.WriteLine($"store: {storeRoot}");
return log.All(l => l.Ok) ? 0 : 1;

Process Start()
{
    var info = new ProcessStartInfo("dotnet", [Path.Combine(published, "Orchestrator.Web.dll")])
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    info.Environment["ORCH_STORE"] = storeRoot;
    info.Environment["ORCH_PORT"] = port.ToString();
    var process = Process.Start(info)!;
    process.OutputDataReceived += (_, _) => { };
    process.ErrorDataReceived += (_, _) => { };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    for (var i = 0; i < 60; i++)
    {
        try
        {
            if (http.GetAsync("/api/sessions").Result.IsSuccessStatusCode) break;
        }
        catch (Exception e) when (e is HttpRequestException or AggregateException or TaskCanceledException)
        {
        }
        Thread.Sleep(500);
    }
    Console.WriteLine($"supervisor started (pid {process.Id})");
    return process;
}

void Stop(Process process, string why)
{
    process.Kill();
    process.WaitForExit();
    Console.WriteLine($"supervisor killed: {why}");
}

async Task<T> WaitFor<T>(string what, TimeSpan limit, Func<T?> probe) where T : class
{
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed < limit)
    {
        if (probe() is { } value)
        {
            Console.WriteLine($"{watch.Elapsed.TotalSeconds,5:0}s {what}");
            return value;
        }
        await Task.Delay(1000);
    }
    throw new TimeoutException($"no {what} after {limit.TotalMinutes} min. Store: {storeRoot}");
}

void Check(string check, bool ok, string detail) => log.Add((check, ok, detail));

static string? Ref(Event e, string key) => e.Refs.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

static IReadOnlyList<string> Refs(Event e, string key) =>
    e.Refs.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];

static void Run(string cwd, string program, params string[] arguments)
{
    var result = new Shell(cwd).Run(program, arguments);
    if (!result.Ok) throw new InvalidOperationException($"{program} {string.Join(' ', arguments)}: {result.Error}{result.Output}");
}
