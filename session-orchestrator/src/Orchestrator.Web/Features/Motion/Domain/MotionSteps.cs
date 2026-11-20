namespace Orchestrator.Web.Features.Motion.Domain;

public static class MotionSteps
{
    public static readonly MotionStep Grow = new("grow", "is-open");

    public static readonly MotionStep Fold = new("fold", "is-folded");

    public static readonly MotionStep BadgeIn = new("badge-in", "is-badge");

    public static readonly MotionStep BadgeOut = new("badge-out", "is-badge-out");

    public static readonly MotionStep Spin = new("spin", "spin");

    public static readonly MotionStep PeekOut = new("peek-out", "on");

    public static readonly MotionStep TabIn = new("tab-in", "is-tab-in");

    public static readonly MotionStep Leave = new("leave", "is-leaving");

    public static readonly MotionStep DeckThrow = new("deck-throw", "out-r");

    public static readonly MotionStep DeckTuck = new("deck-tuck", "tuck");

    public static readonly MotionStep DeckBackIn = new("deck-back-in", "back-in");

    public static readonly MotionStep DeckArrive = new("deck-arrive", "arrive");

    public static IReadOnlyList<MotionStep> All { get; } =
        [Grow, Fold, BadgeIn, BadgeOut, Spin, PeekOut, TabIn, Leave, DeckThrow, DeckTuck, DeckBackIn, DeckArrive];
}
