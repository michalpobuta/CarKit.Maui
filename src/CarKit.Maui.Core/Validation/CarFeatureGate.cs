using Microsoft.Extensions.Logging;

namespace CarKit.Maui;

// Stage 9: feature gate (spec: "New platform features decisions (Stage 9)", Feature gate). Removes from the model sent
// to the host the features that the platform, version or CarKit asset does not support (like fallbacks — in every
// validation mode), so renderers and FakeCarHost get the same thing; the app keeps its own model (ICarSession.Stack).
// Without changes it returns the same instance.
internal static class CarFeatureGate
{
    public static CarTemplate Apply(CarTemplate template, ICarCapabilities capabilities, ILogger logger)
    {
        var gate = new Gate(template, capabilities, logger);
        return gate.Template(template);
    }

    private sealed class Gate(CarTemplate root, ICarCapabilities capabilities, ILogger logger)
    {
        private readonly HashSet<CarFeature> _reported = [];

        public CarTemplate Template(CarTemplate template)
        {
#pragma warning disable CARKIT0001 // the video list, MiniPlayer and the iOS 27 overlay are experimental
            CarTemplate gated = template switch
            {
                CarListTemplate list => list with { DetailsHeader = Header(list.DetailsHeader), Sections = Sections(list.Sections) },
                CarPlayVideoBrowseTemplate video => video with { DetailsHeader = Header(video.DetailsHeader), Sections = Sections(video.Sections) },
                CarTabTemplate tabs => tabs with { Tabs = tabs.Tabs.Select(Template).ToCarArray() },
                CarVoiceControlTemplate voice => voice with
                {
                    States = voice.States.Select(State).ToCarArray(),
                    IsOverlay = voice.IsOverlay && Keep(CarFeature.VoiceControlOverlay),
                },
                CarNowPlayingTemplate nowPlaying => NowPlaying(nowPlaying),
                _ => template,
            };
#pragma warning restore CARKIT0001
            return gated.Equals(template) ? template : gated;
        }

        private CarNowPlayingTemplate NowPlaying(CarNowPlayingTemplate nowPlaying)
        {
#pragma warning disable CARKIT0001
            // MiniPlayer is shown by iOS 27; without the 27.0 asset it cannot be turned off — only a log, the flag
            // stays in the model
            if (!nowPlaying.AllowsMiniPlayer)
            {
                Keep(CarFeature.NowPlayingMiniPlayerOptOut);
            }
#pragma warning restore CARKIT0001

            return nowPlaying.Sports is null || Keep(CarFeature.NowPlayingSportsMode) ? nowPlaying : nowPlaying with { Sports = null };
        }

        private CarVoiceControlState State(CarVoiceControlState state) =>
            state.Actions.IsEmpty || Keep(CarFeature.VoiceControlActionButtons) ? state : state with { Actions = [] };

        private CarListDetailsHeader? Header(CarListDetailsHeader? header)
        {
            if (header is null || !Keep(CarFeature.ListDetailsHeader))
            {
                return null;
            }

            return header.Playback is null || Keep(CarFeature.PlaybackConfiguration) ? header : header with { Playback = null };
        }

        private CarArray<CarSection> Sections(CarArray<CarSection> sections)
        {
            if (!sections.Any(section => section.Items.Any(item => item is CarListItem { Playback: not null })) || Keep(CarFeature.PlaybackConfiguration))
            {
                return sections;
            }

            return sections.Select(section => section with
            {
                Items = section.Items.Select(item => item is CarListItem { Playback: not null } row ? row with { Playback = null } : item).ToCarArray(),
            }).ToCarArray();
        }

        // true = the feature stays; otherwise a log once per feature and the template is prepared
        private bool Keep(CarFeature feature)
        {
            if (capabilities.Supports(feature))
            {
                return true;
            }

            if (_reported.Add(feature))
            {
                var version = capabilities.Platform == CarPlatform.CarPlay
                    ? "iOS " + capabilities.IosVersion
                    : "Car App API level " + capabilities.CarAppApiLevel?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                CarKitLog.FeatureUnavailable(logger, root.Kind, root.Id, feature, capabilities.Platform, version);
            }

            return false;
        }
    }
}
