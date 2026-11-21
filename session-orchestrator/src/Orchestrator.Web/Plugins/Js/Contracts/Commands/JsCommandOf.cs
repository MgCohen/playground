namespace Orchestrator.Web.Plugins.Js.Contracts.Commands;

using Orchestrator.Web.Plugins.Js.Contracts;
using Umpire.Vsa.Contracts.Commands;

public abstract record JsCommand<TResult>(string? Function = null) : JsCommand(Function), ICommand<TResult>;
