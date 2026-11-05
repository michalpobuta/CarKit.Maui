namespace CarKit.Maui.Renderers;

// Where MAUI puts app images (MauiImage): Android: a drawable named after the file without the extension, in
// lowercase; iOS: a file in the bundle by name, and SVG is converted to PNG with the same name
internal static class CarImageNames
{
    public static string AndroidResourceName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        return string.Create(name.Length, name, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = char.ToLowerInvariant(source[i]);
                span[i] = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' ? c : '_';
            }
        });
    }

    public static IReadOnlyList<string> IosBundleCandidates(string path)
    {
        var normalized = path.Replace('\\', '/');
        var file = Path.GetFileName(normalized);
        IEnumerable<string> names = file == normalized ? [normalized] : [normalized, file];
        if (string.Equals(Path.GetExtension(normalized), ".svg", StringComparison.OrdinalIgnoreCase))
        {
            names = names.Select(name => Path.ChangeExtension(name, ".png"));
        }

        return [.. names.Distinct(StringComparer.Ordinal)];
    }
}
