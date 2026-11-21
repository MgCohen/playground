namespace Orchestrator.Web.Features.Motion.Domain;

public sealed record MotionStep(string Name, params string[] States)
{
    public string State => States[0];

    public string Target => $"[data-motion~=\"{Name}\"]";
}
