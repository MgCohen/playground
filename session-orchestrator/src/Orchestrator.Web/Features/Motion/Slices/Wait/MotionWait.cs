namespace Orchestrator.Web.Features.Motion.Slices.Wait;

using Orchestrator.Web.Plugins.Js.Contracts.Commands;

public sealed record MotionWait(object? Target, WaitMode Mode, string? Arg = null) : JsCommand<WaitResult>;
