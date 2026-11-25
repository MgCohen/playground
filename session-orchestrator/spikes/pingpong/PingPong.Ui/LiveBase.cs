using Microsoft.AspNetCore.Components;
using PingPong.Core;

namespace PingPong.Ui;

/// <summary>A page root that renders again on each change of the stream. The small components take parameters only.</summary>
public abstract class LiveBase : ComponentBase, IDisposable
{
    [Inject] protected GameClient Client { get; set; } = null!;

    protected override void OnInitialized()
    {
        Client.Changed += Refresh;
        Client.Start();
    }

    void Refresh() => InvokeAsync(StateHasChanged);

    public virtual void Dispose() => Client.Changed -= Refresh;
}
