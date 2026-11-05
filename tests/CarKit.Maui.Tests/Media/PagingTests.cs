using CarKit.Maui.Media;
using Xunit;

namespace CarKit.Maui.Tests.Media;

public class PagingTests
{
    [Fact]
    public void Pages_slice_and_stop_at_the_end()
    {
        int[] items = [0, 1, 2, 3, 4, 5, 6];

        Assert.Equal([0, 1, 2], CarMediaPaging.Slice(items, 0, 3));
        Assert.Equal([6], CarMediaPaging.Slice(items, 2, 3));
        Assert.Empty(CarMediaPaging.Slice(items, 3, 3));
        Assert.Empty(CarMediaPaging.Slice(items, -1, 3));
        Assert.Empty(CarMediaPaging.Slice(items, 0, 0));
        Assert.Empty(CarMediaPaging.Slice(items, int.MaxValue, int.MaxValue));
    }

    [Fact]
    public void Search_results_are_cached_per_controller_and_query()
    {
        var cache = new CarMediaSearchCache();
        IReadOnlyList<CarMediaItem> jazz = [new("a", "A")];
        cache.Store("auto", "jazz", jazz);
        cache.Store("phone", "jazz", []);

        Assert.True(cache.TryGet("auto", "jazz", out var found));
        Assert.Same(jazz, found);
        Assert.True(cache.TryGet("phone", "jazz", out var other));
        Assert.Empty(other);
        Assert.False(cache.TryGet("auto", "rock", out _));

        for (var i = 0; i < 10; i++)
        {
            cache.Store("auto", "q" + i, []);
        }

        Assert.False(cache.TryGet("auto", "jazz", out _));
        Assert.True(cache.TryGet("auto", "q9", out _));
    }
}
