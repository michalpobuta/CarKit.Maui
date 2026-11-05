using CarPlay;
using CoreGraphics;
using UIKit;

namespace CarKit.Maui.Renderers;

// Stage 9: CarAction → CPButton (voice state action buttons and the list header, iOS 26.4). The image and handler
// exist only in the constructor, so a URI/stream image is transparent, and after loading a rebuild follows (new
// buttons from the cache). Choosing calls the handler of the current version of the action through the session and
// does not close the modal.
internal static class CarPlayButtons
{
    public static CPButton[] FromActions(
        IEnumerable<CarAction> actions, int maximum, string key, CGSize size, CarPlayTemplateState state, CarPlayRenderContext context, Action rebuild) =>
        [.. actions.Take(Math.Max(maximum, 0)).Select(action =>
        {
            var image = context.Image(state, $"{key}:{action.Id}", action.Image, size, _ => rebuild());
            return new CPButton(image ?? context.Placeholder(size), _ => context.Select(state, action))
            {
                Title = action.Title.Text,
                Enabled = action.IsEnabled,
            };
        })];
}
