using System.Diagnostics;
using System.Text.Json;

var cwd = args[0];
var store = args[1];
var id = Guid.NewGuid().ToString();
Directory.CreateDirectory(Path.Combine(store, "sessions", id));
File.WriteAllText(Path.Combine(store, "sessions", id, "enabled"), "");

var first = await Turn(["--session-id", id], "Remember the word KIWI. Reply with one word: OK.");
var second = await Turn(["--resume", id], "Which word did I ask you to remember? Reply with that word only.");

var lease = Path.Combine(store, "sessions", id, "lease.json");
Console.WriteLine($"session {id}");
Console.WriteLine($"turn 1: {first.Result} (events {first.Events}, session {first.SessionId})");
Console.WriteLine($"turn 2: {second.Result} (events {second.Events}, session {second.SessionId})");
Console.WriteLine($"lease: {(File.Exists(lease) ? File.ReadAllText(lease) : "missing")}");
Console.WriteLine($"mirror rows: {Directory.GetFiles(Path.Combine(store, "sessions", id, "mirror")).Length}");

async Task<(string Result, int Events, string SessionId)> Turn(string[] session, string prompt)
{
    var info = new ProcessStartInfo("claude")
    {
        WorkingDirectory = cwd,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (var a in new[] { "-p", "--output-format", "stream-json", "--verbose", "--setting-sources", "user" }) info.ArgumentList.Add(a);
    foreach (var a in session) info.ArgumentList.Add(a);
    info.Environment["ORCH_HOLDER"] = "supervisor";
    info.Environment["ORCH_STORE"] = store.Replace('\\', '/');
    using var process = Process.Start(info)!;
    await process.StandardInput.WriteAsync(prompt);
    process.StandardInput.Close();
    var events = 0;
    var result = "";
    var sessionId = "";
    while (await process.StandardOutput.ReadLineAsync() is { } line)
    {
        events++;
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();
        if (root.TryGetProperty("session_id", out var s)) sessionId = s.GetString() ?? "";
        if (type == "result") result = $"{root.GetProperty("subtype").GetString()}: {(root.TryGetProperty("result", out var r) ? r.GetString() : "")}";
        Console.WriteLine($"  {type}/{(root.TryGetProperty("subtype", out var st) ? st.GetString() : "")}");
    }
    await process.WaitForExitAsync();
    Console.WriteLine($"  exit {process.ExitCode} {await process.StandardError.ReadToEndAsync()}");
    return (result, events, sessionId);
}
