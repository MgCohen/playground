using System.Text.Json;
using Orchestrator.Cli.Git;
using Orchestrator.Core.Templates;
using Orchestrator.Store;

namespace Orchestrator.Cli;

public static class ToolCommands
{
    public static int Template(CommandContext c)
    {
        var set = c.Store.Templates;
        var word = c.Args.Positional.Count < 2 ? "list" : c.Args.Positional[1];
        switch (word)
        {
            case "list":
                return TemplateList(c, set);
            case "check":
                return TemplateCheckCommand(c, set);
            case "add":
                var added = set.Store.Add(TemplateFiles.ReadFolder(c.Args.Required(2, "folder")), c.Args.Flag("replace"));
                c.Output.WriteLine($"Added {added.Kind} to {added.Location}{(added.ReplacesCore ? ". It replaces the core template" : "")}. A running supervisor uses it after a restart.");
                return 0;
            case "remove":
                var kind = c.Args.Required(2, "kind");
                set.Store.Remove(kind);
                c.Output.WriteLine($"Removed {kind} from the store. The old files are in {LocalTemplateStore.Removed}/. A running supervisor applies it after a restart.");
                return 0;
        }
        if (!set.Has(word))
        {
            c.Output.WriteLine($"no template {word}. orch template lists the templates.");
            return 1;
        }
        var template = set[word];
        if (c.Args.Option("check") is { } check)
        {
            var result = set.Validate(template.Kind, JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(check)));
            c.Output.WriteLine(result.IsValid ? $"{check} is a valid {template.Kind}." : $"{check} is not a valid {template.Kind}: {result.Message}");
            return result.IsValid ? 0 : 1;
        }
        if (c.Args.Option("render") is { } render)
        {
            c.Output.WriteLine(set.Render(template.Kind, Render.Fields(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(render))), c.Args.Option("name")));
            return 0;
        }
        var schema = JsonDocument.Parse(template.SchemaText).RootElement;
        var required = schema.TryGetProperty("required", out var r) ? r.EnumerateArray().Select(x => x.GetString()).ToHashSet() : [];
        c.Output.WriteLine($"{template.Kind}: {Description(template)}");
        foreach (var p in schema.GetProperty("properties").EnumerateObject())
        {
            var description = p.Value.TryGetProperty("description", out var d) ? d.GetString() : "";
            var type = p.Value.TryGetProperty("type", out var ty) ? ty.ToString() : p.Value.TryGetProperty("enum", out var en) ? string.Join("|", en.EnumerateArray()) : "";
            c.Output.WriteLine($"  {p.Name}{(required.Contains(p.Name) ? " (required)" : "")}: {type}. {description}".TrimEnd(' ', '.') + ".");
        }
        if (template.RenderNames() is { Count: > 0 } names) c.Output.WriteLine($"  renders: {string.Join(", ", names)}");
        return 0;
    }

    static int TemplateList(CommandContext c, TemplateSet set)
    {
        foreach (var t in set.All.OrderBy(t => t.Kind, StringComparer.Ordinal))
        {
            var source = t.Source == TemplateSources.Store ? (t.ReplacesCore ? "store, replaces core" : "store") : "core";
            c.Output.WriteLine($"{t.Kind,-9} {source,-21} {Description(t)}");
        }
        foreach (var e in set.LoadErrors) c.Output.WriteLine($"not loaded: {e}");
        return set.LoadErrors.Count == 0 ? 0 : 1;
    }

    /// <summary>orch template check [&lt;kind&gt;] [--folder DIR]: the checks of the spec (templates/_spec) for one template, a folder, or all.</summary>
    static int TemplateCheckCommand(CommandContext c, TemplateSet set)
    {
        var spec = TemplateSpec.Load(Home.Templates());
        var targets = c.Args.Option("folder") is { } folder ? [TemplateFiles.ReadFolder(folder)]
            : c.Args.Positional.Count > 2 ? [set.Store.Get(c.Args.Positional[2]) ?? throw new TemplateException($"no template {c.Args.Positional[2]}")]
            : set.Store.List().Select(info => set.Store.Get(info.Kind)).OfType<TemplateFiles>().ToList();
        var bad = 0;
        foreach (var files in targets)
        {
            var errors = TemplateCheck.Check(files, spec);
            if (errors.Count > 0) bad++;
            c.Output.WriteLine(errors.Count == 0 ? $"{files.Kind}: ok" : files.Kind + ":" + string.Concat(errors.Select(e => Environment.NewLine + "  " + e)));
        }
        foreach (var e in set.LoadErrors) c.Output.WriteLine($"not loaded: {e}");
        return bad == 0 && set.LoadErrors.Count == 0 ? 0 : 1;
    }

    static string Description(Template t) =>
        JsonDocument.Parse(t.SchemaText).RootElement.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

    public static int Doctor(CommandContext c)
    {
        var failures = 0;
        void Check(string name, Func<(bool Ok, string Detail)> test)
        {
            (bool Ok, string Detail) result;
            try
            {
                result = test();
            }
            catch (Exception e)
            {
                result = (false, e.Message);
            }
            if (!result.Ok) failures++;
            c.Output.WriteLine($"{(result.Ok ? "ok  " : "FAIL")} {name}: {result.Detail}");
        }
        var shell = new Shell(c.Cwd);
        Check("home", () => (File.Exists(Path.Combine(Home.Root(), Home.Solution)), Home.Root()));
        Check("install", () => Install.Check(new Shell(Home.Root()), Install.Commit));
        Check("app", () => Install.CheckApp(new Shell(Home.Root()), SupervisorHost.AppRoot()));
        Check("store", () => (c.Store.Repositories.Exists(), c.Store.Root));
        Check("policy", () =>
        {
            var policy = Home.LoadPolicy(c.Store.Repositories);
            return (true, string.Join(", ", policy.Levels.Select(l => $"{l.Name} {(l.TimeoutSeconds is { } t ? $"{t} s" : "never")}")));
        });
        Check("templates", () =>
        {
            var bad = c.Store.Templates.All.Where(t => !c.Store.Templates.Validate(t.Kind, JsonSerializer.Deserialize<JsonElement>(t.Example ?? "{}")).IsValid).ToList();
            var loadErrors = c.Store.Templates.LoadErrors;
            var ok = bad.Count == 0 && loadErrors.Count == 0;
            return (ok, ok ? $"{c.Store.Templates.All.Count()} kinds, each example is valid" : string.Join("; ", bad.Select(t => $"invalid example: {t.Kind}").Concat(loadErrors.Select(e => $"not loaded: {e}"))));
        });
        Check("claude", () => shell.Run("claude", "--version") is var r && r.Ok ? (true, r.Text) : (false, "not on the PATH"));
        Check("gh", () => shell.Run("gh", "auth", "status") is var r && r.Ok ? (true, "signed in") : (false, "not signed in: run gh auth login"));
        Check("plugin", () =>
        {
            var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
            var json = JsonDocument.Parse(File.ReadAllText(settings)).RootElement;
            if (!json.TryGetProperty("env", out var env) || !env.TryGetProperty("CLAUDE_CODE_PLUGIN_DIRS", out var dirs)) return (false, "CLAUDE_CODE_PLUGIN_DIRS is not in the env block of ~/.claude/settings.json");
            var path = dirs.GetString() ?? "";
            return (File.Exists(Path.Combine(path, ".claude-plugin", "plugin.json")), path);
        });
        Check("supervisor", () =>
        {
            var port = Environment.GetEnvironmentVariable("ORCH_PORT") ?? "7777";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = http.GetAsync($"http://127.0.0.1:{port}/api/sessions").GetAwaiter().GetResult();
            return (response.IsSuccessStatusCode, $"http://127.0.0.1:{port}");
        });
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Starts, stops or shows the supervisor of the store. The process runs on its own, out of the session that starts it.</summary>
    public static int Supervisor(CommandContext c)
    {
        var port = int.TryParse(c.Args.Option("port") ?? Environment.GetEnvironmentVariable("ORCH_PORT"), out var p) ? p : 7777;
        switch (c.Args.Required(1, "start|stop|status"))
        {
            case "start":
                var (started, record) = SupervisorHost.Start(c.Store.Root, port, TimeSpan.FromSeconds(120));
                c.Output.WriteLine(started
                    ? $"Started the supervisor: http://127.0.0.1:{port}/, store {c.Store.Root}, pid {record!.Pid}, log {record.Log}"
                    : $"A supervisor already answers on http://127.0.0.1:{port}/.");
                return 0;
            case "stop":
                var stopped = SupervisorHost.Stop(port);
                c.Output.WriteLine(stopped is not null
                    ? $"Stopped the supervisor of port {port} (pid {stopped.Pid}). Its turns continue after the next start."
                    : SupervisorHost.Answers(port)
                        ? $"The supervisor on port {port} was not started by orch or the tray. Stop it where it runs."
                        : $"No supervisor runs on port {port}.");
                return stopped is not null || !SupervisorHost.Answers(port) ? 0 : 1;
            case "status":
                var answers = SupervisorHost.Answers(port);
                var known = SupervisorHost.Record(port);
                c.Output.WriteLine($"port {port}: {(answers ? "answers" : "no answer")}");
                if (known is not null) c.Output.WriteLine($"pid {known.Pid}, store {known.Store}, started {known.Started}, log {known.Log}, command {known.Command}");
                return answers ? 0 : 1;
            default:
                throw new UsageException("orch supervisor start|stop|status [--port P]");
        }
    }

    /// <summary>
    /// Prints the id and the start time of the parent process: the process that ran orch. In a session, that is the claude
    /// process, so the plugin writes them in lease.json and the supervisor can check if the holder of a live lease is alive.
    /// </summary>
    public static int Process(CommandContext c)
    {
        var self = Environment.ProcessId;
        if (Processes.ParentOf(self) is not { } parent || Processes.StartTime(parent) is not { } start)
            throw new MessageException("the parent process is not known");
        c.Output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { pid = parent, started = Core.Leases.Times.Format(start.ToUniversalTime()) }));
        return 0;
    }
}
