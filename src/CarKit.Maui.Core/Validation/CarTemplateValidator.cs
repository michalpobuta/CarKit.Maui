using Microsoft.Extensions.Logging;

namespace CarKit.Maui;

// Checks a template before it is sent to the host: support and fallback, categories, tabs, depth, limits
internal sealed class CarTemplateValidator
{
    private const int MaxFallbackChain = 4;

    private readonly CarKitOptions _options;
    private readonly ICarCapabilities _capabilities;
    private readonly CarPlatformRules _rules;
    private readonly ILogger _logger;

    public CarTemplateValidator(CarKitOptions options, ICarCapabilities capabilities, ILogger logger)
    {
        _options = options;
        _capabilities = capabilities;
        _rules = CarPlatformRules.For(capabilities.Platform);
        _logger = logger;
    }

    // resultingDepth: stack depth after the operation; null for modals and updates.
    // depthIfScreen: depth when a modal after the fallback becomes a screen (Android Auto).
    public CarTemplate Prepare(CarTemplate template, int? resultingDepth, int? depthIfScreen = null)
    {
        // Stage 9: after the fallback, the feature gate (like fallbacks — also in Off mode)
        var resolved = CarFeatureGate.Apply(ResolveDeep(template), _capabilities, _logger);
        if (_options.Validation == CarValidationMode.Off)
        {
            return resolved;
        }

        CheckPlatformData(template);

        if ((resolved is CarModalTemplate ? resultingDepth : resultingDepth ?? depthIfScreen) is { } depth)
        {
            CheckDepth(depth);
        }

        if (resultingDepth > 1 && _rules.RootOnly.Contains(resolved.Kind))
        {
            Report(new CarStackException(
                $"{resolved.Kind} can only be the root template on {_capabilities.Platform}; show it with SetRootAsync instead of pushing it."));
        }

        CheckDuplicateIds(resolved);
        return CheckContent(resolved, isTab: false);
    }

    // Fallbacks are needed regardless of the validation mode, also for tab children
    private CarTemplate ResolveDeep(CarTemplate template)
    {
        var resolved = Resolve(template);
        if (resolved is not CarTabTemplate tabs)
        {
            return resolved;
        }

        var children = tabs.Tabs.Select(Resolve).ToCarArray();
        return children == tabs.Tabs ? tabs : tabs with { Tabs = children };
    }

    // Different elements with the same Id break the matching of handlers and native objects
    private void CheckDuplicateIds(CarTemplate template)
    {
        var seen = new Dictionary<string, CarElement>();
        foreach (var element in CarModelWalker.Elements(template).Prepend(template))
        {
            if (!seen.TryAdd(element.Id, element) && !ReferenceEquals(seen[element.Id], element) && !seen[element.Id].Equals(element))
            {
                Report(new CarKitException(
                    $"Id '{element.Id}' is used by different elements ({seen[element.Id].GetType().Name}, {element.GetType().Name}) in {template.Kind}; give each element its own Id."));
            }
        }
    }

    private CarTemplate Resolve(CarTemplate template)
    {
        var candidate = template;
        for (var step = 0; step < MaxFallbackChain; step++)
        {
            if (IsSupported(candidate))
            {
                return ReferenceEquals(candidate, template) ? template : candidate with { Id = template.Id };
            }

            if (_options.UnsupportedTemplateBehavior == UnsupportedTemplateBehavior.Throw)
            {
                break;
            }

            var next = candidate.Fallback ?? CarTemplateFallbacks.Create(candidate);
            if (next is null)
            {
                break;
            }

            candidate = next;
        }

        throw new CarTemplateNotSupportedException(
            $"{_capabilities.Platform} does not support {template.Kind} and no fallback applies.", template.Kind, _capabilities.Platform);
    }

    // The template type and — if the rules know it — its variant (e.g. QR code sign-in from Car App API 4)
    private bool IsSupported(CarTemplate template) =>
        _capabilities.Supports(template.Kind)
        && (CarTemplateVariants.Of(template) is not { } variant
            || !_rules.Templates.TryGetValue(template.Kind, out var rule)
            || !rule.Variants.TryGetValue(variant, out var requirement)
            || requirement.IsSatisfiedBy(_capabilities));

    // Data without which the renderer cannot build the native object (CPVoiceControlTemplate, CPContactMessageButton,
    // CPButton). Checked on the original on both platforms: the voice fallback on Android Auto also shows the active
    // state.
    private void CheckPlatformData(CarTemplate template)
    {
        switch (template)
        {
            case CarVoiceControlTemplate voice:
                CheckVoiceStates(voice);
                break;
            case CarPlayContactTemplate contact:
                CheckContactButtons(contact);
                break;
        }
    }

    private void CheckVoiceStates(CarVoiceControlTemplate voice)
    {
        if (voice.States.IsEmpty)
        {
            Report(new CarKitException("VoiceControl needs at least one state."));
            return;
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in voice.States)
        {
            if (!identifiers.Add(state.Identifier))
            {
                Report(new CarKitException($"VoiceControl state Identifier '{state.Identifier}' is used twice; give each state its own Identifier."));
            }
        }

        if (voice.ActiveStateId is { } active && !identifiers.Contains(active))
        {
            Report(new CarKitException($"VoiceControl ActiveStateId '{active}' is not one of the states."));
        }
    }

    private void CheckContactButtons(CarPlayContactTemplate contact)
    {
        foreach (var button in contact.Buttons)
        {
            if (button.Kind == CarPlayContactButtonKind.Message && string.IsNullOrWhiteSpace(button.PhoneOrEmail))
            {
                Report(new CarKitException($"CarPlayContact Message button '{button.Id}' needs PhoneOrEmail; Siri sends the message to it."));
            }
            else if (button.Kind == CarPlayContactButtonKind.Custom && button.Title is null && button.Image is null)
            {
                Report(new CarKitException($"CarPlayContact Custom button '{button.Id}' needs a Title or an Image."));
            }
        }
    }

    private CarTemplate CheckContent(CarTemplate template, bool isTab)
    {
        if (isTab && !_rules.TabChildren.Contains(template.Kind))
        {
            Report(new CarTemplateNotAllowedException(
                $"{template.Kind} cannot be a tab on {_capabilities.Platform}.", template.Kind, _options.Categories));
        }

        CheckCategory(template.Kind);
        if (template is CarListTemplate list)
        {
            CheckItems(list);
        }

        if (template is CarPointOfInterestTemplate points)
        {
            CheckPoints(points);
        }

        if (template is CarMapTemplate map)
        {
            CheckMap(map);
        }

        if (template is CarTabTemplate tabs)
        {
            var children = tabs.Tabs.Select(tab => CheckContent(tab, isTab: true)).ToCarArray();
            template = children == tabs.Tabs ? tabs : tabs with { Tabs = children };
        }

        return ApplyLimits(template);
    }

    private void CheckCategory(CarTemplateKind kind)
    {
        foreach (var category in DeclaredCategories())
        {
            if (category.Templates.TryGetValue(kind, out var requirement) && requirement.IsSatisfiedBy(_capabilities))
            {
                return;
            }
        }

        Report(new CarTemplateNotAllowedException(
            $"{kind} is not allowed on {_capabilities.Platform} for categories {_options.Categories}.", kind, _options.Categories));
    }

    // Rows with category rules (Stage 6: conversations only in Communication) and data without which the host rejects
    // the row: ConversationItem.build() requires messages, and the host opens the conversation (CarPlay → Siri), so
    // OnSelected will not work
    private void CheckItems(CarListTemplate list)
    {
        foreach (var item in list.Sections.SelectMany(section => section.Items))
        {
            if (CarListItemKinds.Of(item) is { } kind && !DeclaredCategories().Any(category => category.Items.Contains(kind)))
            {
                Report(new CarTemplateNotAllowedException(
                    $"{kind} rows ('{item.Id}') are not allowed on {_capabilities.Platform} for categories {_options.Categories}; declare the Communication category.",
                    list.Kind,
                    _options.Categories));
            }

            if (item is CarConversationItem conversation)
            {
                if (conversation.Messages.IsEmpty)
                {
                    Report(new CarKitException($"Conversation '{conversation.Id}' needs at least one message."));
                }

                if (conversation.OnSelected is not null)
                {
                    Report(new CarKitException(
                        $"Conversation '{conversation.Id}' cannot have OnSelected: the car opens conversations itself (Siri on CarPlay). Use OnReply and OnMarkAsRead."));
                }
            }
        }
    }

    // Stage 7: a selection outside the points (CarPlay SelectedIndex) and a point without a distance where Android Auto
    // shows PlaceListMapTemplate (POI categories) — it rejects rows without a DistanceSpan
    // (ModelUtils.validateAllNonBrowsableRowsHaveDistance); a place list without a map (other categories) does not
    // require distances (final review, M3)
    private void CheckPoints(CarPointOfInterestTemplate template)
    {
        if (template.SelectedPointId is { } selected && !template.Points.Any(point => point.Id == selected))
        {
            Report(new CarKitException($"PointOfInterest SelectedPointId '{selected}' is not one of the points."));
        }

        if (_capabilities.Platform == CarPlatform.CarPlay || !CarPointsOfInterest.UsesPlaceListMap(_options.Categories))
        {
            return;
        }

        foreach (var point in template.Points.Where(point => point.Distance is null))
        {
            Report(new CarKitException(
                $"Point '{point.Id}' needs a Distance on {_capabilities.Platform}: the place list map (PlaceListMapTemplate) rejects places without a distance."));
        }
    }

    // Stage 8: NavigationTemplate.Builder.build() throws "Action strip for this template must be set", and the Android
    // Auto action strip is built from TrailingBarButtons (CarPlay maps do not require them)
    private void CheckMap(CarMapTemplate map)
    {
        if (_capabilities.Platform != CarPlatform.CarPlay && map.TrailingBarButtons.IsEmpty)
        {
            Report(new CarKitException(
                $"Map '{map.Id}' needs at least one of TrailingBarButtons on {_capabilities.Platform}: the navigation template requires an action strip."));
        }
    }

    // Maximum stack depth for the declared categories (modules plan the stack, e.g. the Media module's auto-browse)
    public int MaxDepth => DeclaredCategories().Select(c => c.MaxDepth).DefaultIfEmpty(int.MaxValue).Max();

    private void CheckDepth(int depth)
    {
        var maxDepth = MaxDepth;
        if (depth > maxDepth)
        {
            Report(new CarTemplateLimitException(
                $"Stack depth {depth} exceeds {maxDepth} for categories {_options.Categories}.", CarLimitKind.StackDepth, maxDepth, depth));
        }
    }

    private IEnumerable<CarCategoryRule> DeclaredCategories()
    {
        foreach (var (category, rule) in _rules.Categories)
        {
            if (_options.Categories.HasFlag(category))
            {
                yield return rule;
            }
        }
    }

    private CarTemplate ApplyLimits(CarTemplate template)
    {
        var limited = template switch
        {
            CarListTemplate list => list with { Sections = LimitSections(list.Sections) },
#pragma warning disable CARKIT0001
            CarPlayVideoBrowseTemplate video => video with { Sections = LimitSections(video.Sections) },
#pragma warning restore CARKIT0001
            CarGridTemplate grid => grid with { Items = Limit(grid.Items, CarLimitKind.GridItems) },
            // Stage 9: voice state action buttons (rule maxActions VoiceControl;
            // CPVoiceControlState.MaximumActionButtonCount)
            CarVoiceControlTemplate voice => voice with
            {
                States = voice.States.Select(state => state.Actions.IsEmpty ? state : state with { Actions = LimitActions(state.Actions, voice.Kind) }).ToCarArray(),
            },
            CarPointOfInterestTemplate poi => poi with { Points = Limit(poi.Points, CarLimitKind.PointsOfInterest) },
            CarMapTemplate map => map with { MapButtons = Limit(map.MapButtons, CarLimitKind.MapButtons, MapButtonLimit(map)) },
            CarTabTemplate tabs => tabs with { Tabs = Limit(tabs.Tabs, CarLimitKind.Tabs) },
            CarInformationTemplate info => info with
            {
                Items = Limit(info.Items, CarLimitKind.InformationItems),
                Actions = LimitActions(info.Actions, info.Kind),
            },
            CarMessageTemplate message => message with { Actions = LimitActions(message.Actions, message.Kind) },
            CarAlertTemplate alert => alert with { Actions = LimitActions(alert.Actions, alert.Kind) },
            CarLongMessageTemplate longMessage => longMessage with { Actions = LimitActions(longMessage.Actions, longMessage.Kind) },
            AndroidAutoSignInTemplate signIn => signIn with { Actions = LimitActions(signIn.Actions, signIn.Kind) },
            _ => template,
        };

        // Without changes we return the original, so as not to multiply instances
        return limited.Equals(template) ? template : limited;
    }

    // Android Auto: the pan button (Action.PAN) takes one of the 4 slots of the map bar (ACTIONS_CONSTRAINTS_MAP)
    private int? MapButtonLimit(CarMapTemplate map) =>
        map.IsPanningEnabled && _capabilities.Platform != CarPlatform.CarPlay && _capabilities.GetLimit(CarLimitKind.MapButtons) is { } limit
            ? Math.Max(limit - 1, 0)
            : null;

    private CarArray<CarSection> LimitSections(CarArray<CarSection> sections)
    {
        sections = Limit(sections, CarLimitKind.ListSections);
        var total = sections.Sum(s => s.Items.Count);
        if (_capabilities.GetLimit(CarLimitKind.ListItems) is not { } max || total <= max || !Overflow(CarLimitKind.ListItems, max, total))
        {
            return sections;
        }

        var budget = max;
        var result = new List<CarSection>();
        foreach (var section in sections)
        {
            if (budget == 0)
            {
                break;
            }

            var keep = Math.Min(budget, section.Items.Count);
            result.Add(keep == section.Items.Count ? section : section with { Items = section.Items.Take(keep).ToCarArray() });
            budget -= keep;
        }

        return result.ToCarArray();
    }

    private CarArray<T> LimitActions<T>(CarArray<T> actions, CarTemplateKind kind)
    {
        var max = _rules.Templates.TryGetValue(kind, out var rule) ? rule.MaxActions : null;
        if (kind == CarTemplateKind.Alert)
        {
            max = AlertActionLimit(max);
        }

        return max is { } limit ? Limit(actions, CarLimitKind.Actions, limit) : actions;
    }

    // Stage 10: the limit depends on the entitlement (CarPlay Audio: 1) and the platform provides it; on excess CarPlay
    // keeps the LAST actions, so CarKit truncates here so that the primary action (first in the model) stays. The
    // smallest limit, because one Audio key lowers it for the whole app
    private int? AlertActionLimit(int? rule) =>
        _capabilities.GetLimit(CarLimitKind.Actions) ?? DeclaredCategories().Min(category => category.AlertActions) ?? rule;

    private CarArray<T> Limit<T>(CarArray<T> items, CarLimitKind kind, int? fixedMax = null)
    {
        if ((fixedMax ?? _capabilities.GetLimit(kind)) is not { } max || items.Count <= max || !Overflow(kind, max, items.Count))
        {
            return items;
        }

        return items.Take(max).ToCarArray();
    }

    // true = truncate; false = leave (Warn/Throw already handled by Report)
    private bool Overflow(CarLimitKind kind, int max, int actual)
    {
        if (_options.Limits.OnOverflow == CarOverflowBehavior.Truncate)
        {
            CarKitLog.Truncated(_logger, kind, actual, max);
            return true;
        }

        Report(new CarTemplateLimitException($"{kind}: {actual} exceeds the limit of {max}.", kind, max, actual));
        return false;
    }

    private void Report(CarKitException exception)
    {
        if (_options.Validation == CarValidationMode.Strict)
        {
            throw exception;
        }

        CarKitLog.ValidationWarning(_logger, exception.Message, exception);
    }
}
