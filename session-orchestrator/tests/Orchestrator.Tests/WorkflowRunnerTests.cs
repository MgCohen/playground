using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Sessions;
using Orchestrator.Core.Storage;
using Orchestrator.Core.Templates;
using Orchestrator.Core.Workflows;
using Orchestrator.Store;
using Orchestrator.Store.Local;

namespace Orchestrator.Tests;

/// <summary>The sessions of a run in memory. Each start makes an entry; each prompt and role change is recorded.</summary>
public sealed class FakeSessions : IRunSessions
{
    public Dictionary<string, SessionEntry> Entries { get; } = [];
    public List<RunSessionSpec> Started { get; } = [];
    public List<(string Session, string Text)> Prompts { get; } = [];

    public SessionEntry Add(string id, string? parent = null, string? type = null, string role = Roles.Free)
    {
        var entry = new SessionEntry(id) { Parent = parent, Role = role, Type = type is null ? null : new SessionType { Agent = type }, Created = new Stamp($"2026-11-18T10:00:{Entries.Count:00}Z", "test") };
        Entries[id] = entry;
        return entry;
    }

    public SessionEntry? Get(string id) => Entries.GetValueOrDefault(id);

    public IReadOnlyList<SessionEntry> Tree(string id)
    {
        var root = id;
        while (Entries.GetValueOrDefault(root)?.Parent is { } p) root = p;
        var tree = new List<SessionEntry>();
        void Walk(string current)
        {
            if (Entries.GetValueOrDefault(current) is not { } e) return;
            tree.Add(e);
            foreach (var child in Entries.Values.Where(c => c.Parent == current)) Walk(child.Id);
        }
        Walk(root);
        return tree;
    }

    public string Start(RunSessionSpec spec)
    {
        Started.Add(spec);
        var id = $"new-{Started.Count}";
        Entries[id] = new SessionEntry(id) { Parent = spec.Parent, Role = spec.Role, Workflow = spec.Workflow, Title = spec.Title, Type = spec.Type is null ? null : new SessionType { Agent = spec.Type } };
        return id;
    }

    public void Prompt(string session, string text) => Prompts.Add((session, text));

    public void SetRole(string session, string role, string? workflow)
    {
        if (Entries.TryGetValue(session, out var e)) Entries[session] = e with { Role = role, Workflow = workflow ?? e.Workflow };
    }
}

/// <summary>The questions and notices of a run in memory. A test answers a thread with <see cref="Answers"/>.</summary>
public sealed class FakeMessages : IRunMessages
{
    public List<(string Thread, string From, string To, string Question, IReadOnlyList<string>? Options)> Asked { get; } = [];
    public Dictionary<string, string> Answers { get; } = [];
    public List<(string Session, string Text)> Notices { get; } = [];

    public string Ask(string from, string to, string question, string context, IReadOnlyList<string>? options)
    {
        var thread = $"q{Asked.Count + 1}";
        Asked.Add((thread, from, to, question, options));
        return thread;
    }

    public string? Answer(string thread) => Answers.GetValueOrDefault(thread);

    public void Notify(string session, string text) => Notices.Add((session, text));

    /// <summary>The questions that a session asked itself, with no runner: (thread, session).</summary>
    public List<(string Thread, string Session)> Own { get; } = [];

    public List<(string Thread, string Reason)> Closed { get; } = [];

    public string? OpenQuestion(string session, string since) => Own.LastOrDefault(q => q.Session == session && !Answers.ContainsKey(q.Thread)).Thread;

    public void Close(string thread, string reason) => Closed.Add((thread, reason));

    public string Last => Asked[^1].Thread;
}

/// <summary>A tool that gives fixed holds and counts its calls. With no result, the step waits.</summary>
public sealed class FakeTool(string name, params string[] holds) : IWorkflowTool
{
    public int Calls { get; private set; }
    public bool Ready { get; set; } = true;
    public string Name => name;

    public StepResult? Run(StepContext context)
    {
        Calls++;
        return Ready ? new StepResult { Holds = holds, Output = new Dictionary<string, string> { ["result"] = $"{name} {Calls}" } } : null;
    }
}

public sealed class WorkflowRunnerTests
{
    string root = "";
    FakeSessions sessions = null!;
    FakeMessages messages = null!;
    readonly Dictionary<string, WorkflowDefinition> workflows = [];

    [SetUp]
    public void Prepare()
    {
        root = Directory.CreateTempSubdirectory("orch-run-").FullName;
        sessions = new FakeSessions();
        messages = new FakeMessages();
        sessions.Add("owner");
        workflows.Clear();
    }

    [TearDown]
    public void Clean()
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException)
        {
        }
    }

    WorkflowRunner Runner(params IWorkflowTool[] tools) =>
        new(new LocalRepositories(root).WorkflowRuns, n => workflows.GetValueOrDefault(n) ?? Home.Workflows(null).Get(n), WorkflowRunner.Executors(sessions, messages, tools), sessions, messages);

    WorkflowDefinition Define(string json)
    {
        var w = StoreJson.Read<WorkflowDefinition>(json)!;
        workflows[w.Name] = w;
        return w;
    }

    /// <summary>A graph: a (agent) → b or c by condition; b and c are done steps. Entry conditions as given.</summary>
    void Fork(string? whenB = "go b", string? whenC = "go c", string? entryC = null) => Define($$"""
        {
          "name": "fork", "version": 1, "purpose": "test", "entry": "a", "done": "finished",
          "steps": [
            { "id": "a", "title": "A", "executor": "agent", "instructions": "Do A.",
              "next": [ { "step": "b"{{(whenB is null ? "" : $", \"when\": \"{whenB}\"")}} }, { "step": "c"{{(whenC is null ? "" : $", \"when\": \"{whenC}\"")}} } ] },
            { "id": "b", "title": "B", "executor": "agent", "instructions": "Do B.", "done": true },
            { "id": "c", "title": "C", "executor": "agent", "instructions": "Do C."{{(entryC is null ? "" : $", \"when\": \"{entryC}\"")}}, "done": true }
          ]
        }
        """);

    static StepResult Holds(params string[] facts) => new() { Holds = facts };

    // Planning: the first workflow runs from its definition.

    [Test]
    public void The_planning_workflow_runs_from_its_definition_with_a_question_loop_and_a_change_loop()
    {
        var runner = Runner();
        var run = runner.Start("planning", "owner", new Dictionary<string, string> { ["goal"] = "Plan C3" }, "user");
        Assert.That((run.Step, run.Status, run.Wait!.Kind), Is.EqualTo(("read", RunStates.Waiting, WaitKinds.Agent)));
        Assert.That(sessions.Entries["owner"].Role, Is.EqualTo(Roles.Orchestrator));
        Assert.That(sessions.Entries["owner"].Workflow, Is.EqualTo("planning"));
        Assert.That(sessions.Prompts[^1].Text, Does.Contain("orch run step " + run.Id).And.Contain("Run input goal: Plan C3").And.Contain("\"questions are open\""));

        // read → ask: one condition holds.
        run = runner.Step(run.Id, Holds("questions are open"), "session:owner");
        Assert.That(run.Step, Is.EqualTo("ask"));
        run = runner.Step(run.Id, Holds(), "session:owner");
        Assert.That(run.Step, Is.EqualTo("ask"), "a step command with no content does not end an agent step");
        run = runner.Step(run.Id, new StepResult { Output = new Dictionary<string, string> { ["question"] = "Q1" } }, "session:owner");
        Assert.That((run.Step, run.Wait!.Kind), Is.EqualTo(("answer", WaitKinds.Ask)));
        Assert.That(messages.Asked[^1].To, Is.EqualTo("user"));

        // The answer arrives in the store: the step command with no input polls it.
        Assert.That(runner.Step(run.Id, null, "user").Step, Is.EqualTo("answer"));
        messages.Answers[messages.Last] = "Blue.";
        run = runner.Step(run.Id, null, "user");
        Assert.That(run.Step, Is.EqualTo("record"));
        Assert.That(run.History[^2].Answer, Is.EqualTo("Blue."));
        Assert.That(run.History[^2].Holds, Does.Contain("the answer is recorded in the store"));

        run = runner.Step(run.Id, Holds("no questions are open"), "session:owner");
        Assert.That(run.Step, Is.EqualTo("spec"));
        run = runner.Step(run.Id, new StepResult { Output = new Dictionary<string, string> { ["spec"] = "docs/spec.md" } }, "session:owner");
        Assert.That((run.Step, run.Wait!.Kind), Is.EqualTo(("approve", WaitKinds.Ask)));
        Assert.That(messages.Asked[^1].Options, Is.EqualTo(RunVerdicts.All));

        // change → back to ask (a loop).
        messages.Answers[messages.Last] = "change: add the risks";
        run = runner.Step(run.Id, null, "user");
        Assert.That(run.Step, Is.EqualTo("ask"));
        Assert.That(run.History[^2].Approval, Is.EqualTo(RunVerdicts.Change));

        run = runner.Step(run.Id, Holds(), "session:owner") is { Step: "ask" } ? runner.Step(run.Id, new StepResult { Output = new Dictionary<string, string> { ["question"] = "Q2" } }, "session:owner") : run;
        messages.Answers[messages.Last] = "Yes, list them.";
        run = runner.Step(run.Id, null, "user");
        run = runner.Step(run.Id, Holds("no questions are open"), "session:owner");
        run = runner.Step(run.Id, new StepResult { Output = new Dictionary<string, string> { ["spec"] = "docs/spec.md v2" } }, "session:owner");
        messages.Answers[messages.Last] = "approve";
        run = runner.Step(run.Id, null, "user");
        Assert.That(run.Step, Is.EqualTo("propose"));
        Assert.That(runner.Get(run.Id)!.Outputs["spec"], Is.EqualTo("docs/spec.md v2"));

        run = runner.Step(run.Id, Holds("The user approved the spec, and the owner has the list of next tasks."), "session:owner");
        Assert.That((run.Status, run.Wait), Is.EqualTo((RunStates.Done, (RunWait?)null)));
        Assert.That(run.History.Select(v => v.Step), Is.EqualTo(new[] { "read", "ask", "answer", "record", "spec", "approve", "ask", "answer", "record", "spec", "approve", "propose" }));
        Assert.That(messages.Notices[^1].Session, Is.EqualTo("owner"));
        Assert.Throws<WorkflowException>(() => runner.Step(run.Id, Holds("x"), "user"));
    }

    [Test]
    public void The_planning_definition_passes_the_graph_rules_and_has_the_C1_steps()
    {
        var planning = Home.Workflows(null).Get("planning")!;
        Assert.That(planning.Steps.Select(s => s.Id), Is.EqualTo(new[] { "read", "ask", "answer", "record", "spec", "approve", "propose" }));
        Assert.That(Home.Workflows(null).Check("planning", Home.Workflows(null).Files("planning")!), Is.Empty);
    }

    // Traversal: valid, then pick.

    [Test]
    public void One_valid_next_step_goes_there()
    {
        Fork();
        var runner = Runner();
        var run = runner.Start("fork", "owner", null, "user");
        run = runner.Step(run.Id, Holds("go c"), "user");
        Assert.That((run.Step, run.History[0].Next), Is.EqualTo(("c", "c")));
    }

    [Test]
    public void A_condition_with_no_text_is_always_valid()
    {
        Fork(whenB: null, whenC: "go c");
        var runner = Runner();
        var run = runner.Start("fork", "owner", null, "user");
        run = runner.Step(run.Id, new StepResult { Output = new Dictionary<string, string> { ["x"] = "1" } }, "user");
        Assert.That(run.Step, Is.EqualTo("b"));
    }

    [Test]
    public void Conditions_compare_with_no_case_and_no_end_period()
    {
        Fork();
        var runner = Runner();
        var run = runner.Start("fork", "owner", null, "user");
        Assert.That(runner.Step(run.Id, Holds("  Go B. "), "user").Step, Is.EqualTo("b"));
    }

    [Test]
    public void Many_valid_next_steps_wait_for_the_pick_of_the_agent()
    {
        Fork();
        var runner = Runner();
        var run = runner.Start("fork", "owner", null, "user");
        run = runner.Step(run.Id, Holds("go b", "go c"), "user");
        Assert.That((run.Step, run.Wait!.Kind), Is.EqualTo(("a", WaitKinds.Pick)));
        Assert.That(run.Wait.Options, Is.EqualTo(new[] { "b", "c" }));
        Assert.That(messages.Notices[^1].Text, Does.Contain("--pick"));
        Assert.Throws<WorkflowException>(() => runner.Step(run.Id, new StepResult { Pick = "a" }, "user"));
        run = runner.Step(run.Id, new StepResult { Pick = "c" }, "user");
        Assert.That(run.Step, Is.EqualTo("c"));
    }

    [Test]
    public void A_pick_in_the_step_result_goes_at_once()
    {
        Fork();
        var runner = Runner();
        var run = runner.Start("fork", "owner", null, "user");
        run = runner.Step(run.Id, new StepResult { Holds = ["go b", "go c"], Pick = "b" }, "user");
        Assert.That(run.Step, Is.EqualTo("b"));
    }

    [Test]
    public void No_valid_next_step_stops_the_run_and_asks()
    {
        Fork();
        var runner = Runner();
        var run = runner.Start("fork", "owner", null, "user");
        run = runner.Step(run.Id, Holds("something else"), "user");
        Assert.That((run.Step, run.Wait!.Kind), Is.EqualTo(("a", WaitKinds.Stuck)));
        Assert.That(messages.Asked[^1].Question, Does.Contain("no next step is valid"));
        Assert.That(messages.Asked[^1].Options, Is.EqualTo(new[] { "b", "c" }));
        Assert.That(runner.Step(run.Id, null, "user").Step, Is.EqualTo("a"), "no answer yet");
        messages.Answers[messages.Last] = "c, because the test says so";
        Assert.That(runner.Step(run.Id, null, "user").Step, Is.EqualTo("c"));
    }

    [Test]
    public void A_run_that_moves_on_closes_its_stale_stuck_question()
    {
        Fork();
        var runner = Runner();
        var run = runner.Start("fork", "owner", null, "user");
        run = runner.Step(run.Id, Holds("something else"), "user");
        var stuck = messages.Last;
        run = runner.Step(run.Id, new StepResult { Pick = "b" }, "session:owner");
        Assert.That(run.Step, Is.EqualTo("b"));
        Assert.That(messages.Closed.Select(c => c.Thread), Is.EqualTo(new[] { stuck }));
        Assert.That(messages.Closed[0].Reason, Does.Contain("moved on to step b"));
    }

    [Test]
    public void A_stop_or_a_verdict_of_the_step_command_closes_the_open_question_but_an_answered_one_stays()
    {
        Approval(ApprovalStrategies.Required);
        var runner = Runner();
        var run = runner.Start("approval", "owner", null, "user");
        runner.Step(run.Id, Holds("done"), "session:owner");
        var approval = messages.Last;
        runner.Step(run.Id, new StepResult { Verdict = RunVerdicts.Change }, "user");
        Assert.That(messages.Closed.Select(c => c.Thread), Is.EqualTo(new[] { approval }));

        runner.Step(run.Id, Holds("done"), "session:owner");
        var second = messages.Last;
        runner.Stop(run.Id, "test", "user");
        Assert.That(messages.Closed.Select(c => c.Thread), Is.EqualTo(new[] { approval, second }));
        Assert.That(messages.Closed[^1].Reason, Does.Contain("is stopped"));

        var other = runner.Start("approval", "owner", null, "user");
        runner.Step(other.Id, Holds("done"), "session:owner");
        messages.Answers[messages.Last] = "approve";
        runner.Step(other.Id, null, "user");
        Assert.That(messages.Closed, Has.Count.EqualTo(2), "an answered question is not closed");
    }

    [Test]
    public void An_ask_step_uses_the_open_question_of_the_owner_and_asks_no_second_question()
    {
        var runner = Runner();
        var run = runner.Start("planning", "owner", null, "user");
        run = runner.Step(run.Id, Holds("questions are open"), "session:owner");
        messages.Own.Add(("toolu_own", "owner"));
        var asked = messages.Asked.Count;
        run = runner.Step(run.Id, new StepResult { Output = new Dictionary<string, string> { ["question"] = "Q1" } }, "session:owner");
        Assert.That((run.Step, run.Wait!.Thread, run.Current!.Thread), Is.EqualTo(("answer", "toolu_own", "toolu_own")));
        Assert.That(messages.Asked, Has.Count.EqualTo(asked), "no second question");
        messages.Answers["toolu_own"] = "Blue.";
        run = runner.Step(run.Id, null, "user");
        Assert.That((run.Step, run.History[^2].Answer), Is.EqualTo(("record", "Blue.")));
    }

    [Test]
    public void An_ask_step_with_no_owner_question_asks_the_output_question_of_the_step_before()
    {
        var runner = Runner();
        var run = runner.Start("planning", "owner", null, "user");
        run = runner.Step(run.Id, Holds("questions are open"), "session:owner");
        run = runner.Step(run.Id, new StepResult { Output = new Dictionary<string, string> { ["question"] = "Which color?" } }, "session:owner");
        Assert.That((run.Step, messages.Asked[^1].Question), Is.EqualTo(("answer", "Which color?")));
    }

    [Test]
    public void The_entry_condition_of_the_target_also_counts()
    {
        Fork(whenB: "go", whenC: "go", entryC: "c is ready");
        var runner = Runner();
        var run = runner.Start("fork", "owner", null, "user");
        Assert.That(runner.Step(run.Id, Holds("go"), "user").Step, Is.EqualTo("b"), "c is not valid: its entry condition is not true");

        var second = runner.Start("fork", "owner", null, "user");
        Assert.That(runner.Step(second.Id, Holds("go", "c is ready"), "user").Wait!.Kind, Is.EqualTo(WaitKinds.Pick));
    }

    [Test]
    public void A_done_step_ends_the_run_only_when_the_definition_of_done_is_true()
    {
        Define("""
            {
              "name": "loop", "version": 1, "purpose": "test", "entry": "work", "done": "all items are done",
              "steps": [
                { "id": "work", "title": "Work", "executor": "agent", "instructions": "Do one item.", "done": true,
                  "next": [ { "step": "work", "when": "items are left" } ] }
              ]
            }
            """);
        var runner = Runner();
        var run = runner.Start("loop", "owner", null, "user");
        run = runner.Step(run.Id, Holds("items are left"), "user");
        run = runner.Step(run.Id, Holds("items are left"), "user");
        Assert.That((run.Status, run.History.Count), Is.EqualTo((RunStates.Waiting, 3)));
        run = runner.Step(run.Id, new StepResult { Done = true }, "user");
        Assert.That((run.Status, run.History.Count), Is.EqualTo((RunStates.Done, 3)));
    }

    [Test]
    public void A_loop_with_no_end_stops_at_the_visit_limit()
    {
        Define("""
            {
              "name": "spin", "version": 1, "purpose": "test", "entry": "a", "done": "never",
              "steps": [
                { "id": "a", "title": "A", "executor": "tool", "tool": "noop", "instructions": "Spin.", "next": [ { "step": "b" } ] },
                { "id": "b", "title": "B", "executor": "tool", "tool": "noop", "instructions": "Spin.", "done": true, "next": [ { "step": "a" } ] }
              ]
            }
            """);
        var tool = new FakeTool("noop");
        var run = Runner(tool).Start("spin", "owner", null, "user");
        Assert.That(run.Status, Is.EqualTo(RunStates.Stopped));
        Assert.That(run.History, Has.Count.EqualTo(WorkflowRunner.MaxVisits));
        Assert.That(run.Reason, Does.Contain("loop"));
    }

    [Test]
    public void The_exit_condition_keeps_the_run_on_the_step_until_it_is_true()
    {
        Define("""
            {
              "name": "exit", "version": 1, "purpose": "test", "entry": "a", "done": "ok",
              "steps": [
                { "id": "a", "title": "A", "executor": "agent", "instructions": "Do A.", "exit": "the file is written", "next": [ { "step": "b" } ] },
                { "id": "b", "title": "B", "executor": "agent", "instructions": "Do B.", "done": true }
              ]
            }
            """);
        var runner = Runner();
        var run = runner.Start("exit", "owner", null, "user");
        run = runner.Step(run.Id, Holds("something"), "user");
        Assert.That((run.Step, run.Wait!.Kind), Is.EqualTo(("a", WaitKinds.Exit)));
        run = runner.Step(run.Id, Holds("the file is written"), "user");
        Assert.That(run.Step, Is.EqualTo("b"));
        Assert.That(run.History[0].Holds, Is.EqualTo(new[] { "something", "the file is written" }));
    }

    // Approval strategies.

    WorkflowDefinition Approval(string strategy, string executor = "agent") => Define($$"""
        {
          "name": "approval", "version": 1, "purpose": "test", "entry": "a", "done": "ok",
          "steps": [
            { "id": "a", "title": "A", "executor": "{{executor}}", {{(executor == "tool" ? "\"tool\": \"noop\"," : "")}} "instructions": "Do A.",
              "approval": { "by": "user", "strategy": "{{strategy}}" },
              "next": [ { "step": "a", "when": "the verdict is change" }, { "step": "b", "when": "the verdict is approve" }, { "step": "b", "when": "no approval" } ] },
            { "id": "b", "title": "B", "executor": "agent", "instructions": "Do B.", "done": true }
          ]
        }
        """);

    [Test]
    public void A_required_approval_waits_for_the_verdict_and_cannot_be_skipped()
    {
        Approval(ApprovalStrategies.Required);
        var runner = Runner();
        var run = runner.Start("approval", "owner", null, "user");
        run = runner.Step(run.Id, Holds("done"), "session:owner");
        Assert.That((run.Wait!.Kind, run.Wait.By), Is.EqualTo((WaitKinds.Approval, "user")));
        Assert.That(messages.Asked[^1].Options, Is.EqualTo(RunVerdicts.All));
        Assert.Throws<WorkflowException>(() => runner.Step(run.Id, new StepResult { SkipApproval = true }, "user"));
        Assert.That(runner.Step(run.Id, null, "user").Wait!.Kind, Is.EqualTo(WaitKinds.Approval), "no answer yet");
        messages.Answers[messages.Last] = "approve";
        run = runner.Step(run.Id, null, "user");
        Assert.That((run.Step, run.History[0].Approval), Is.EqualTo(("b", RunVerdicts.Approve)));
    }

    [Test]
    public void A_verdict_of_the_step_command_closes_a_waiting_approval()
    {
        Approval(ApprovalStrategies.Required);
        var runner = Runner();
        var run = runner.Start("approval", "owner", null, "user");
        runner.Step(run.Id, Holds("done"), "session:owner");
        run = runner.Step(run.Id, new StepResult { Verdict = RunVerdicts.Change }, "user");
        Assert.That((run.Step, run.History.Count, run.History[0].Approval), Is.EqualTo(("a", 2, RunVerdicts.Change)));
        Assert.Throws<WorkflowException>(() => runner.Step(run.Id, new StepResult { Holds = ["x"], Verdict = "maybe" }, "user"));
    }

    [Test]
    public void A_skippable_approval_waits_but_the_owner_can_skip_it()
    {
        Approval(ApprovalStrategies.Skippable);
        var runner = Runner();
        var run = runner.Start("approval", "owner", null, "user");
        run = runner.Step(run.Id, Holds("no approval"), "session:owner");
        Assert.That(run.Wait!.Kind, Is.EqualTo(WaitKinds.Approval));
        run = runner.Step(run.Id, new StepResult { SkipApproval = true }, "session:owner");
        Assert.That((run.Step, run.History[0].Approval), Is.EqualTo(("b", "skipped")));
    }

    [Test]
    public void An_ignored_approval_does_not_wait()
    {
        Approval(ApprovalStrategies.Ignored, executor: "tool");
        var run = Runner(new FakeTool("noop", "no approval")).Start("approval", "owner", null, "user");
        Assert.That((run.Step, run.History[0].Approval), Is.EqualTo(("b", ApprovalStrategies.Ignored)));
        Assert.That(messages.Asked, Is.Empty);
    }

    [Test]
    public void A_refused_approval_stops_the_run()
    {
        Approval(ApprovalStrategies.Required);
        var runner = Runner();
        var run = runner.Start("approval", "owner", null, "user");
        run = runner.Step(run.Id, new StepResult { Holds = ["x"], Verdict = RunVerdicts.Refuse }, "user");
        Assert.That((run.Status, run.Reason), Is.EqualTo((RunStates.Stopped, "user refused the step a")));
    }

    // Executors.

    [Test]
    public void A_tool_step_runs_its_tool_at_once_and_waits_when_the_tool_has_no_result()
    {
        Define("""
            {
              "name": "tools", "version": 1, "purpose": "test", "entry": "a", "done": "the tool is done",
              "steps": [ { "id": "a", "title": "A", "executor": "tool", "tool": "check", "instructions": "Check.", "done": true } ]
            }
            """);
        var tool = new FakeTool("check", "the tool is done") { Ready = false };
        var runner = Runner(tool);
        var run = runner.Start("tools", "owner", null, "user");
        Assert.That((run.Status, run.Wait!.Kind), Is.EqualTo((RunStates.Waiting, WaitKinds.Tool)));
        tool.Ready = true;
        run = runner.Step(run.Id, null, "user");
        Assert.That((run.Status, run.Outputs["result"], tool.Calls), Is.EqualTo((RunStates.Done, "check 2", 2)));
    }

    [Test]
    public void A_tool_step_with_an_unknown_tool_fails()
    {
        Define("""
            {
              "name": "tools", "version": 1, "purpose": "test", "entry": "a", "done": "x",
              "steps": [ { "id": "a", "title": "A", "executor": "tool", "tool": "missing", "instructions": "Check.", "done": true } ]
            }
            """);
        Assert.That(() => Runner().Start("tools", "owner", null, "user"), Throws.TypeOf<WorkflowException>().With.Message.Contains("no tool"));
    }

    WorkflowDefinition AgentStep(string session) => Define($$"""
        {
          "name": "agents", "version": 1, "purpose": "test", "entry": "a", "done": "x",
          "steps": [ { "id": "a", "title": "A", "executor": "agent", "session": {{session}}, "instructions": "Do A.", "done": true } ]
        }
        """);

    [Test]
    public void An_agent_step_of_the_owner_prompts_the_owner()
    {
        AgentStep("\"owner\"");
        var run = Runner().Start("agents", "owner", null, "user");
        Assert.That((run.Current!.Session, run.Wait!.Session), Is.EqualTo(("owner", "owner")));
        Assert.That(sessions.Prompts.Single().Session, Is.EqualTo("owner"));
        Assert.That(sessions.Started, Is.Empty);
    }

    [Test]
    public void An_agent_step_of_a_new_session_starts_a_worker_child_of_the_owner()
    {
        AgentStep("\"new\"");
        var run = Runner().Start("agents", "owner", null, "user");
        var spec = sessions.Started.Single();
        Assert.That((spec.Parent, spec.Role, spec.Workflow, spec.Type), Is.EqualTo(("owner", Roles.Worker, "agents", (string?)null)));
        Assert.That(spec.Goal, Does.Contain("orch run step " + run.Id));
        Assert.That(run.Current!.Session, Is.EqualTo("new-1"));
        Assert.That(sessions.Prompts, Is.Empty, "the brief carries the prompt");
    }

    [Test]
    public void An_agent_step_of_an_existing_session_prompts_it_and_makes_it_a_worker()
    {
        sessions.Add("other");
        AgentStep("""{ "session": "other" }""");
        var run = Runner().Start("agents", "owner", null, "user");
        Assert.That((run.Current!.Session, sessions.Entries["other"].Role, sessions.Entries["other"].Workflow), Is.EqualTo(("other", Roles.Worker, "agents")));
        Assert.That(sessions.Prompts.Single().Session, Is.EqualTo("other"));

        AgentStep("""{ "session": "nobody" }""");
        Assert.Throws<WorkflowException>(() => Runner().Start("agents", "owner", null, "user"));
    }

    [Test]
    public void An_agent_step_of_a_type_uses_a_session_of_that_type_in_the_work_tree_else_starts_one()
    {
        sessions.Add("root");
        sessions.Entries["owner"] = sessions.Entries["owner"] with { Parent = "root" };
        sessions.Add("decider-1", parent: "root", type: "orch:decider", role: Roles.Orchestrator);
        sessions.Add("outside", type: "decider");
        AgentStep("""{ "type": "decider" }""");
        var run = Runner().Start("agents", "owner", null, "user");
        Assert.That(run.Current!.Session, Is.EqualTo("decider-1"));
        Assert.That(sessions.Entries["decider-1"].Role, Is.EqualTo(Roles.Orchestrator), "a session that owns a run keeps its role");

        sessions.Entries["decider-1"] = sessions.Entries["decider-1"] with { Lifecycle = new SessionLifecycle { Phase = ControlStates.Archived } };
        run = Runner().Start("agents", "owner", null, "user");
        Assert.That(run.Current!.Session, Is.EqualTo("new-1"));
        Assert.That(sessions.Started.Single().Type, Is.EqualTo("decider"));
    }

    [Test]
    public void An_ask_step_with_an_approval_asks_the_approval_party()
    {
        Define("""
            {
              "name": "asks", "version": 1, "purpose": "test", "entry": "a", "done": "x",
              "steps": [ { "id": "a", "title": "A", "executor": "ask", "instructions": "Approve?", "approval": { "by": "orchestrator", "strategy": "required" }, "done": true } ]
            }
            """);
        var runner = Runner();
        var run = runner.Start("asks", "owner", null, "user");
        Assert.That((messages.Asked.Single().To, messages.Asked.Single().From), Is.EqualTo(("orchestrator", "owner")));
        run = runner.Step(run.Id, new StepResult { Answer = "approve, ship it", Done = true }, "session:owner");
        Assert.That((run.Status, run.History[0].Approval), Is.EqualTo((RunStates.Done, RunVerdicts.Approve)));
    }

    // Run state.

    [Test]
    public void The_run_state_is_on_the_repository_and_a_new_runner_continues_it()
    {
        Fork();
        var run = Runner().Start("fork", "owner", null, "user");
        Assert.That(File.Exists(Path.Combine(root, StoreFiles.Runs, $"{run.Id}.json")));
        var again = Runner();
        Assert.That(again.Require(run.Id[..12]).Id, Is.EqualTo(run.Id), "a run by the first characters of its id");
        Assert.That(again.Step(run.Id, Holds("go b"), "user").Step, Is.EqualTo("b"));
        Assert.That(again.List(r => r.IsOpen).Single().Id, Is.EqualTo(run.Id));
        Assert.That(again.Stop(run.Id, "test", "user").Status, Is.EqualTo(RunStates.Stopped));
        Assert.That(again.List(r => r.IsOpen), Is.Empty);
    }

    [Test]
    public void A_start_needs_a_known_workflow_and_a_known_owner()
    {
        Assert.Throws<WorkflowException>(() => Runner().Start("nothing", "owner", null, "user"));
        Assert.Throws<WorkflowException>(() => Runner().Start("planning", "nobody", null, "user"));
    }

    [Test]
    public void The_prompt_names_the_conditions_once_with_their_use()
    {
        var planning = Home.Workflows(null).Get("planning")!;
        var run = new WorkflowRun("r1", "planning", 1, "owner");
        var conditions = RunPrompt.Conditions(planning, planning.Step("record")!).ToList();
        Assert.That(conditions.Select(c => c.Condition), Is.EqualTo(new[] { "questions are open", "no questions are open" }));
        Assert.That(RunPrompt.For(run, planning, planning.Step("propose")!), Does.Contain("the definition of done").And.Contain("Send a report to orchestrator when done."));
    }

    [Test]
    public void Verdicts_parse_from_the_start_of_the_answer()
    {
        Assert.That(new[] { "Approve.", "approved, thanks", "change: more", "refuse", "No", "maybe" }.Select(RunVerdicts.Parse),
            Is.EqualTo(new[] { "approve", "approve", "change", "refuse", "refuse", null }));
    }

    [Test]
    public void The_default_agent_of_a_role_is_the_plugin_agent_and_a_type_replaces_it()
    {
        Assert.That(Roles.AgentOf(new SessionEntry("a") { Role = Roles.Worker }), Is.EqualTo("worker"));
        Assert.That(Roles.AgentOf(new SessionEntry("a") { Role = Roles.Orchestrator }), Is.EqualTo("orchestrator"));
        Assert.That(Roles.AgentOf(new SessionEntry("a") { Role = Roles.Free }), Is.Null);
        Assert.That(Roles.AgentOf(new SessionEntry("a") { Role = Roles.Worker, Type = new SessionType { Agent = "decider" } }), Is.EqualTo("decider"));
    }

    [Test]
    public void A_tool_step_must_name_its_tool_and_only_a_tool_step_names_one()
    {
        var spec = Home.Spec();
        Definition Files(string step) => new(new Dictionary<string, string> { [Catalog.Workflows.Marker] = $$"""
            { "name": "t", "version": 1, "purpose": "p", "entry": "a", "done": "d", "steps": [ {{step}} ] }
            """ });
        Assert.That(WorkflowDefinition.Check("tt", Files("""{ "id": "a", "title": "A", "executor": "tool", "instructions": "i", "done": true }"""), spec, null),
            Has.Some.Contains("a tool step names its tool"));
        Assert.That(WorkflowDefinition.Check("tt", Files("""{ "id": "a", "title": "A", "executor": "agent", "tool": "x", "instructions": "i", "done": true }"""), spec, null),
            Has.Some.Contains("only a tool step names a tool"));
    }
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalWorkflowRunContract(string backend) : RepositoryContract<WorkflowRun>(backend)
{
    protected override IRepository<Doc<WorkflowRun>, string> Open(string root) => Repos(root).WorkflowRuns;

    protected override string IdOf(int n) => $"planning-0000000{n}";

    protected override WorkflowRun Sample(int n) => new(IdOf(n), "planning", 1, $"session-{n}")
    {
        Status = n % 2 == 0 ? RunStates.Waiting : RunStates.Done,
        Step = "ask",
        Wait = new RunWait(WaitKinds.Ask, $"wait {n}") { Thread = $"q{n}" },
        Input = new Dictionary<string, string> { ["goal"] = $"goal {n}" },
        History = [new RunStep("read", "agent", "2026-11-18T10:00:00Z") { Holds = ["questions are open"], Next = "ask" }],
        Created = new Stamp($"2026-11-18T10:00:0{n}Z", "user"),
    };
}

/// <summary>The runner on the store: the CLI, the session registry, the event stream and the supervisor turns.</summary>
[NonParallelizable]
public sealed class WorkflowStoreTests
{
    static string Run(Rig rig, params string[] args)
    {
        var output = new StringWriter();
        Environment.SetEnvironmentVariable("ORCH_STORE", rig.Folder);
        try
        {
            var code = Cli.Cli.Run(args, output, output, rig.Folder);
            Assert.That(code, Is.EqualTo(0), output.ToString());
            return output.ToString();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
        }
    }

    [Test]
    public void The_planning_workflow_runs_through_the_orch_run_commands()
    {
        using var rig = new Rig();
        var owner = rig.Create("start", "Owner");
        var started = Run(rig, "run", "start", "planning", "--owner", owner[..8], "--goal", "Plan C3");
        var run = WorkflowHost.Runner(rig.Store, "test").List().Single();
        Assert.That(started, Does.Contain($"Run {run.Id} (planning v1)").And.Contain("step read"));
        Assert.That(File.Exists(Path.Combine(rig.Folder, StoreFiles.Runs, $"{run.Id}.json")));
        Assert.That(rig.Store.Session(owner).Role, Is.EqualTo(Roles.Orchestrator));
        Assert.That(rig.Store.Session(owner).Workflow, Is.EqualTo("planning"));
        Assert.That(rig.Store.Pending(owner).Text, Does.Contain($"orch run step {run.Id}"));

        Run(rig, "run", "step", run.Id, "--holds", "questions are open");
        Run(rig, "run", "step", run.Id[..12], "--output-question", "Which color?");
        var thread = WorkflowHost.Runner(rig.Store, "test").Require(run.Id).Wait!.Thread!;
        Assert.That(rig.Store.Decision(thread)!.Session, Is.EqualTo(owner));
        Assert.That(rig.Store.Decision(thread)!.Question, Is.EqualTo("Which color?"), "the output question of the ask step");
        Run(rig, "answer", thread, "Blue");
        Assert.That(Run(rig, "run", "step", run.Id), Does.Contain("step record"));
        Run(rig, "run", "step", run.Id, "--holds", "no questions are open");
        Run(rig, "run", "step", run.Id, "--output-spec", "docs/spec.md");
        Run(rig, "run", "step", run.Id, "--answer", "approve");
        Assert.That(Run(rig, "run", "step", run.Id, "--done"), Does.Contain(": done, step propose"));
        var shown = Run(rig, "run", "show", run.Id);
        Assert.That(shown, Does.Contain("1. read (agent)").And.Contain("approval: approve").And.Contain("7. propose (agent)"));
        Assert.That(Run(rig, "run", "list", "--open"), Does.Contain("No runs."));
    }

    [Test]
    public void In_the_store_a_stale_stuck_question_closes_with_a_notice_and_the_ask_step_uses_the_owner_question()
    {
        using var rig = new Rig();
        var owner = rig.Create("start", "Owner");
        var runner = WorkflowHost.Runner(rig.Store, "test");
        var run = runner.Start("planning", owner, null, Parties.User);
        run = runner.Step(run.Id, new StepResult { Holds = ["something else"] }, Parties.Session(owner));
        var stuck = run.Wait!.Thread!;
        Assert.That(rig.Store.OpenDecisions(owner).Select(d => d.Id), Is.EqualTo(new[] { stuck }));

        // C3-F2: the owner picks ask. The stuck question closes, with the notice question-closed to the owner.
        run = runner.Step(run.Id, new StepResult { Pick = "ask" }, Parties.Session(owner));
        Assert.That(rig.Store.OpenDecisions(owner), Is.Empty);
        Assert.That(rig.Store.Events.FirstAnswer(stuck)!.From, Is.EqualTo(Parties.Supervisor));
        var notice = rig.Store.Events.Thread(stuck).Single(e => e.Kind == EventKinds.Notice);
        Assert.That((notice.To, rig.Store.Events.Fields(notice)["kind"]), Is.EqualTo((Parties.Session(owner), (object?)"question-closed")));

        // C3-F3: the owner asks with ask_user. The answer step uses that question.
        rig.Store.SaveDecision(new Decision("toolu_own", owner) { Question = "Which color?" });
        run = runner.Step(run.Id, new StepResult { Output = new Dictionary<string, string> { ["question"] = "Which color?" } }, Parties.Session(owner));
        Assert.That((run.Step, run.Wait!.Thread), Is.EqualTo(("answer", "toolu_own")));
        Assert.That(rig.Store.OpenDecisions(owner).Select(d => d.Id), Is.EqualTo(new[] { "toolu_own" }), "one question");
        new Messages(rig.Store).Answer(owner, "toolu_own", "Blue");
        Assert.That(runner.Step(run.Id, null, Parties.User).Step, Is.EqualTo("record"));
    }

    [Test]
    public async Task A_new_session_of_an_agent_step_starts_through_the_registry_and_runs_with_the_worker_agent()
    {
        using var rig = new Rig();
        var owner = rig.Create("start", "Owner");
        Home.Workflows(rig.Store.Root).Add("helper", new Definition(new Dictionary<string, string>
        {
            [Catalog.Workflows.Marker] = """
                { "name": "helper", "version": 1, "purpose": "test", "entry": "help", "done": "the help is given",
                  "steps": [ { "id": "help", "title": "Help", "executor": "agent", "session": "new", "instructions": "Give help.", "done": true } ] }
                """,
        }));
        var run = WorkflowHost.Runner(rig.Store, "test").Start("helper", owner, null, Parties.User);
        var child = run.Current!.Session!;
        var entry = rig.Store.Session(child);
        Assert.That((entry.Parent, entry.Role, entry.Workflow, entry.Type?.Agent), Is.EqualTo((owner, Roles.Worker, "helper", (string?)null)));
        await rig.Cycle();
        var turn = rig.Turns.Single(t => t.Id == child);
        Assert.That(turn.Agent, Is.EqualTo("worker"));
        Assert.That(turn.Prompt, Does.Contain($"orch run step {run.Id}"));
        Assert.That(rig.Turns.Single(t => t.Id == owner).Agent, Is.EqualTo("orchestrator"));
    }

    [Test]
    public void A_brief_takes_the_stream_type_and_workflow_and_a_child_keeps_the_stream_of_its_parent()
    {
        using var rig = new Rig();
        var parent = new Messages(rig.Store).NewSession(new BriefFields { Title = "P", Goal = "g", Cwd = "C:/x", Stream = "C", Type = "decider", Workflow = "planning" }, "test");
        var child = new Messages(rig.Store).NewSession(new BriefFields { Title = "C", Goal = "g", Cwd = "C:/x", Parent = parent, Agent = "worker" }, "test");
        Assert.That((rig.Store.Session(parent).Stream, rig.Store.Session(parent).Type!.Agent, rig.Store.Session(parent).Workflow), Is.EqualTo(("C", "decider", "planning")));
        Assert.That((rig.Store.Session(child).Stream, rig.Store.Session(child).Type!.Agent), Is.EqualTo(("C", "worker")));
    }
}
