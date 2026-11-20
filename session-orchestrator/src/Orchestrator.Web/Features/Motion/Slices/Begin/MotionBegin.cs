namespace Orchestrator.Web.Features.Motion.Slices.Begin;

using Orchestrator.Web.Plugins.Js.Contracts.Commands;
using Umpire.Messages.Contracts;

public sealed record MotionBegin(string Kind) : JsCommand<Unit>;
