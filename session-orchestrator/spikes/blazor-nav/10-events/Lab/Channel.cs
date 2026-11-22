namespace Spike.Events.Lab;

/// <summary>One command to the browser: a verb, an argument and a task for each phase that the browser reports.</summary>
public sealed class Command(int id, string verb, string arg)
{
    readonly Dictionary<string, TaskCompletionSource<OrchDoneArgs>> phases = [];

    public int Id { get; } = id;
    public string Verb { get; } = verb;
    public string Arg { get; } = arg;

    /// <summary>The task of one phase. It ends when the browser sends that phase.</summary>
    public Task<OrchDoneArgs> On(string phase) => Get(phase).Task;

    internal void Set(OrchDoneArgs done) => Get(done.Phase).TrySetResult(done);

    TaskCompletionSource<OrchDoneArgs> Get(string phase)
    {
        if (!phases.TryGetValue(phase, out var tcs))
            phases[phase] = tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return tcs;
    }
}

/// <summary>
/// The event channel: no IJSRuntime call. C# renders each open command as an element in the outlet. A script sees the
/// element, does the work and dispatches "orch-done" events. The custom event brings each phase back to C#.
/// </summary>
public sealed class Channel
{
    int next;
    readonly List<Command> open = [];

    public IReadOnlyList<Command> Open => open;

    /// <summary>The outlet renders again when the list of open commands changes.</summary>
    public event Action? Changed;

    // The last phase of each verb: after it, the command leaves the outlet.
    static string Last(string verb) => verb == "vt" ? "finished" : "end";

    public Command Send(string verb, string arg)
    {
        var command = new Command(++next, verb, arg);
        open.Add(command);
        Changed?.Invoke();
        return command;
    }

    public void Complete(OrchDoneArgs done)
    {
        var command = open.Find(c => c.Id == done.Id);
        if (command is null) return;
        command.Set(done);
        if (done.Phase != Last(command.Verb)) return;
        open.Remove(command);
        Changed?.Invoke();
    }
}

/// <summary>The time of one navigation, shared by the layout and the pages of one circuit, and the mode of the run.</summary>
public sealed class NavClock
{
    readonly System.Diagnostics.Stopwatch clock = new();

    public long T => clock.ElapsedMilliseconds;

    public void Restart() => clock.Restart();

    /// <summary>True: the pipeline uses the event channel. False: it uses IJSRuntime calls.</summary>
    public bool Events { get; set; } = true;
}
