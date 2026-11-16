namespace PatternEngine.Plugins.Messages;

public interface ICommand<TResult>;

public interface IHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct);
}
