namespace Orchestrator.Core.Storage;

/// <summary>Append-only log text. Logs are not entities, so they do not use <see cref="IRepository{T, TId}"/>.</summary>
public interface ILogSink
{
    void Write(string line);
}
