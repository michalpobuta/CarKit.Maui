using System.Runtime.Versioning;
using CarKit.Maui.Renderers;
using CarPlay;
using CoreGraphics;
using UIKit;

namespace CarKit.Maui.Media;

// Stage 9: CarNowPlayingSports → CPNowPlayingModeSports (iOS 18.4, ref package 26.0; spec: "New platform features
// decisions (Stage 9)"). The mode and its parts are immutable: an image from a URI/stream does not reach the current
// mode, only a reload builds a new mode from the cache. Team logo: an image or initials (CPNowPlayingSportsTeamLogo
// requires one of them).
[SupportedOSPlatform("ios18.4")]
[UnsupportedOSPlatform("maccatalyst")]
internal static class CarPlaySportsMode
{
    // No size API in package 26.0; sizes of the Now Playing screen elements in points
    private static readonly CGSize _logoSize = new(60, 60);
    private static readonly CGSize _indicatorSize = new(20, 20);
    private static readonly CGSize _statusSize = new(30, 30);
    private static readonly CGSize _artworkSize = new(400, 400);

    public static CPNowPlayingModeSports Create(CarNowPlayingSports model, CarPlayTemplateState state, CarPlayRenderContext context, Action reload)
    {
        UIImage? Image(string key, CarImage? image, CGSize size) => context.Image(state, "sports:" + key, image, size, _ => reload());

        var status = model.Status is null && model.StatusImage is null && model.Clock is null
            ? null
            : new CPNowPlayingSportsEventStatus(
                model.Status is { } text ? [.. text.Variants] : null!,
                Image("status", model.StatusImage, _statusSize)!,
                model.Clock is { } clock ? Clock(clock) : null!);
        return new CPNowPlayingModeSports(
            Team(model.LeftTeam, "left", Image),
            Team(model.RightTeam, "right", Image),
            status!,
            Image("background", model.BackgroundArtwork, _artworkSize)!);
    }

    private static CPNowPlayingSportsTeam Team(CarSportsTeam team, string side, Func<string, CarImage?, CGSize, UIImage?> image)
    {
        var logo = image(side + ":logo", team.Logo, _logoSize) is { } loaded
            ? new CPNowPlayingSportsTeamLogo(loaded)
            : new CPNowPlayingSportsTeamLogo(team.Initials);
        // Stage 9 review (I1): eventScore is nonnull in the header and the binding checks it (ArgumentNullException);
        // without a score (e.g. before a match) an empty text; teamStandings and possessionIndicator are nullable
        return new CPNowPlayingSportsTeam(
            team.Name, logo, team.Standings!, team.Score ?? string.Empty, image(side + ":possession", team.PossessionIndicator, _indicatorSize)!, team.IsFavorite);
    }

    private static CPNowPlayingSportsClock Clock(CarSportsClock clock) => new(
        clock.Time.TotalSeconds,
        clock.IsPaused,
        clock.CountsUp
            ? CPNowPlayingSportsClock.CPNowPlayingSportsClockTimeOption.ElapsedTime
            : CPNowPlayingSportsClock.CPNowPlayingSportsClockTimeOption.RemainingTime);
}
