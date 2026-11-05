using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace CarKit.Maui;

/// <summary>
/// Immutable array with structural equality, used for every collection in the CarKit model.
/// The default value behaves as an empty array.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
[CollectionBuilder(typeof(CarArray), nameof(CarArray.Create))]
public readonly struct CarArray<T> : IReadOnlyList<T>, IEquatable<CarArray<T>>
{
    private readonly ImmutableArray<T> _items;

    /// <summary>Creates an array that wraps <paramref name="items"/>.</summary>
    /// <param name="items">The items; a default array is treated as empty.</param>
    public CarArray(ImmutableArray<T> items) => _items = items;

    private ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    /// <summary>Gets the number of items.</summary>
    public int Count => Items.Length;

    /// <summary>Gets a value indicating whether the array has no items.</summary>
    public bool IsEmpty => Items.IsEmpty;

    /// <summary>Gets the item at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based index.</param>
    public T this[int index] => Items[index];

    /// <summary>Returns the items as an <see cref="ImmutableArray{T}"/>.</summary>
    /// <returns>The wrapped items; never a default array.</returns>
    public ImmutableArray<T> AsImmutableArray() => Items;

    /// <summary>Returns a copy with the item at <paramref name="index"/> replaced.</summary>
    /// <param name="index">The zero-based index.</param>
    /// <param name="item">The new item.</param>
    /// <returns>The new array.</returns>
    public CarArray<T> SetItem(int index, T item) => new(Items.SetItem(index, item));

    /// <summary>Returns a copy with <paramref name="item"/> appended.</summary>
    /// <param name="item">The item to add.</param>
    /// <returns>The new array.</returns>
    public CarArray<T> Add(T item) => new(Items.Add(item));

    /// <summary>Returns a copy with <paramref name="item"/> inserted at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based index.</param>
    /// <param name="item">The item to insert.</param>
    /// <returns>The new array.</returns>
    public CarArray<T> Insert(int index, T item) => new(Items.Insert(index, item));

    /// <summary>Returns a copy without the item at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based index.</param>
    /// <returns>The new array.</returns>
    public CarArray<T> RemoveAt(int index) => new(Items.RemoveAt(index));

    /// <summary>Returns an allocation-free enumerator.</summary>
    /// <returns>The enumerator.</returns>
    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    /// <inheritdoc />
    public bool Equals(CarArray<T> other) => Items.SequenceEqual(other.Items, EqualityComparer<T>.Default);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CarArray<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => "[" + string.Join(", ", Items) + "]";

    /// <summary>Compares two arrays item by item.</summary>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <returns><see langword="true"/> when both arrays contain equal items in the same order.</returns>
    public static bool operator ==(CarArray<T> left, CarArray<T> right) => left.Equals(right);

    /// <summary>Compares two arrays item by item.</summary>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <returns><see langword="true"/> when the arrays differ.</returns>
    public static bool operator !=(CarArray<T> left, CarArray<T> right) => !left.Equals(right);

    /// <summary>Wraps an <see cref="ImmutableArray{T}"/>.</summary>
    /// <param name="items">The items.</param>
    public static implicit operator CarArray<T>(ImmutableArray<T> items) => new(items);

    /// <summary>Unwraps to an <see cref="ImmutableArray{T}"/>.</summary>
    /// <param name="array">The array.</param>
    public static implicit operator ImmutableArray<T>(CarArray<T> array) => array.Items;
}

/// <summary>Factory methods for <see cref="CarArray{T}"/>.</summary>
public static class CarArray
{
    /// <summary>Creates an array from a span; used by collection expressions.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>The new array.</returns>
    public static CarArray<T> Create<T>(ReadOnlySpan<T> items) => new(ImmutableArray.Create(items));

    /// <summary>Creates an array from a sequence.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The items.</param>
    /// <returns>The new array.</returns>
    public static CarArray<T> ToCarArray<T>(this IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source.ToImmutableArray());
    }
}
