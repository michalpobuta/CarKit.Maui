namespace CarKit.Maui;

/// <summary>Passed to a selection handler. Handlers run on the main thread.</summary>
public class CarItemContext
{
    internal CarItemContext(ICarSession session, CarElement element, CancellationToken cancellationToken)
    {
        Session = session;
        Element = element;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the session the element belongs to.</summary>
    public ICarSession Session { get; }

    /// <summary>Gets the selected element (or the template for template-level handlers).</summary>
    public CarElement Element { get; }

    /// <summary>Gets a token cancelled when the car disconnects.</summary>
    public CancellationToken CancellationToken { get; }
}

/// <summary>Passed to <see cref="CarListItemToggle.OnToggled"/>.</summary>
public sealed class CarToggleContext : CarItemContext
{
    internal CarToggleContext(ICarSession session, CarElement element, bool isChecked, CancellationToken cancellationToken)
        : base(session, element, cancellationToken) => IsChecked = isChecked;

    /// <summary>Gets the new toggle state.</summary>
    public bool IsChecked { get; }
}

/// <summary>Passed to <see cref="CarListImageRowItem.OnImageSelected"/>.</summary>
public sealed class CarImageRowContext : CarItemContext
{
    internal CarImageRowContext(ICarSession session, CarElement element, int imageIndex, CancellationToken cancellationToken)
        : base(session, element, cancellationToken) => ImageIndex = imageIndex;

    /// <summary>Gets the index of the selected image.</summary>
    public int ImageIndex { get; }
}

/// <summary>Passed to the search handlers of <see cref="CarSearchTemplate"/>.</summary>
public sealed class CarSearchContext : CarItemContext
{
    internal CarSearchContext(ICarSession session, CarElement element, string text, CancellationToken cancellationToken)
        : base(session, element, cancellationToken) => Text = text;

    /// <summary>Gets the search text.</summary>
    public string Text { get; }
}

/// <summary>Passed to <see cref="AndroidAutoInputSignIn.OnInputSubmitted"/> and <see cref="CarConversationItem.OnReply"/>.</summary>
public sealed class CarTextInputContext : CarItemContext
{
    internal CarTextInputContext(ICarSession session, CarElement element, string text, CancellationToken cancellationToken)
        : base(session, element, cancellationToken) => Text = text;

    /// <summary>Gets the entered text.</summary>
    public string Text { get; }
}

/// <summary>Passed to <see cref="CarPointOfInterestTemplate.OnMapRegionChanged"/>.</summary>
public sealed class CarMapRegionContext : CarItemContext
{
    internal CarMapRegionContext(ICarSession session, CarElement element, CarMapRegion region, CancellationToken cancellationToken)
        : base(session, element, cancellationToken) => Region = region;

    /// <summary>Gets the region the car now shows.</summary>
    public CarMapRegion Region { get; }
}

/// <summary>Passed to <see cref="CarTabTemplate.OnTabSelected"/>.</summary>
public sealed class CarTabContext : CarItemContext
{
    internal CarTabContext(ICarSession session, CarElement element, CarTemplate selectedTab, CancellationToken cancellationToken)
        : base(session, element, cancellationToken) => SelectedTab = selectedTab;

    /// <summary>Gets the selected tab.</summary>
    public CarTemplate SelectedTab { get; }
}
