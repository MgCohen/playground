namespace Orchestrator.Core.Storage;

/// <summary>
/// An optional port of an append-only repository, such as the envelopes: the entities in the order of their first
/// read, from a position. A reader keeps its position and gets only the new entities at each call, with no scan of
/// the entities that it has. A repository without this port gives the same entities through a filter on its list.
/// </summary>
public interface IAppendFeed<T>
{
    /// <summary>The entities after the first <paramref name="position"/> entities. The list of earlier entities does not change.</summary>
    IReadOnlyList<T> ReadFrom(int position);
}
