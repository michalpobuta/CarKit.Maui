using AndroidX.Car.App.Model;
using NativeAction = AndroidX.Car.App.Model.Action;
using Tab = AndroidX.Car.App.Model.Tab;

namespace CarKit.Maui.Renderers;

// CarTabTemplate → TabTemplate (Car App API 6): a header with the app icon (required), tabs with an icon (without an
// image → the app icon), content of the active tab only. The user's selection switches the tab at once (rebuild =
// refresh), then calls OnTabSelected; a SelectedTabId change in the model wins (CarTabSelection).
internal static class TabTemplateRenderer
{
    private const int TabContentsApi8Level = 8;

    public static CarAppRendered Render(CarTabTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var active = CarTabSelection.Active(screen.RenderedTabs, model, screen.UserSelectedTab);
        screen.RenderedTabs = model;
        var builder = new TabTemplate.Builder(new TabCallback(model, context, screen)).SetHeaderAction(NativeAction.AppIcon!)!;
        if (active is null)
        {
            builder.SetLoading(true);
            CarAppHooks.Apply(model, builder, context);
            return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Tab, null, IsLoading: true));
        }

        var tabs = new List<CarAppTabShape>();
        foreach (var tab in model.Tabs)
        {
            var title = tab.TabInfo?.Title.Text ?? tab.Title?.Text ?? " ";
            var icon = context.Icon(screen, tab.TabInfo?.Image, CarAppImages.SmallPixels) ?? CarAppImages.AppIcon(context.Context);
            builder.AddTab(new Tab.Builder().SetTitle(title.Length > 0 ? title : " ")!.SetIcon(icon)!.SetContentId(tab.Id)!.Build()!);
            tabs.Add(new CarAppTabShape(title, tab.TabInfo?.Image?.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var activeTab = model.Tabs.First(tab => tab.Id == active);
        var content = CarAppTemplateRenderers.Render(activeTab, context, screen.Tab(active));
        builder.SetActiveTabContentId(active);
        builder.SetTabContents(new TabContents.Builder(content.Template, context.ApiLevel >= TabContentsApi8Level).Build()!);
        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Tab, null, IsLoading: false)
        {
            Tabs = [.. tabs],
            ActiveTab = active,
            ActiveContent = content.Shape,
        });
    }

    private sealed class TabCallback(CarTabTemplate model, CarAppRenderContext context, CarAppScreenState screen) : Java.Lang.Object, TabTemplate.ITabCallback
    {
        public void OnTabSelected(string? tabContentId)
        {
            if (model.Tabs.FirstOrDefault(tab => tab.Id == tabContentId) is not { } tab)
            {
                return;
            }

            screen.UserSelectedTab = tab.Id;
            screen.RequestRerender(context.Post);
            context.Run(model.OnTabSelected, new CarTabContext(context.Session, model, tab, context.Session.ConnectionToken), "TabSelected");
        }
    }
}
