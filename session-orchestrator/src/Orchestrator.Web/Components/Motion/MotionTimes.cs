using Microsoft.JSInterop;

namespace Orchestrator.Web.Components.Motion;

/// <summary>Reads a time token of motion.css from the page. motion.css is the one owner of each motion time (I11).</summary>
public static class MotionTimes
{
    /// <summary>The time of a token, for example "--t-undo". An unknown token is zero.</summary>
    public static async Task<TimeSpan> Of(IJSRuntime js, string token) =>
        TimeSpan.FromMilliseconds(await js.InvokeAsync<double>("orchMotion.ms", token));
}
