using CarKit.Maui.Renderers;
using Xunit;

namespace CarKit.Maui.Tests.Renderers;

public class TaskStepsTests
{
    private readonly object _root = new();
    private readonly object _details = new();

    [Fact]
    public void Push_counts_a_step_and_pop_restores_it()
    {
        var steps = new CarAppTaskSteps();
        steps.ResetTo(_root, CarAppTemplateTypes.List);
        steps.Pushed(_details, CarAppTemplateTypes.List);

        Assert.Equal(2, steps.Total);
        steps.Removed(_details);
        Assert.Equal(1, steps.Total);
    }

    // Stage 3 criterion: a refresh does not use up a task step
    [Fact]
    public void Refresh_does_not_count()
    {
        var steps = new CarAppTaskSteps();
        steps.ResetTo(_root, CarAppTemplateTypes.List);

        steps.Updated(_root, isRefresh: true);
        steps.Updated(_root, isRefresh: true);

        Assert.Equal(1, steps.Total);
        Assert.False(steps.WouldExceed(4));
        Assert.True(steps.WouldExceed(5));
    }

    [Fact]
    public void Non_refresh_update_counts_on_its_screen()
    {
        var steps = new CarAppTaskSteps();
        steps.ResetTo(_root, CarAppTemplateTypes.List);
        steps.Pushed(_details, CarAppTemplateTypes.List);

        steps.Updated(_details, isRefresh: false);

        Assert.Equal(3, steps.Total);
        Assert.Equal(2, steps.StepsOf(_details));
        Assert.Equal(1, steps.StepsOf(_root));
    }

    // Going back returns the steps of the screen that disappeared (also those from updates)
    [Fact]
    public void Removing_a_screen_returns_its_steps()
    {
        var steps = new CarAppTaskSteps();
        steps.ResetTo(_root, CarAppTemplateTypes.List);
        steps.Pushed(_details, CarAppTemplateTypes.List);
        steps.Updated(_details, isRefresh: false);

        steps.Removed(_details);
        steps.Updated(_details, isRefresh: false);

        Assert.Equal(1, steps.Total);
        Assert.Equal(0, steps.StepsOf(_details));
    }

    [Fact]
    public void Reset_starts_from_one()
    {
        var steps = new CarAppTaskSteps();
        steps.ResetTo(_root, CarAppTemplateTypes.List);
        steps.Pushed(_details, CarAppTemplateTypes.List);
        var newRoot = new object();

        steps.ResetTo(newRoot, CarAppTemplateTypes.List);

        Assert.Equal(1, steps.Total);
        Assert.Equal(CarAppTaskSteps.HostLimit, 5);
    }

    // Review Focus 5 / I4 (review): going back to a screen with a different template type is a host error → a new root screen,
    // even without screens above (a pop right before SetRootAsync leaves a pending return on the root)
    [Fact]
    public void Root_type_change_replaces_the_root_screen()
    {
        Assert.Equal(CarAppRootPlan.RefreshRoot, CarAppRootPlans.For(CarAppTemplateTypes.List, CarAppTemplateTypes.List));
        Assert.Equal(CarAppRootPlan.ReplaceRoot, CarAppRootPlans.For(CarAppTemplateTypes.List, CarAppTemplateTypes.Tab));
        Assert.Equal(CarAppRootPlan.ReplaceRoot, CarAppRootPlans.For(CarAppTemplateTypes.Sectioned, CarAppTemplateTypes.Grid));
    }

    // I3 (review): the host shows at most 5 templates, and the fifth must end the task (Pane, Message, LongMessage, …)
    [Fact]
    public void Fifth_template_must_end_the_task()
    {
        Assert.True(CarAppTaskSteps.Allows(4, CarAppTemplateTypes.List));
        Assert.False(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.List));
        Assert.False(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.Sectioned));
        Assert.False(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.Grid));
        Assert.True(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.Pane));
        Assert.True(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.Message));
        Assert.True(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.LongMessage));
        Assert.True(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.SignIn));
        Assert.False(CarAppTaskSteps.Allows(6, CarAppTemplateTypes.Pane));
    }

    // Review Focus 4 (Stage 4): SignInTemplate "does not count against the template quota": a push, a reply to the entered
    // text (UpdateAsync with ErrorMessage) and rebuilds do not use up steps, also when sign-in is the root
    [Fact]
    public void Sign_in_screens_never_use_task_steps()
    {
        var steps = new CarAppTaskSteps();
        steps.ResetTo(_root, CarAppTemplateTypes.List);
        steps.Pushed(_details, CarAppTemplateTypes.SignIn);
        steps.Updated(_details, isRefresh: false);
        steps.Updated(_details, isRefresh: false);
        steps.Updated(_details, isRefresh: false);

        Assert.Equal(1, steps.Total);
        Assert.Equal(0, steps.StepsOf(_details));
        Assert.Equal(0, CarAppTaskSteps.Cost(CarAppTemplateTypes.SignIn));

        steps.ResetTo(_root, CarAppTemplateTypes.SignIn);
        steps.Updated(_root, isRefresh: false);
        Assert.Equal(0, steps.Total);
    }

    // LongMessageTemplate javadoc: also outside the template limit (Stage 3 counted it as a step)
    [Fact]
    public void Long_message_does_not_count_against_the_quota()
    {
        var steps = new CarAppTaskSteps();
        steps.ResetTo(_root, CarAppTemplateTypes.List);
        steps.Pushed(_details, CarAppTemplateTypes.LongMessage);
        steps.Updated(_details, isRefresh: false);

        Assert.Equal(1, steps.Total);
        Assert.Equal(0, CarAppTaskSteps.Cost(CarAppTemplateTypes.LongMessage));
        Assert.Equal(1, CarAppTaskSteps.Cost(CarAppTemplateTypes.Message));
        Assert.Equal(1, CarAppTaskSteps.Cost(CarAppTemplateTypes.Sectioned));
    }

    // Stage 8 / I1 (review): "When a task reaches one of these templates, the host resets the template quota":
    // NavigationTemplate counts as the first step of a new task (preview → guidance → end ×3, then the destination
    // list fits within the limit)
    [Fact]
    public void Navigation_template_resets_the_quota()
    {
        var steps = new CarAppTaskSteps();
        steps.ResetTo(_root, CarAppTemplateTypes.Navigation);
        for (var trip = 0; trip < 3; trip++)
        {
            steps.Pushed(_details, CarAppTemplateTypes.List);
            steps.Removed(_details);
            steps.Updated(_root, isRefresh: false, CarAppTemplateTypes.MapWithContent);
            steps.Updated(_root, isRefresh: false, CarAppTemplateTypes.Navigation);
            Assert.Equal(1, steps.Total);
        }

        steps.Pushed(_details, CarAppTemplateTypes.List);
        Assert.Equal(2, steps.Total);
        Assert.True(CarAppTaskSteps.Allows(steps.Total + 1, CarAppTemplateTypes.List));
    }

    // Pushing a NavigationTemplate also starts a new task: the screens under it no longer use up steps
    [Fact]
    public void Pushed_navigation_template_starts_a_new_task()
    {
        var steps = new CarAppTaskSteps();
        steps.ResetTo(_root, CarAppTemplateTypes.List);
        var list = new object();
        steps.Pushed(list, CarAppTemplateTypes.List);
        steps.Pushed(_details, CarAppTemplateTypes.Navigation);

        Assert.Equal(1, steps.Total);
        Assert.Equal(0, steps.StepsOf(_root));
        steps.Updated(_details, isRefresh: true, CarAppTemplateTypes.Navigation);
        Assert.Equal(1, steps.Total);
    }

    // I2 (review): "when popping back to a screen, an app must send a template that is of the same type as the one last
    // sent by that screen": after going back the screen first sends the previous template, the new type only in the next frame
    [Fact]
    public void Returning_screen_resends_the_last_template_type_first()
    {
        Assert.True(CarAppScreenReturns.ResendLast(CarAppTemplateTypes.Navigation, CarAppTemplateTypes.MapWithContent, returning: true));
        Assert.False(CarAppScreenReturns.ResendLast(CarAppTemplateTypes.Navigation, CarAppTemplateTypes.Navigation, returning: true));
        Assert.False(CarAppScreenReturns.ResendLast(CarAppTemplateTypes.Navigation, CarAppTemplateTypes.MapWithContent, returning: false));
        Assert.False(CarAppScreenReturns.ResendLast(null, CarAppTemplateTypes.MapWithContent, returning: true));
    }

    // Stage 5: the MediaPlaybackTemplate javadoc (1.7) does not exempt it from the limit: a step like any template, it may
    // end the task
    [Fact]
    public void Media_playback_template_costs_a_step_and_can_end_the_task()
    {
        Assert.Equal(1, CarAppTaskSteps.Cost(CarAppTemplateTypes.MediaPlayback));
        Assert.True(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.MediaPlayback));
        Assert.False(CarAppTaskSteps.Allows(6, CarAppTemplateTypes.MediaPlayback));
    }
}

public class TemplateTypesTests
{
    // Car App Library template type derived from the model (without rendering): root plan before a rebuild
    [Fact]
    public void Template_types_follow_the_renderer_choice()
    {
        Assert.Equal(CarAppTemplateTypes.List, CarAppTemplateTypes.Of(new CarListTemplate("A"), useSectionedItemTemplate: false));
        Assert.Equal(CarAppTemplateTypes.Sectioned, CarAppTemplateTypes.Of(new CarListTemplate("A"), useSectionedItemTemplate: true));
        Assert.Equal(CarAppTemplateTypes.Grid, CarAppTemplateTypes.Of(new CarGridTemplate("G"), true));
        Assert.Equal(CarAppTemplateTypes.Tab, CarAppTemplateTypes.Of(new CarTabTemplate([]), true));
        Assert.Equal(CarAppTemplateTypes.Pane, CarAppTemplateTypes.Of(new CarInformationTemplate("I"), true));
        Assert.Equal(CarAppTemplateTypes.Search, CarAppTemplateTypes.Of(new CarSearchTemplate(), true));
        Assert.Equal(CarAppTemplateTypes.Message, CarAppTemplateTypes.Of(new CarMessageTemplate("M"), true));
        Assert.Equal(CarAppTemplateTypes.LongMessage, CarAppTemplateTypes.Of(new CarLongMessageTemplate("L", "x"), true));
        Assert.Equal("MediaPlaybackTemplate", CarAppTemplateTypes.Of(new CarNowPlayingTemplate(), true));
    }

    [Fact]
    public void Sign_in_template_type_follows_the_model() =>
        Assert.Equal("SignInTemplate", CarAppTemplateTypes.Of(new AndroidAutoSignInTemplate("Sign-in", new AndroidAutoPinSignIn("1234")), true));
}
