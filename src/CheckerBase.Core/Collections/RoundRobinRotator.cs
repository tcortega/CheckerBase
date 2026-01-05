namespace CheckerBase.Core.Collections;

/// <summary>
/// Thread-safe, lock-free round-robin rotator for any type.
/// </summary>
/// <typeparam name="T">The type of items to rotate through.</typeparam>
public sealed class RoundRobinRotator<T>
{
    private readonly T[] _items;
    private int _index = -1;

    /// <summary>
    /// Gets the number of items in the rotator.
    /// </summary>
    public int Count => _items.Length;

    /// <summary>
    /// Creates a new rotator with the given items.
    /// </summary>
    /// <param name="items">The items to rotate through. Must not be empty.</param>
    /// <exception cref="ArgumentException">Thrown when items is empty.</exception>
    public RoundRobinRotator(T[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Length == 0)
            throw new ArgumentException("Items array cannot be empty.", nameof(items));

        _items = items;
    }

    /// <summary>
    /// Gets the next item using lock-free round-robin.
    /// </summary>
    /// <returns>The next item.</returns>
    public T Next()
    {
        var idx = (uint)Interlocked.Increment(ref _index) % (uint)_items.Length;
        return _items[idx];
    }
}