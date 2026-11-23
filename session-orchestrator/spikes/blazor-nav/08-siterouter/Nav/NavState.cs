using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Spike.SiteRouter.Nav;

/// <summary>The kind of a move. Push and Back come from the Navigator. Tab is a link click.</summary>
public enum Move { Tab, Push, Back }

/// <summary>The URL in one object. SiteHost gives it to the pages as a cascading value.</summary>
public sealed record NavState(string Url, string Path, Type? Page, Move Move, IReadOnlyDictionary<string, string> Query);

/// <summary>What a page hook gets: the move kind and the from and to page types.</summary>
public sealed record NavContext(Move Move, Type? From, Type? To)
{
    public override string ToString() => $"{Move} {From?.Name ?? "-"} -> {To?.Name ?? "-"}";
}

/// <summary>The optional hooks of a page. A page implements only the hooks it needs.</summary>
public interface INavPage
{
    /// <summary>The Navigator calls it after the motion ends.</summary>
    Task OnEnterAsync(NavContext c) => Task.CompletedTask;

    /// <summary>The Navigator calls it before the move. False cancels the move.</summary>
    Task<bool> OnLeaveAsync(NavContext c) => Task.FromResult(true);
}

/// <summary>The view side of navigation. SiteHost implements it for the Navigator.</summary>
public interface INavHost
{
    /// <summary>The live page of a slot. A slot is a page type of the table or a push entry.</summary>
    INavPage? Instance(object? slot);

    /// <summary>Shows the active slot with the motion class and waits for the motion end.</summary>
    Task PlayAsync(Move move);
}

/// <summary>A page on the push stack. It has no URL. Done gives the result back to PushAsync.</summary>
public sealed class PushEntry(Type page, IDictionary<string, object> parameters)
{
    public Type Page { get; } = page;
    public IDictionary<string, object> Parameters { get; } = parameters;
    internal TaskCompletionSource<object?> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>The typed page table: one URL for each page type.</summary>
public sealed class PageTable
{
    readonly Dictionary<string, Type> byPath = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<Type, string> byType = [];

    public PageTable Map<TPage>(string path) where TPage : IComponent
    {
        byPath[path] = typeof(TPage);
        byType[typeof(TPage)] = path;
        return this;
    }

    public Type? Find(string path) => byPath.GetValueOrDefault(path);

    public string PathOf(Type page) =>
        byType.TryGetValue(page, out var path) ? path : throw new InvalidOperationException($"{page.Name} has no URL");
}

public static class NavServices
{
    /// <summary>Adds the page table (one for the app) and the Navigator (one for each circuit).</summary>
    public static IServiceCollection AddNav(this IServiceCollection services, Action<PageTable> map)
    {
        var table = new PageTable();
        map(table);
        return services.AddSingleton(table).AddScoped<Navigator>();
    }
}
