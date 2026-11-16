namespace PatternEngine.Plugins.Messages;

public readonly record struct Result<T>
{
    private readonly T? value;

    private Result(T? value, Error? error)
    {
        this.value = value;
        Error = error;
    }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public T Value => IsSuccess ? value! : throw new InvalidOperationException($"The result failed: {Error}");

    public static Result<T> Ok(T value) => new(value, null);

    public static Result<T> Fail(string code, string message) => new(default, new Error(code, message));

    public static Result<T> Fail(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Ok(value);

    public static implicit operator Result<T>(Error error) => Fail(error);
}
