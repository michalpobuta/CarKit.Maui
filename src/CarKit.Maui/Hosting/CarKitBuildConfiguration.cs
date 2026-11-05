namespace CarKit.Maui;

// Values from MSBuild (buildTransitive/CarKit.Maui.targets) passed through RuntimeHostConfigurationOption
internal static class CarKitBuildConfiguration
{
    public const string CategoriesSwitchName = "CarKit.Maui.Categories";

    private static readonly char[] _separators = [';', ','];

    // null = the project did not declare categories (e.g. no CarKit targets in the build)
    public static CarAppCategory? Categories => Parse(AppContext.GetData(CategoriesSwitchName) as string);

    public static CarAppCategory? Parse(string? value)
    {
        var names = (value ?? string.Empty).Split(_separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
        {
            return null;
        }

        var categories = CarAppCategory.None;
        foreach (var name in names)
        {
            // Single flag names only; numbers and None are an error, as in MSBuild (CARKIT002)
            if (!Enum.TryParse<CarAppCategory>(name, ignoreCase: true, out var category)
                || category == CarAppCategory.None
                || !Enum.IsDefined(category)
                || !char.IsLetter(name[0]))
            {
                throw new InvalidOperationException($"Unknown CarKit category '{name}' in {CategoriesSwitchName}.");
            }

            categories |= category;
        }

        return categories;
    }
}
