namespace Orchestrator.Web.Features.Motion.Slices.Times;

using Orchestrator.Web.Plugins.Js.Contracts.Commands;

public sealed record MotionMs(string Token) : JsCommand<double>;
