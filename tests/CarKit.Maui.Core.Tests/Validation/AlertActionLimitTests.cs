using Xunit;

namespace CarKit.Maui.Core.Tests.Validation;

// Stage 10: CPAlertTemplate.maximumActionCount depends on the entitlement (carplay-audio: 1, other categories: 3; probe in
// the ledger), and with an excess CarPlay keeps the LAST actions, so without a limit in CarKit an Audio app lost its main
// action (the first one in the model)
public class AlertActionLimitTests
{
    private static CarAlertTemplate Alert() => new("Reserve?")
    {
        Actions = [new CarAction("Reserve"), new CarAction("Cancel") { Style = CarActionStyle.Cancel }],
    };

    private static IEnumerable<string?> Titles(CarTemplate template) =>
        Assert.IsType<CarAlertTemplate>(template).Actions.Select(a => a.Title.Text);

    [Fact]
    public void Audio_alert_keeps_the_primary_action_on_carplay()
    {
        var validator = V.Create(CarAppCategory.Audio);

        Assert.Equal(["Reserve"], Titles(validator.Prepare(Alert(), 1)));
    }

    [Fact]
    public void Audio_limit_applies_to_mixed_categories()
    {
        var validator = V.Create(CarAppCategory.Audio | CarAppCategory.Navigation);

        Assert.Equal(["Reserve"], Titles(validator.Prepare(Alert(), 1)));
    }

    [Fact]
    public void Other_categories_keep_both_actions()
    {
        var validator = V.Create(CarAppCategory.Parking);

        Assert.Equal(["Reserve", "Cancel"], Titles(validator.Prepare(Alert(), 1)));
    }

    [Theory]
    [InlineData(CarAppCategory.Parking, 1, new[] { "Reserve" })]
    [InlineData(CarAppCategory.Audio, 3, new[] { "Reserve", "Cancel" })]
    public void Runtime_limit_wins_over_the_rules(CarAppCategory category, int limit, string[] expected)
    {
        var validator = V.Create(category, runtimeLimits: kind => kind == CarLimitKind.Actions ? limit : null);

        Assert.Equal(expected, Titles(validator.Prepare(Alert(), 1)));
    }

    [Fact]
    public void Runtime_limit_does_not_change_other_templates()
    {
        var validator = V.Create(CarAppCategory.Parking, runtimeLimits: kind => kind == CarLimitKind.Actions ? 1 : null);
        var information = new CarInformationTemplate("I") { Actions = [new CarAction("1"), new CarAction("2")] };

        Assert.Equal(2, Assert.IsType<CarInformationTemplate>(validator.Prepare(information, 1)).Actions.Count);
    }

    [Fact]
    public void Overflow_throws_with_the_platform_limit()
    {
        var validator = V.Create(CarAppCategory.Audio, configure: o => o.Limits.OnOverflow = CarOverflowBehavior.Throw);

        var ex = Assert.Throws<CarTemplateLimitException>(() => validator.Prepare(Alert(), 1));

        Assert.Equal((CarLimitKind.Actions, 1, 2), (ex.Limit, ex.Maximum, ex.Actual));
    }
}
