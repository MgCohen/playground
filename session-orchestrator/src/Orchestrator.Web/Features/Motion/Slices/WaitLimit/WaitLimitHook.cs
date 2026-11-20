namespace Orchestrator.Web.Features.Motion.Slices.WaitLimit;

using Orchestrator.Web.Features.Motion.Slices.Wait;
using Orchestrator.Web.Plugins.Js.Contracts;
using Umpire.Messages.Contracts;
using Umpire.Vsa.Handlers.Commands;

public sealed class WaitLimitHook(IJsTrace trace) : AfterCommandHook<MotionWait, WaitResult>
{
    public override Task AfterAsync(IEnvelope<MotionWait> envelope, Result<WaitResult> outcome)
    {
        if (outcome.IsSuccess && outcome.Value?.AtLimit == true)
        {
            var wait = envelope.Message;
            trace.Warn($"wait at limit #{envelope.Id}: {wait.Mode} {wait.Arg} on {wait.Target}: {outcome.Value}");
        }
        return Task.CompletedTask;
    }
}
