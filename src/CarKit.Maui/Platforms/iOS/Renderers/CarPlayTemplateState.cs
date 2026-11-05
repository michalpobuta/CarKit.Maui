using CarPlay;
using Foundation;

namespace CarKit.Maui.Renderers;

// The state of one CarPlay template. Handlers look up an element by Id in the current model at selection time, so an
// in-place update need not replace the handlers (CPAlertAction, CPGridButton and CPTextButton have no handler setters).
// Awaited images per element (key): a late image reaches the element only if it still awaits the same CarImage.
internal sealed class CarPlayTemplateState(CarTemplate model)
{
    private CPTemplate? _native;

    public CPTemplate Native
    {
        get => _native ?? throw new InvalidOperationException("The CarPlay template is not rendered yet.");
        set => _native = value;
    }

    public CarTemplate Model { get; private set; } = model;

    private readonly Dictionary<string, CarImage> _expectedImages = [];

    // The theme the images were set for; after a theme change an update with the same model sets them again
    public CarContentStyle ImageStyle { get; set; }

    // The elements' native objects (CPListItem, CPListImageRowItem) by model Id — updates through setters
    public Dictionary<string, NSObject> Items { get; } = [];

    // Tabs: the states of the child templates in CPTabBarTemplate.Templates order
    public List<CarPlayTemplateState> Children { get; } = [];

    public CarSearchRequests Search { get; } = new();

    // Search results shown by CPSearchTemplate (not part of the template model)
    public Dictionary<string, CarListItem> SearchResults { get; } = [];

    // The CPSearchTemplate and CPTabBarTemplate delegates are weak on the Objective-C side; we keep them here
    public NSObject? Delegate { get; set; }

    public void ExpectImage(string key, CarImage? image)
    {
        if (image is null)
        {
            _expectedImages.Remove(key);
        }
        else
        {
            _expectedImages[key] = image;
        }
    }

    public bool Expects(string key, CarImage image) => _expectedImages.TryGetValue(key, out var expected) && expected.Equals(image);

    public void Commit(CarTemplate model, CarContentStyle imageStyle)
    {
        Model = model;
        ImageStyle = imageStyle;
    }

    public bool Restyled(CarPlayRenderContext context) => ImageStyle != context.Style;

    public T Current<T>(T rendered) where T : CarElement => CarModelWalker.Find(Model, rendered.Id) as T ?? rendered;
}
