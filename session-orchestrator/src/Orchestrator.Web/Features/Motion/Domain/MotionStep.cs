namespace Orchestrator.Web.Features.Motion.Domain;

public sealed record MotionStep(string Name, string State)
{
    public string Target => $"[data-motion~=\"{Name}\"]";
}
