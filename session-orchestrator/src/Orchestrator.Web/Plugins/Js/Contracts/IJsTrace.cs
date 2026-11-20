namespace Orchestrator.Web.Plugins.Js.Contracts;

public interface IJsTrace
{
    IReadOnlyList<string> Lines { get; }

    void Write(string line);
}
