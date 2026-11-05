namespace CarKit.Maui.Media;

/// <summary>
/// The app's media library: CarKit shows it in the car as a browsable tree (CarPlay auto-browse, Android Auto through
/// the Media3 <c>MediaLibraryService</c>), with paging and search.
/// </summary>
/// <remarks>CarKit calls these methods on the main thread; they may complete on any thread.</remarks>
public interface ICarMediaLibrary
{
    /// <summary>Returns the root item; its children are the top level of the library.</summary>
    /// <param name="ct">Cancelled when the car no longer needs the result.</param>
    /// <returns>The root item.</returns>
    Task<CarMediaItem> GetRootAsync(CancellationToken ct);

    /// <summary>Returns one page of the children of an item.</summary>
    /// <param name="parentId">The <see cref="CarMediaItem.Id"/> of the browsable item.</param>
    /// <param name="page">The zero-based page index.</param>
    /// <param name="pageSize">The maximum number of items on the page; <see cref="int.MaxValue"/> when the car asks for all children at once.</param>
    /// <param name="ct">Cancelled when the car no longer needs the result.</param>
    /// <returns>At most <paramref name="pageSize"/> items; fewer items mean the last page.</returns>
    Task<IReadOnlyList<CarMediaItem>> GetChildrenAsync(string parentId, int page, int pageSize, CancellationToken ct);

    /// <summary>Searches the library.</summary>
    /// <param name="query">The text the driver entered or said.</param>
    /// <param name="ct">Cancelled when the car no longer needs the result.</param>
    /// <returns>The matching items.</returns>
    Task<IReadOnlyList<CarMediaItem>> SearchAsync(string query, CancellationToken ct);

    /// <summary>Returns an item by its identifier.</summary>
    /// <param name="id">The <see cref="CarMediaItem.Id"/>.</param>
    /// <param name="ct">Cancelled when the car no longer needs the result.</param>
    /// <returns>The item, or <see langword="null"/> when the library has no such item.</returns>
    Task<CarMediaItem?> GetItemAsync(string id, CancellationToken ct);
}
