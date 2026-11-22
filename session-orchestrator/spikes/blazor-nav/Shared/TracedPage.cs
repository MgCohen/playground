using Microsoft.AspNetCore.Components;

namespace NavSpike.Shared;

/// <summary>A page base that logs the plain Blazor lifecycle: OnInitialized, the first OnAfterRender and Dispose.</summary>
public abstract class TracedPage : ComponentBase, IDisposable
{
    [Inject] protected Trace Trace { get; set; } = default!;

    protected abstract string PageName { get; }

    protected override void OnInitialized() => Trace.Add($"{PageName}: OnInitialized");

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender) Trace.Add($"{PageName}: OnAfterRender(first)");
    }

    public virtual void Dispose() => Trace.Add($"{PageName}: Dispose");
}
