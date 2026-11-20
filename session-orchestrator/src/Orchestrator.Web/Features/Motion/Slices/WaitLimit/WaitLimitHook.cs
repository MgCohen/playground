namespace Orchestrator.Web.Features.Motion.Slices.WaitLimit;

using Orchestrator.Web.Features.Motion.Slices.Wait;
using Orchestrator.Web.Plugins.Js.Infrastructure;
using Umpire.Messages.Contracts;
using Umpire.Vsa.Handlers.Commands;

internal sealed class WaitLimitHook(JsTrace trace) : AfterCommandHook<MotionWait, WaitResult>
{
    public override Task AfterAsync(IEnvelope<MotionWait> envelope, Result<WaitResult> outcome)
    {
        if (outcome.IsSuccess && outcome.Value?.AtLimit == true)
        {
            var wait = envelope.Message;
            trace.Write($"wait at limit #{envelope.Id}: {wait.Mode} {wait.Arg} on {wait.Target}: {outcome.Value}");
        }
        return Task.CompletedTask;
    }
}
