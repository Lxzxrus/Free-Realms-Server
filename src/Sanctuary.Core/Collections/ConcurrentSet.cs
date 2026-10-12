using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Core.Collections;

public class ConcurrentSet<T> : IReadOnlySet<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _dictionary;

    /// <summary>
    /// Initializes an instance of the ConcurrentSet class.
    /// </summary>
    public ConcurrentSet()
    {
        _dictionary = new ConcurrentDictionary<T, byte>();
    }

    public bool TryAdd(T item) => _dictionary.TryAdd(item, 0);

    public bool Contains(T item) => _dictionary.ContainsKey(item);

    public bool TryRemove(T item) => _dictionary.TryRemove(item, out _);

    public void Clear() => _dictionary.Clear();

    public int Count => _dictionary.Count;

    public bool IsEmpty => _dictionary.IsEmpty;

    public IEnumerator<T> GetEnumerator() => _dictionary.Keys.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // IReadOnlySet<T>, so the set can stand in wherever a HashSet<T> is read. Each is a snapshot comparison.

    public bool IsProperSubsetOf(IEnumerable<T> other) => _dictionary.Keys.ToHashSet().IsProperSubsetOf(other);

    public bool IsProperSupersetOf(IEnumerable<T> other) => _dictionary.Keys.ToHashSet().IsProperSupersetOf(other);

    public bool IsSubsetOf(IEnumerable<T> other) => _dictionary.Keys.ToHashSet().IsSubsetOf(other);

    public bool IsSupersetOf(IEnumerable<T> other) => other.All(Contains);

    public bool Overlaps(IEnumerable<T> other) => other.Any(Contains);

    public bool SetEquals(IEnumerable<T> other) => _dictionary.Keys.ToHashSet().SetEquals(other);
}
