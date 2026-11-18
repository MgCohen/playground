using System.Reflection;

namespace Orchestrator.Providers;

/// <summary>Marks the provider that the factory makes when <c>ORCH_PROVIDER</c> is not set. Exactly one provider has it.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DefaultProviderAttribute : Attribute;

/// <summary>Gives a provider a name for <c>ORCH_PROVIDER</c>, for example <c>fake</c>.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ProviderAttribute(string name) : Attribute
{
    public string Name => name;
}

/// <summary>Makes the provider. The code outside the provider folders does not name a provider.</summary>
public static class ProviderFactory
{
    /// <summary>Selects a named provider in place of the default provider, for example <c>fake</c> for a demo with no model.</summary>
    public const string Variable = "ORCH_PROVIDER";

    static IProvider? fallback;

    /// <summary>The provider with no options: for the app facts and the commands that need no store root.</summary>
    public static IProvider Default => fallback ??= Create(new ProviderOptions());

    public static IProvider Create(ProviderOptions options) => Create(options, Environment.GetEnvironmentVariable(Variable));

    public static IProvider Create(ProviderOptions options, string? name)
    {
        var types = typeof(ProviderFactory).Assembly.GetTypes().Where(t => typeof(IProvider).IsAssignableFrom(t) && !t.IsAbstract).ToList();
        var type = name is { Length: > 0 }
            ? types.SingleOrDefault(t => string.Equals(t.GetCustomAttribute<ProviderAttribute>()?.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{Variable}={name} names no provider. Known: {string.Join(", ", types.Select(t => t.GetCustomAttribute<ProviderAttribute>()?.Name).OfType<string>())}, or no value for the default provider")
            : types.Single(t => t.GetCustomAttribute<DefaultProviderAttribute>() is not null);
        return (IProvider)Activator.CreateInstance(type, options)!;
    }
}
