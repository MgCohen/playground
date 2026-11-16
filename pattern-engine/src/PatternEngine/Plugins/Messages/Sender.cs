using Microsoft.Extensions.DependencyInjection;

namespace PatternEngine.Plugins.Messages;

public interface ISender
{
    Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default);
}

public sealed class Sender(IServiceProvider services) : ISender
{
    public async Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        var handlerType = typeof(IHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        var handler = services.GetRequiredService(handlerType);
        var method = handlerType.GetMethod(nameof(IHandler<ICommand<TResult>, TResult>.HandleAsync))!;
        return await (Task<Result<TResult>>)method.Invoke(handler, [command, ct])!;
    }
}

public static class MessageRegistration
{
    public static IServiceCollection AddHandlers(this IServiceCollection services, System.Reflection.Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandler<,>)))
            {
                services.AddTransient(contract, type);
            }
        }

        services.AddSingleton<ISender, Sender>();
        return services;
    }
}
