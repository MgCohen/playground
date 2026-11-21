namespace Orchestrator.Web.Plugins.Js.Contracts;

public interface IJsTrace
{
    IReadOnlyList<string> Lines { get; }

    event Action<string>? Written;

    void Write(string line);

    void Warn(string line);
}
