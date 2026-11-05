namespace CarKit.Maui;

/// <summary>Text shown in the car, with optional shorter variants for narrow layouts.</summary>
public sealed record CarText
{
    /// <summary>Creates a text with a single variant.</summary>
    /// <param name="text">The text.</param>
    public CarText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Variants = [text];
    }

    /// <summary>Creates a text with variants ordered from the longest to the shortest.</summary>
    /// <param name="variants">The variants; at least one is required.</param>
    public CarText(IEnumerable<string> variants)
    {
        var array = variants?.ToCarArray() ?? throw new ArgumentNullException(nameof(variants));
        if (array.IsEmpty)
        {
            throw new ArgumentException("At least one text variant is required.", nameof(variants));
        }

        Variants = array;
    }

    /// <summary>Gets the variants, the preferred (longest) one first.</summary>
    public CarArray<string> Variants { get; }

    /// <summary>Gets the preferred variant.</summary>
    public string Text => Variants[0];

    /// <summary>Creates a single-variant text.</summary>
    /// <param name="text">The text.</param>
    public static implicit operator CarText(string text) => new(text);

    /// <summary>Creates a single-variant text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text.</returns>
    public static CarText FromString(string text) => new(text);

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>An sRGB color with alpha.</summary>
/// <param name="Red">The red component.</param>
/// <param name="Green">The green component.</param>
/// <param name="Blue">The blue component.</param>
/// <param name="Alpha">The alpha component; 255 is opaque.</param>
public readonly record struct CarColor(byte Red, byte Green, byte Blue, byte Alpha = 255);

/// <summary>A geographic coordinate in degrees (WGS 84).</summary>
public readonly record struct CarLocation
{
    /// <summary>Creates a coordinate.</summary>
    /// <param name="latitude">Latitude from -90 to 90.</param>
    /// <param name="longitude">Longitude from -180 to 180.</param>
    public CarLocation(double latitude, double longitude)
    {
        if (latitude is not (>= -90 and <= 90))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be between -90 and 90.");
        }

        if (longitude is not (>= -180 and <= 180))
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be between -180 and 180.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Gets the latitude.</summary>
    public double Latitude { get; }

    /// <summary>Gets the longitude.</summary>
    public double Longitude { get; }
}

/// <summary>Display unit of a <see cref="CarDistance"/>.</summary>
public enum CarDistanceUnit
{
    /// <summary>Meters.</summary>
    Meters,

    /// <summary>Kilometers.</summary>
    Kilometers,

    /// <summary>Feet.</summary>
    Feet,

    /// <summary>Yards.</summary>
    Yards,

    /// <summary>Miles.</summary>
    Miles,
}

/// <summary>A distance as the car shows it, for example 1.2 km.</summary>
public readonly record struct CarDistance
{
    /// <summary>Creates a distance.</summary>
    /// <param name="value">The value in <paramref name="unit"/>; finite and not negative.</param>
    /// <param name="unit">The display unit.</param>
    public CarDistance(double value, CarDistanceUnit unit)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Distance must be a finite value of at least 0.");
        }

        Value = value;
        Unit = unit;
    }

    /// <summary>Gets the value in <see cref="Unit"/>.</summary>
    public double Value { get; }

    /// <summary>Gets the display unit.</summary>
    public CarDistanceUnit Unit { get; }
}

/// <summary>The part of the map the car shows: a center and a span in degrees.</summary>
public readonly record struct CarMapRegion
{
    /// <summary>Creates a region.</summary>
    /// <param name="center">The center.</param>
    /// <param name="latitudeDelta">The north-south span in degrees; finite and not negative.</param>
    /// <param name="longitudeDelta">The east-west span in degrees; finite and not negative.</param>
    public CarMapRegion(CarLocation center, double latitudeDelta, double longitudeDelta)
    {
        if (!double.IsFinite(latitudeDelta) || latitudeDelta < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(latitudeDelta), latitudeDelta, "Latitude span must be a finite value of at least 0.");
        }

        if (!double.IsFinite(longitudeDelta) || longitudeDelta < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(longitudeDelta), longitudeDelta, "Longitude span must be a finite value of at least 0.");
        }

        Center = center;
        LatitudeDelta = latitudeDelta;
        LongitudeDelta = longitudeDelta;
    }

    /// <summary>Gets the center.</summary>
    public CarLocation Center { get; }

    /// <summary>Gets the north-south span in degrees.</summary>
    public double LatitudeDelta { get; }

    /// <summary>Gets the east-west span in degrees.</summary>
    public double LongitudeDelta { get; }
}

/// <summary>Base of every model object that renderers track by identity.</summary>
public abstract record CarElement
{
    private protected CarElement()
    {
    }

    /// <summary>
    /// Gets the stable identifier. Defaults to a new GUID; keep it when updating a model with a
    /// <see langword="with"/> expression so renderers can match the old and new object.
    /// </summary>
    public string Id
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    } = Guid.NewGuid().ToString("N");
}
