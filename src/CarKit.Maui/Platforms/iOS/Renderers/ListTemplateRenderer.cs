using System.Runtime.Versioning;
using CarPlay;
using CoreGraphics;
using CoreMedia;
using Foundation;
using UIKit;

namespace CarKit.Maui.Renderers;

// CarListTemplate → CPListTemplate (spec: template catalog, "Template decisions (Stage 3)"). In-place update:
// the same structure → CPListItem/CPListImageRowItem/CPMessageListItem setters, a different one → UpdateSections. The
// CarPlay list title is read-only. CarConversationItem → CPMessageListItem (Stage 6): Siri handles the selection with
// the conversation identifier. Stage 9 (asset net10.0-ios26.4, iOS 26.4): DetailsHeader →
// CPListTemplateDetailsHeader (ListHeader with a setter, so in place), row and header Playback →
// CPPlaybackConfiguration; CarPlayVideoBrowseTemplate (iOS 27) = the same list (the list model from the sections and
// header, the renderer state holds the video model).
internal static class ListTemplateRenderer
{
    // No API for the header thumbnail size in package 26.4 (iOS 27: maximumImageSizeForAspectRatio); a large thumbnail
    // in points
    private static readonly CGSize _thumbnailSize = new(240, 240);

    public static CarPlayTemplateState Render(CarListTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        var native = new CPListTemplate(model.Title?.Text ?? string.Empty, Sections(model, state, context));
        state.Native = native;
        ApplyHeader(native, model.DetailsHeader, previous: null, first: true, state, context);
        ApplyTemplate(native, model, previous: null, state, context);
        return state;
    }

#pragma warning disable CARKIT0001 // the iOS 27 video list is experimental
    public static CarPlayTemplateState Render(CarPlayVideoBrowseTemplate model, CarPlayRenderContext context)
    {
        var state = Render(AsList(model), context);
        state.Commit(model, context.Style);
        return state;
    }

    public static void Update(CarPlayTemplateState state, CPListTemplate native, CarPlayVideoBrowseTemplate model, CarPlayRenderContext context) =>
        Update(state, native, AsList(model), context);

    // A list from the video list data (sections, header, bar, tab); the same element instances, so the handlers reach
    // the video model
    public static CarListTemplate AsList(CarPlayVideoBrowseTemplate model) => new(model.Title ?? string.Empty)
    {
        Id = model.Id,
        Sections = model.Sections,
        DetailsHeader = model.DetailsHeader,
        TabInfo = model.TabInfo,
        LeadingBarButtons = model.LeadingBarButtons,
        TrailingBarButtons = model.TrailingBarButtons,
    };

    private static CarListTemplate ListModel(CarTemplate model) =>
        model as CarListTemplate ?? AsList((CarPlayVideoBrowseTemplate)model);
#pragma warning restore CARKIT0001

    public static void Update(CarPlayTemplateState state, CPListTemplate native, CarListTemplate model, CarPlayRenderContext context)
    {
        var previous = ListModel(state.Model);
        if (previous.IsLoading && model.IsLoading)
        {
            // A list that is loading has no native sections (the model's sections wait until loading ends)
        }
        else if (CarPlayUpdatePlanner.HasSameItems(previous, model))
        {
            foreach (var (before, after) in previous.Sections.SelectMany(s => s.Items).Zip(model.Sections.SelectMany(s => s.Items)))
            {
                switch (state.Items[after.Id], before, after)
                {
                    case (CPListItem item, CarListItem old, CarListItem row):
                        ApplyItem(item, row, old, state, context);
                        break;
                    case (CPListImageRowItem item, CarListImageRowItem old, CarListImageRowItem row):
                        ApplyImageRow(item, row, old, state, context);
                        break;
                    case (CPMessageListItem item, CarConversationItem old, CarConversationItem row):
                        ApplyMessageItem(item, row, old, state, context);
                        break;
                }
            }
        }
        else
        {
            state.Items.Clear();
            native.UpdateSections(Sections(model, state, context));
        }

        ApplyHeader(native, model.DetailsHeader, previous.DetailsHeader, first: false, state, context);
        ApplyTemplate(native, model, previous, state, context);
    }

    // Stage 9: the details header (iOS 26.4); the 26.0 asset and iOS < 26.4 — the gate removes it from the model, so
    // nothing to do
    private static void ApplyHeader(CPListTemplate native, CarListDetailsHeader? header, CarListDetailsHeader? previous, bool first, CarPlayTemplateState state, CarPlayRenderContext context)
    {
#if IOS26_4_OR_GREATER
        if (!OperatingSystem.IsIOSVersionAtLeast(26, 4) || (!first && Equals(header, previous) && !state.Restyled(context)) || (first && header is null))
        {
            return;
        }

        native.ListHeader = header is null ? null! : Header(header, state, context);
#endif
    }

#if IOS26_4_OR_GREATER
    [SupportedOSPlatform("ios26.4")]
    [UnsupportedOSPlatform("maccatalyst")]
    private static CPListTemplateDetailsHeader Header(CarListDetailsHeader model, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        CPListTemplateDetailsHeader? header = null;
        var thumbnail = context.Image(state, "header", model.Thumbnail, _thumbnailSize, loaded =>
        {
            if (header is not null)
            {
                header.Thumbnail = new CPThumbnailImage(loaded);
            }
        });
        header = new CPListTemplateDetailsHeader(
            new CPThumbnailImage(thumbnail ?? context.Placeholder(_thumbnailSize)),
            model.Title?.Text!,
            model.Subtitle?.Text!,
            [.. (model.Body?.Variants ?? []).Select(variant => new NSAttributedString(variant))],
            HeaderButtons(model, state, context, () => header))
        {
            AdaptiveBackgroundStyle = model.UsesAdaptiveBackground,
        };
        if (model.Playback is { } playback)
        {
            header.PlaybackConfiguration = Playback(playback);
        }

        return header;
    }

    // Header buttons (max. MaximumActionButtonCount); a late image → buttons from the model's current header
    [SupportedOSPlatform("ios26.4")]
    [UnsupportedOSPlatform("maccatalyst")]
    private static CPButton[] HeaderButtons(CarListDetailsHeader model, CarPlayTemplateState state, CarPlayRenderContext context, Func<CPListTemplateDetailsHeader?> header) =>
        CarPlayButtons.FromActions(
            model.Actions, (int)CPListTemplateDetailsHeader.MaximumActionButtonCount, "header-action", CPListTemplateDetailsHeader.MaximumActionButtonSize, state, context,
            rebuild: () =>
            {
                if (header() is { } native && ListModel(state.Model).DetailsHeader is { } current)
                {
                    native.ActionButtons = HeaderButtons(current, state, context, header);
                }
            });

    // Stage 9: playback state (iOS 26.4) of the row and header; unknown length = 0 (the CPPlaybackConfiguration.h header)
    [SupportedOSPlatform("ios26.4")]
    [UnsupportedOSPlatform("maccatalyst")]
    public static CPPlaybackConfiguration Playback(CarPlaybackConfiguration model) => new(
        model.Presentation switch
        {
            CarPlaybackPresentation.Audio => CPPlaybackPresentation.Audio,
            CarPlaybackPresentation.Video => CPPlaybackPresentation.Video,
            _ => CPPlaybackPresentation.None,
        },
        model.Action switch
        {
            CarPlaybackAction.Play => CPPlaybackAction.Play,
            CarPlaybackAction.Pause => CPPlaybackAction.Pause,
            CarPlaybackAction.Replay => CPPlaybackAction.Replay,
            _ => CPPlaybackAction.None,
        },
        CMTime.FromSeconds(model.Elapsed.TotalSeconds, 1000),
        CMTime.FromSeconds(model.Duration?.TotalSeconds ?? 0, 1000));
#endif

    // A search result: selection is handled by the CPSearchTemplate delegate, so no Handler
    public static CPListItem SearchResult(CarListItem row, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        var item = new CPListItem(row.Text.Text, row.Detail?.Text) { UserInfo = new NSString(row.Id) };
        ApplyItem(item, row, previous: null, state, context, selectable: false);
        return item;
    }

    private static void ApplyTemplate(CPListTemplate native, CarListTemplate model, CarListTemplate? previous, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        native.EmptyViewTitleVariants = [.. model.EmptyViewTitle?.Variants ?? []];
        native.EmptyViewSubtitleVariants = [.. model.EmptyViewSubtitle?.Variants ?? []];
        if (OperatingSystem.IsIOSVersionAtLeast(18, 4))
        {
            // IsLoading on CarPlay: the spinner of an empty list (the sections are empty then)
            native.ShowsSpinnerWhileEmpty = model.IsLoading;
        }

        BarButtonRenderer.Apply(native, model, previous, state, context);
    }

    private static CPListSection[] Sections(CarListTemplate model, CarPlayTemplateState state, CarPlayRenderContext context) =>
        model.IsLoading ? [] : [.. model.Sections.Select(section => Section(section, state, context))];

    private static CPListSection Section(CarSection section, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        ICPListTemplateItem[] items = [.. section.Items.Select(item => Item(item, state, context))];
        return section.Header is null && section.IndexTitle is null
            ? new CPListSection(items)
            : new CPListSection(items, section.Header?.Text ?? string.Empty, section.IndexTitle ?? string.Empty);
    }

    private static ICPListTemplateItem Item(CarListTemplateItem item, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        NSObject native = item switch
        {
            CarListItem row => CreateItem(row, state, context),
            CarListImageRowItem row => CreateImageRow(row, state, context),
            CarConversationItem row => CreateMessageItem(row, state, context),
            _ => throw new NotSupportedException($"CarKit does not render {item.GetType().Name} on CarPlay."),
        };
        state.Items[item.Id] = native;
        return (ICPListTemplateItem)native;
    }

    private static CPListItem CreateItem(CarListItem row, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        var item = new CPListItem(row.Text.Text, row.Detail?.Text) { UserInfo = new NSString(row.Id) };
        ApplyItem(item, row, previous: null, state, context);
        return item;
    }

    private static void ApplyItem(CPListItem item, CarListItem row, CarListItem? previous, CarPlayTemplateState state, CarPlayRenderContext context, bool selectable = true)
    {
        if (previous is not null && previous.Text.Text != row.Text.Text)
        {
            item.SetText(row.Text.Text);
        }

        if (previous is not null && previous.Detail?.Text != row.Detail?.Text)
        {
            item.SetDetailText(row.Detail?.Text!);
        }

        if (previous is null || !Equals(previous.Image, row.Image) || state.Restyled(context))
        {
            var image = context.Image(state, "item:" + row.Id, row.Image, CPListItem.MaximumImageSize, item.SetImage);
            if (previous is not null || image is not null)
            {
                item.SetImage(image!);
            }
        }

        item.AccessoryType = row.Accessory switch
        {
            CarListItemAccessory.Cloud => CPListItemAccessoryType.Cloud,
            CarListItemAccessory.DisclosureIndicator => CPListItemAccessoryType.DisclosureIndicator,
            _ when row.IsBrowsable => CPListItemAccessoryType.DisclosureIndicator,
            _ => CPListItemAccessoryType.None,
        };
        item.IsPlaying = row.IsPlaying;
        item.PlaybackProgress = (nfloat)(row.PlaybackProgress ?? 0);
#if IOS26_4_OR_GREATER
        if (OperatingSystem.IsIOSVersionAtLeast(26, 4) && (previous is null ? row.Playback is not null : !Equals(previous.Playback, row.Playback)))
        {
            // The 26.4 binding rejects null (ArgumentNullException), although the CPPlayableItem protocol has nullable
            // — "none" = None/None
            item.PlaybackConfiguration = Playback(row.Playback ?? new CarPlaybackConfiguration());
        }
#endif
        item.IsExplicitContent = row.IsExplicitContent;
        item.Enabled = row.IsEnabled;
        if (selectable && (previous is null || (previous.OnSelected is null) != (row.OnSelected is null)))
        {
            // CarPlay shows the spinner until the completion is called, i.e. until the current row version's handler
            // finishes
            item.Handler = row.OnSelected is null ? null! : (_, completion) => context.Select(state, row, completion);
        }
    }

    // Conversation: the Identifier type (CarPlay passes the Siri conversation identifier). The leading and trailing
    // configurations are immutable, so a change of image, pinning, unread or mute = a new configuration; text through
    // setters
    private static CPMessageListItem CreateMessageItem(CarConversationItem row, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        CPMessageListItem? item = null;
        var image = MessageImage(row, state, context, () => item);
        item = new CPMessageListItem(row.Id, row.Title.Text, Leading(row, image), Trailing(row), row.DetailText!, row.TrailingText!, CPMessageListItemType.Identifier)
        {
            UserInfo = new NSString(row.Id),
            Enabled = row.IsEnabled,
        };
        return item;
    }

    private static void ApplyMessageItem(CPMessageListItem item, CarConversationItem row, CarConversationItem previous, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        if (previous.Title.Text != row.Title.Text)
        {
            item.Text = row.Title.Text;
        }

        if (previous.DetailText != row.DetailText)
        {
            item.DetailText = row.DetailText!;
        }

        if (previous.TrailingText != row.TrailingText)
        {
            item.TrailingText = row.TrailingText!;
        }

        if (!Equals(previous.Image, row.Image) || state.Restyled(context) || previous.IsPinned != row.IsPinned || previous.IsUnread != row.IsUnread)
        {
            item.LeadingConfiguration = Leading(row, MessageImage(row, state, context, () => item));
        }

        if (previous.IsMuted != row.IsMuted)
        {
            item.TrailingConfiguration = Trailing(row);
        }

        item.Enabled = row.IsEnabled;
    }

    // A URI/stream image after loading: the leading configuration from the row's current model
    private static UIImage? MessageImage(CarConversationItem row, CarPlayTemplateState state, CarPlayRenderContext context, Func<CPMessageListItem?> item) =>
        context.Image(state, "message:" + row.Id, row.Image, CPMessageListItem.MaximumMessageItemImageSize, loaded =>
        {
            if (item() is { } native)
            {
                native.LeadingConfiguration = Leading(state.Current(row), loaded);
            }
        });

    private static CPMessageListItemLeadingConfiguration Leading(CarConversationItem row, UIImage? image) =>
        new(row.IsPinned ? CPMessageLeadingItem.Pin : CPMessageLeadingItem.None, image!, row.IsUnread);

    private static CPMessageListItemTrailingConfiguration Trailing(CarConversationItem row) =>
        new(row.IsMuted ? CPMessageTrailingItem.Mute : CPMessageTrailingItem.None, null!);

    private static CPListImageRowItem CreateImageRow(CarListImageRowItem row, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        var slots = new UIImage[Math.Min(row.Images.Count, (int)CPListImageRowItem.MaximumNumberOfGridImages)];
        CPListImageRowItem? item = null;
        LoadImages(row, slots, state, context, () => SetImages(item, slots));
        item = OperatingSystem.IsIOSVersionAtLeast(26) ? new CPListImageRowItem(row.Text.Text, Elements(slots), false) : NewImageRow(row.Text.Text, slots);
        item.UserInfo = new NSString(row.Id);
        item.Enabled = row.IsEnabled;
        item.Handler = (_, completion) => context.Select(state, row, completion);
        item.ListImageRowHandler = (_, index, completion) =>
        {
            var current = state.Current(row);
            context.Run(current.OnImageSelected, new CarImageRowContext(context.Session, current, (int)index, context.Session.ConnectionToken), "SelectImage", completion);
        };
        return item;
    }

    private static void ApplyImageRow(CPListImageRowItem item, CarListImageRowItem row, CarListImageRowItem previous, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        if (previous.Text.Text != row.Text.Text)
        {
            item.Text = row.Text.Text;
        }

        item.Enabled = row.IsEnabled;
        if (previous.Images != row.Images || state.Restyled(context))
        {
            var slots = new UIImage[Math.Min(row.Images.Count, (int)CPListImageRowItem.MaximumNumberOfGridImages)];
            LoadImages(row, slots, state, context, () => SetImages(item, slots));
            SetImages(item, slots);
        }
    }

    private static void LoadImages(CarListImageRowItem row, UIImage[] slots, CarPlayTemplateState state, CarPlayRenderContext context, Action changed)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            var index = i;
            slots[i] = context.Image(state, $"row:{row.Id}:{index}", row.Images[i], CPListImageRowItem.MaximumImageSize, loaded =>
            {
                slots[index] = loaded;
                changed();
            }) ?? context.Placeholder(CPListImageRowItem.MaximumImageSize);
        }
    }

    private static void SetImages(CPListImageRowItem? item, UIImage[] slots)
    {
        if (item is null)
        {
            return;
        }

        if (OperatingSystem.IsIOSVersionAtLeast(26))
        {
            item.Elements = Elements(slots);
        }
        else
        {
#pragma warning disable CA1422 // UpdateImages is superseded by Elements since iOS 26; this is the path for iOS 15–25
            item.UpdateImages(slots);
#pragma warning restore CA1422
        }
    }

    [SupportedOSPlatform("ios26.0")]
    [UnsupportedOSPlatform("maccatalyst")]
    private static CPListImageRowItemGridElement[] Elements(UIImage[] images)
    {
        var elements = new CPListImageRowItemGridElement[images.Length];
        for (var i = 0; i < images.Length; i++)
        {
            elements[i] = new CPListImageRowItemGridElement(images[i]);
        }

        return elements;
    }

#pragma warning disable CA1422 // The constructor with UIImage[] is superseded by elements since iOS 26; this is the path for iOS 15–25
    private static CPListImageRowItem NewImageRow(string text, UIImage[] images) => new(text, images);
#pragma warning restore CA1422
}
