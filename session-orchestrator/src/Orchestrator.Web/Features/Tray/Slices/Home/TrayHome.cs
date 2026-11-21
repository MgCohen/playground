namespace Orchestrator.Web.Features.Tray.Slices.Home;

using Orchestrator.Web.Plugins.Js.Contracts.Commands;
using Umpire.Messages.Contracts;

public sealed record TrayHome(object Stage) : JsCommand<Unit>;
