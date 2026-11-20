namespace Orchestrator.Web.Features.Motion.Slices.Dot;

using Orchestrator.Web.Plugins.Js.Contracts.Commands;
using Umpire.Messages.Contracts;

public sealed record MotionDot(object Nav) : JsCommand<Unit>;
