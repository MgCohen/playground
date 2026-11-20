namespace Orchestrator.Web.Features.Motion.Domain;

public static class MotionSteps
{
    public static readonly MotionStep Grow = new("grow", "is-" + TrayPhase.Open);

    public static readonly MotionStep Fold = new("fold", "is-" + TrayPhase.Folded);

    public static readonly MotionStep BadgeIn = new("badge-in", "is-" + TrayPhase.Badge);

    public static readonly MotionStep BadgeOut = new("badge-out", "is-" + TrayPhase.BadgeOut);

    public static readonly MotionStep Spin = new("spin", "spin");

    public static readonly MotionStep PeekOut = new("peek-out", "on");

    public static readonly MotionStep TabIn = new("tab-in", "is-tab-in");

    public static readonly MotionStep Leave = new("leave", "is-leaving");

    public static readonly MotionStep DeckThrow = new("deck-throw", "out-r", "out-l");

    public static readonly MotionStep DeckTuck = new("deck-tuck", "tuck");

    public static IReadOnlyList<MotionStep> All { get; } =
        [Grow, Fold, BadgeIn, BadgeOut, Spin, PeekOut, TabIn, Leave, DeckThrow, DeckTuck];
}
