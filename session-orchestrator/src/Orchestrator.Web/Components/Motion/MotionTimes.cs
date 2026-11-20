using Orchestrator.Web.Features.Motion.Slices.Times;
using Umpire.Vsa.Contracts.Commands;

namespace Orchestrator.Web.Components.Motion;

/// <summary>
/// Reads a time token of motion.css from the page. motion.css is the one owner of each motion time (I11). A failed read or a
/// time of 0 (an unknown token) gives null, so the caller cannot use 0 s by mistake. (A helper in a slice is not allowed by the
/// VSA rules, so it stays here.)
/// </summary>
public static class MotionTimes
{
    public static async Task<TimeSpan?> Of(ICommandSender commands, string token)
    {
        var ms = await commands.SendAsync(new MotionMs(token), default);
        return ms.IsSuccess && ms.Value > 0 ? TimeSpan.FromMilliseconds(ms.Value) : null;
    }
}
