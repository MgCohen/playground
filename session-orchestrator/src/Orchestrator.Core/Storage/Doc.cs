namespace Orchestrator.Core.Storage;

/// <summary>
/// One stored value with a string id: the entity of each data kind of the store. A change of <see cref="Value"/>
/// tells the repository that returned the document, so the next save writes it.
/// </summary>
public sealed class Doc<T> : Entity<string>
{
    T value;
    Action<Doc<T>>? changed;

    public Doc(string id, T value)
    {
        Id = id;
        this.value = value;
    }

    public T Value
    {
        get => value;
        set
        {
            this.value = value;
            IsChanged = true;
            changed?.Invoke(this);
        }
    }

    /// <summary>True after a change of <see cref="Value"/> that no save wrote.</summary>
    public bool IsChanged { get; private set; }

    /// <summary>For implementations: the call that the repository gets at each change.</summary>
    public void Track(Action<Doc<T>> onChange) => changed = onChange;

    /// <summary>For implementations: a save wrote the value.</summary>
    public void Saved(long version)
    {
        Version = version;
        IsChanged = false;
    }
}
