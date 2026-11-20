namespace Orchestrator.Web.Features.Motion.Slices.Wait;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<WaitMode>))]
public enum WaitMode
{
    Settle,
    Property,
    Time,
    Event,
}
