using Orchestrator.Web.Features.Motion.Slices.Times;
using Umpire.Vsa.Contracts.Commands;

namespace Orchestrator.Web.Components.Motion;

/// <summary>Reads a time token of motion.css from the page. motion.css is the one owner of each motion time (I11).</summary>
public static class MotionTimes
{
    /// <summary>The time of a token, for example "--t-undo"; null when the page does not answer (the JS trace has the error).</summary>
    public static async Task<TimeSpan?> Of(ICommandSender commands, string token)
    {
        var ms = await commands.SendAsync(new MotionMs(token), default);
        return ms.IsSuccess ? TimeSpan.FromMilliseconds(ms.Value) : null;
    }
}
