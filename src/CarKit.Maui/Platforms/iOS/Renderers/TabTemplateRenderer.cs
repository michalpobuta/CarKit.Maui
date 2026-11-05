using CarPlay;
using Foundation;

namespace CarKit.Maui.Renderers;

// CarTabTemplate → CPTabBarTemplate (root only: the rootOnly rule). Children with the same Id and type are updated in
// place, the tab list through UpdateTemplates; TabInfo → TabTitle, TabImage, ShowsTabBadge
internal static class TabTemplateRenderer
{
    public static CarPlayTemplateState Render(CarTabTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        state.Children.AddRange(model.Tabs.Select(tab => Child(tab, state, context)));
        var native = new CPTabBarTemplate([.. state.Children.Select(child => child.Native)]);
        var tabDelegate = new TabDelegate(state, context);
        native.Delegate = tabDelegate;
        state.Delegate = tabDelegate;
        state.Native = native;
        Select(native, state, model.SelectedTabId);
        return state;
    }

    public static void Update(CarPlayTemplateState state, CPTabBarTemplate native, CarTabTemplate model, CarPlayRenderContext context)
    {
        var previous = (CarTabTemplate)state.Model;
        var existing = state.Children.ToDictionary(child => child.Model.Id);
        var children = model.Tabs.Select(tab =>
        {
            // TabInfo before the child's hooks: the ConfigureCarPlay hook may override TabTitle/TabImage (Stage 4)
            if (existing.TryGetValue(tab.Id, out var child)
                && CarPlayTemplateRenderers.Update(child, tab, context, beforeHooks: updated => ApplyTabInfo(updated, tab, context)) == CarPlayUpdate.InPlace)
            {
                return child;
            }

            return Child(tab, state, context);
        }).ToList();

        var changed = children.Count != state.Children.Count || children.Where((child, i) => !ReferenceEquals(child, state.Children[i])).Any();
        state.Children.Clear();
        state.Children.AddRange(children);
        if (changed)
        {
            native.UpdateTemplates([.. children.Select(child => child.Native)]);
        }

        if (previous.SelectedTabId != model.SelectedTabId)
        {
            Select(native, state, model.SelectedTabId);
        }
    }

    private static CarPlayTemplateState Child(CarTemplate tab, CarPlayTemplateState parent, CarPlayRenderContext context) =>
        CarPlayTemplateRenderers.Render(tab, context, beforeHooks: child => ApplyTabInfo(child, tab, context));

    private static void ApplyTabInfo(CarPlayTemplateState child, CarTemplate tab, CarPlayRenderContext context)
    {
        var native = child.Native;
        native.TabTitle = tab.TabInfo?.Title.Text ?? tab.Title?.Text ?? string.Empty;
        native.ShowsTabBadge = tab.TabInfo?.Badge > 0;
        var image = context.Image(child, "tab:" + tab.Id, tab.TabInfo?.Image, CarPlayImages.TabSize, loaded => native.TabImage = loaded);
        if (image is not null || tab.TabInfo?.Image is null)
        {
            native.TabImage = image!;
        }
    }

    // SelectTemplate since iOS 17; earlier CarPlay shows the first tab
    private static void Select(CPTabBarTemplate native, CarPlayTemplateState state, string? tabId)
    {
        var index = state.Children.FindIndex(child => child.Model.Id == tabId);
        if (index >= 0 && OperatingSystem.IsIOSVersionAtLeast(17))
        {
            native.SelectTemplate(index);
        }
    }

    private sealed class TabDelegate(CarPlayTemplateState state, CarPlayRenderContext context) : CPTabBarTemplateDelegate
    {
        public override void DidSelectTemplate(CPTabBarTemplate tabBarTemplate, CPTemplate selectedTemplate)
        {
            var model = (CarTabTemplate)state.Model;
            var id = (selectedTemplate.UserInfo as NSString)?.ToString();
            if (model.Tabs.FirstOrDefault(tab => tab.Id == id) is { } tab)
            {
                context.Run(model.OnTabSelected, new CarTabContext(context.Session, model, tab, context.Session.ConnectionToken), "TabSelected");
            }
        }
    }
}
