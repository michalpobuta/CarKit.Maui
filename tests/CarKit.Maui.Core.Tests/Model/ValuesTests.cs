using System.Collections.Immutable;
using Xunit;

namespace CarKit.Maui.Core.Tests.Model;

public class CarArrayTests
{
    [Fact]
    public void Arrays_with_equal_items_are_equal_and_hash_equal()
    {
        CarArray<string> a = ["x", "y"];
        CarArray<string> b = ImmutableArray.Create("x", "y");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Arrays_with_different_items_are_not_equal()
    {
        CarArray<string> a = ["x", "y"];
        CarArray<string> b = ["y", "x"];

        Assert.NotEqual(a, b);
        Assert.True(a != b);
        Assert.False(a.Equals((object)"x"));
    }

    [Fact]
    public void Default_behaves_as_empty()
    {
        CarArray<int> empty = default;

        Assert.True(empty.IsEmpty);
        Assert.Empty(empty);
        Assert.Equal(new CarArray<int>(ImmutableArray<int>.Empty), empty);
        Assert.True(empty.AsImmutableArray().IsEmpty);
        Assert.Equal("[]", empty.ToString());
    }

    [Fact]
    public void Converts_to_and_from_immutable_array()
    {
        ImmutableArray<int> source = [1, 2, 3];
        CarArray<int> array = source;
        ImmutableArray<int> back = array;

        Assert.Equal(source, back);
        Assert.Equal([1, 2, 3], array.ToCarArray());
        Assert.Equal(3, array.Count);
        Assert.Equal(2, array[1]);
    }

    [Fact]
    public void Mutating_helpers_return_new_arrays()
    {
        CarArray<int> array = [1, 2, 3];

        Assert.Equal([1, 9, 3], array.SetItem(1, 9));
        Assert.Equal([1, 2, 3, 4], array.Add(4));
        Assert.Equal([1, 3], array.RemoveAt(1));
        Assert.Equal([0, 1, 2, 3], array.Insert(0, 0));
        Assert.Equal([1, 2, 3], array);
    }

    [Fact]
    public void Enumerates_through_all_interfaces()
    {
        CarArray<int> array = [1, 2];
        var viaGeneric = new List<int>();
        foreach (var item in (IEnumerable<int>)array) viaGeneric.Add(item);
        var viaNonGeneric = new List<object>();
        foreach (var item in (System.Collections.IEnumerable)array) viaNonGeneric.Add(item);

        Assert.Equal([1, 2], viaGeneric);
        Assert.Equal([1, 2], viaNonGeneric.Cast<int>());
        Assert.Equal("[1, 2]", array.ToString());
    }

    [Fact]
    public void Records_holding_arrays_compare_structurally()
    {
        var first = new CarText(["Long title", "Short"]);
        var second = new CarText(new List<string> { "Long title", "Short" });

        Assert.Equal(first, second);
    }
}

public class CarTextTests
{
    [Fact]
    public void Converts_implicitly_from_string()
    {
        CarText text = "Stations";

        Assert.Equal("Stations", text.Text);
        Assert.Equal(["Stations"], text.Variants);
        Assert.Equal("Stations", text.ToString());
    }

    [Fact]
    public void Keeps_variants_in_order_longest_first()
    {
        var text = new CarText(["Nearest stations", "Stations"]);

        Assert.Equal("Nearest stations", text.Text);
        Assert.Equal(2, text.Variants.Count);
    }

    [Fact]
    public void Rejects_empty_variants()
    {
        Assert.Throws<ArgumentException>(() => new CarText(Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() => new CarText((string)null!));
    }
}

public class CarImageTests
{
    [Fact]
    public void Factories_create_matching_variants()
    {
        Assert.Equal("station.png", Assert.IsType<CarFileImage>(CarImage.FromFile("station.png")).Path);
        Assert.Equal(new Uri("https://example.com/a.png"), Assert.IsType<CarUriImage>(CarImage.FromUri(new Uri("https://example.com/a.png"))).Uri);
        Assert.Equal([1, 2, 3], Assert.IsType<CarBytesImage>(CarImage.FromBytes([1, 2, 3])).Data);
        var symbol = Assert.IsType<CarSymbolImage>(CarImage.FromSystemSymbol("car.fill", "directions_car"));
        Assert.Equal("car.fill", symbol.IosSymbolName);
        Assert.Equal("directions_car", symbol.AndroidIconName);
    }

    [Fact]
    public async Task Stream_image_opens_through_delegate()
    {
        var image = Assert.IsType<CarStreamImage>(CarImage.FromStream(_ => Task.FromResult<Stream>(new MemoryStream([7]))));

        await using var stream = await image.OpenAsync(CancellationToken.None);
        Assert.Equal(7, stream.ReadByte());
    }

    [Fact]
    public void Bytes_images_compare_by_content()
    {
        Assert.Equal(CarImage.FromBytes([1, 2]), CarImage.FromBytes([1, 2]));
    }

    [Fact]
    public void Dark_variant_and_tint_are_kept()
    {
        var image = CarImage.FromFile("light.png").WithDarkVariant(CarImage.FromFile("dark.png")) with { Tint = new CarColor(255, 0, 0) };

        Assert.Equal(CarImage.FromFile("dark.png"), image.Dark);
        Assert.Equal(new CarColor(255, 0, 0, 255), image.Tint);
    }

    [Fact]
    public void Factories_validate_arguments()
    {
        Assert.Throws<ArgumentException>(() => CarImage.FromFile(" "));
        Assert.Throws<ArgumentNullException>(() => CarImage.FromUri(null!));
        Assert.Throws<ArgumentNullException>(() => CarImage.FromStream(null!));
        Assert.Throws<ArgumentException>(() => CarImage.FromSystemSymbol(null, null));
    }
}

public class CarLocationTests
{
    [Fact]
    public void Accepts_valid_coordinates()
    {
        var location = new CarLocation(52.2297, 21.0122);

        Assert.Equal(52.2297, location.Latitude);
        Assert.Equal(21.0122, location.Longitude);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    public void Rejects_out_of_range_coordinates(double latitude, double longitude) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarLocation(latitude, longitude));
}

public class CarElementTests
{
    [Fact]
    public void Ids_are_unique_and_survive_with_expressions()
    {
        var first = new CarAction("Navigate");
        var second = new CarAction("Navigate");
        var renamed = first with { Title = "Go" };

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Id, renamed.Id);
        Assert.Equal(32, first.Id.Length);
    }

    [Fact]
    public void Id_can_be_overridden()
    {
        var action = new CarAction("Navigate") { Id = "navigate" };

        Assert.Equal("navigate", action.Id);
    }
}
