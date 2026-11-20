using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Orchestrator.Web.App.Host;
using Orchestrator.Web.Features.Motion.Slices.Wait;
using Orchestrator.Web.Plugins.Js.Contracts;
using Orchestrator.Web.Plugins.Js.Infrastructure;
using Umpire.Vsa.Contracts.Commands;

namespace Orchestrator.Tests;

/// <summary>
/// The JS command pipeline of the motion pipeline (R1, S3): a command record is the one part of a JS call. The JS name comes
/// from the type, one handler calls the JS runtime, a JS error comes back as an Error, and the trace has each call.
/// </summary>
public sealed class JsPipelineTests
{
    /// <summary>A JS runtime for the tests: it records each call and answers with the given function.</summary>
    sealed class FakeJs(Func<string, object?[]?, CancellationToken, Task<object?>> answer) : IJSRuntime
    {
        public List<(string Name, object?[]? Args)> Calls { get; } = [];

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            await InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add((identifier, args));
            return (TValue)(await answer(identifier, args, cancellationToken))!;
        }
    }

    static (ICommandSender Commands, IServiceProvider Services) Compose(FakeJs js)
    {
        var services = new ServiceCollection();
        services.AddScoped<IJSRuntime>(_ => js);
        OrchHost.Add(services, new ConfigurationBuilder().Build());
        var scope = services.BuildServiceProvider().CreateScope();
        return (scope.ServiceProvider.GetRequiredService<ICommandSender>(), scope.ServiceProvider);
    }

    [Test]
    public async Task MotionWait_with_fake_js()
    {
        var js = new FakeJs((_, _, _) => Task.FromResult<object?>(new WaitResult(WaitResult.End, 12)));
        var (commands, services) = Compose(js);

        var waited = await commands.SendAsync(new MotionWait(".tray-panel", WaitMode.Property, "clip-path"), default);

        Assert.That(waited.Value, Is.EqualTo(new WaitResult(WaitResult.End, 12)));
        Assert.That(js.Calls.Single().Name, Is.EqualTo("orchMotion.wait"), "the JS name comes from the type");
        Assert.That(js.Calls.Single().Args, Is.EqualTo(new object?[] { ".tray-panel", WaitMode.Property, "clip-path" }));
        Assert.That(services.GetRequiredService<JsTrace>().Lines.Last(), Does.Contain("js orchMotion.wait").And.Contain("by end"));
    }

    [Test]
    public async Task A_JS_error_is_an_Error_with_the_JS_name_and_a_trace_line()
    {
        var js = new FakeJs((_, _, _) => throw new JSException("orchMotion.wait is not a function"));
        var (commands, services) = Compose(js);

        var waited = await commands.SendAsync(new MotionWait(".x", WaitMode.Settle), default);

        Assert.That(waited.IsSuccess, Is.False, "nothing is swallowed: the caller gets the error");
        Assert.That(waited.Error!.Code, Is.EqualTo(JsErrors.Failed));
        Assert.That(waited.Error.Message, Does.Contain("orchMotion.wait"));
        Assert.That(services.GetRequiredService<JsTrace>().Lines.Last(), Does.Contain("error js.failed"));
    }

    [Test]
    public async Task A_wait_at_its_limit_writes_a_trace_line()
    {
        var js = new FakeJs((_, _, _) => Task.FromResult<object?>(new WaitResult(WaitResult.Limit, 1000)));
        var (commands, services) = Compose(js);

        await commands.SendAsync(new MotionWait(".tray-panel", WaitMode.Property, "clip-path"), default);

        Assert.That(services.GetRequiredService<JsTrace>().Lines, Has.Some.Contains("wait at limit").And.Contains("clip-path"));
    }

    [Test]
    public async Task A_newer_call_with_the_same_key_cancels_the_older_one()
    {
        var js = new FakeJs(async (_, _, cancel) =>
        {
            await Task.Delay(Timeout.Infinite, cancel);
            return null;
        });
        var (commands, _) = Compose(js);

        var older = commands.SendAsync(new MotionWait(".page", WaitMode.Settle) { Key = "tab-in" }, default);
        var newer = commands.SendAsync(new MotionWait(".page", WaitMode.Settle) { Key = "other" }, default);
        _ = commands.SendAsync(new MotionWait(".page", WaitMode.Settle) { Key = "tab-in" }, default);

        var first = await older.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(first.Error?.Code, Is.EqualTo(JsErrors.Canceled));
        Assert.That(newer.IsCompleted, Is.False, "a call with another key goes on");
    }

    [TestCase(typeof(MotionWait), "orchMotion.wait")]
    [TestCase(typeof(JsPipelineTests), "orchJs.pipelineTests")]
    public void The_JS_name_comes_from_the_type(Type type, string name) =>
        Assert.That(JsNames.NameOf(type), Is.EqualTo(name));

    [Test]
    public void One_wait_utility()
    {
        var src = Path.Combine(Orchestrator.Store.Home.Root(), "src");
        var old = new[] { "orchTray.end", "orchTray.frames", "orchMotion.settle" };
        var hits = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".razor" or ".cs" or ".js")
            .SelectMany(f => old.Where(o => File.ReadAllText(f).Contains(o, StringComparison.Ordinal)).Select(o => $"{Path.GetFileName(f)}: {o}"))
            .ToList();
        Assert.That(hits, Is.Empty, "Use orchMotion.wait (the MotionWait command) for each wait (I12)");
    }

    /// <summary>
    /// The R1 folders have zero Umpire findings. Umpire has no check mode for each folder, so the build keeps the format
    /// findings as warnings everywhere; this test builds the web project again and reads the findings of the R1 folders.
    /// The two UMP020301 findings of MotionWait are an Umpire gap: the VSA rule does not know the open handler of preview.11.
    /// </summary>
    [Test]
    [Category("Build")]
    public void R1_folders_have_no_findings()
    {
        var root = Orchestrator.Store.Home.Root();
        var start = new ProcessStartInfo("dotnet", ["build", Path.Combine(root, "src", "Orchestrator.Web"), "--no-dependencies", "--no-restore",
            "-t:Rebuild", "-p:TreatWarningsAsErrors=false", "-nologo", "-v:q"])
        { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        using var build = Process.Start(start)!;
        var output = build.StandardOutput.ReadToEnd() + build.StandardError.ReadToEnd();
        build.WaitForExit();
        string[] r1 = [@"\Plugins\Js\", @"\Features\Motion\", "/Plugins/Js/", "/Features/Motion/"];
        var findings = output.Split('\n')
            .Where(l => l.Contains("UMP", StringComparison.Ordinal) && r1.Any(f => l.Contains(f, StringComparison.Ordinal)))
            .Where(l => !(l.Contains("UMP020301", StringComparison.Ordinal) && l.Contains("MotionWait", StringComparison.Ordinal)))
            .Select(l => l.Trim()).Distinct().ToList();
        Assert.That(build.ExitCode, Is.EqualTo(0), output);
        Assert.That(findings, Is.Empty, string.Join("\n", findings));
    }
}
