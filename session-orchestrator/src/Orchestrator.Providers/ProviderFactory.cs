using System.Reflection;

namespace Orchestrator.Providers;

/// <summary>Marks the provider that the factory makes. Exactly one provider has it.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DefaultProviderAttribute : Attribute;

/// <summary>Makes the provider. The code outside the provider folders does not name a provider.</summary>
public static class ProviderFactory
{
    static IProvider? fallback;

    /// <summary>The provider with no options: for the app facts and the commands that need no store root.</summary>
    public static IProvider Default => fallback ??= Create(new ProviderOptions());

    public static IProvider Create(ProviderOptions options)
    {
        var type = typeof(ProviderFactory).Assembly.GetTypes()
            .Single(t => typeof(IProvider).IsAssignableFrom(t) && t.GetCustomAttribute<DefaultProviderAttribute>() is not null);
        return (IProvider)Activator.CreateInstance(type, options)!;
    }
}
