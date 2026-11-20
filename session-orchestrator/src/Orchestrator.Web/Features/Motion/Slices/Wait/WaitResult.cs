namespace Orchestrator.Web.Features.Motion.Slices.Wait;

public sealed record WaitResult(string By, double Ms)
{
    public const string End = "end";

    public const string Cancel = "cancel";

    public const string Limit = "limit";

    public const string Hidden = "hidden";

    public const string Missing = "missing";

    public const string None = "none";

    public bool AtLimit => By is Limit or Missing or None;

    public override string ToString() => $"by {By} after {Ms} ms";
}
