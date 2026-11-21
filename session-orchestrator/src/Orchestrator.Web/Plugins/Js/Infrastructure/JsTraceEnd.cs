namespace Orchestrator.Web.Plugins.Js.Infrastructure;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Web.Plugins.Js.Contracts;
using Umpire.Messages.Contracts;
using Umpire.Messages.Handlers;

public sealed class JsTraceEnd : AfterStep
{
    public override Task AfterAsync(Envelope envelope)
    {
        var started = envelope.Items.TryGetValue(JsTraceStart.StartItem, out var at) && at is long t ? t : 0;
        var ms = started == 0 ? 0 : (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var name = envelope.Items.TryGetValue(JsTraceStart.NameItem, out var n) ? n : envelope.Message.GetType().Name;
        var trace = envelope.Services.GetRequiredService<JsTrace>();
        var arguments = envelope.Items.TryGetValue(JsTraceStart.ArgumentsItem, out var a) ? a : "";
        var line = $"js {name}({arguments}) #{envelope.Id}: {Outcome(envelope)} in {ms} ms";
        var error = envelope.Result?.Error ?? envelope.Error;
        if (error is not null && error.Code != JsErrors.Canceled)
        {
            trace.Warn(line);
        }
        else
        {
            trace.Write(line);
        }
        return Task.CompletedTask;
    }

    static string Outcome(Envelope envelope) =>
        envelope.Result?.Error is { } error ? $"error {error.Code}: {error.Message}"
        : envelope.Error is { } rejected ? $"rejected {rejected.Code}: {rejected.Message}"
        : $"{envelope.Result?.Value}";
}
