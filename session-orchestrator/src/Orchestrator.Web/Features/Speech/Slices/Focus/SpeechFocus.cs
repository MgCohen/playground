namespace Orchestrator.Web.Features.Speech.Slices.Focus;

using Orchestrator.Web.Plugins.Js.Contracts.Commands;
using Umpire.Messages.Contracts;

public sealed record SpeechFocus(string Id, bool Voice) : JsCommand<Unit>;
