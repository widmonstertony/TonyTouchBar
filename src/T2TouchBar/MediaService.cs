using Windows.Media.Control;

namespace T2TouchBar;

internal sealed record MediaState(bool Available, string Title, string Artist, string SourceAppId,
    TimeSpan Position, TimeSpan Duration, bool Playing)
{
    public static readonly MediaState Empty = new(false, "", "", "", TimeSpan.Zero, TimeSpan.Zero, false);
    public bool HasTimeline => Duration > TimeSpan.Zero;
}

internal sealed class MediaService : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private string foregroundProcess = "";
    private string foregroundTitle = "";
    public MediaState State { get; private set; } = MediaState.Empty;

    public void SetForegroundContext(string executable, string title)
    {
        foregroundProcess = Path.GetFileNameWithoutExtension(executable ?? "");
        foregroundTitle = title ?? "";
    }

    public async Task RefreshAsync()
    {
        if (!await gate.WaitAsync(0)) return;
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            session = SelectBestSession(manager);
            if (session is null) { State = MediaState.Empty; return; }
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            var properties = await session.TryGetMediaPropertiesAsync();
            var duration = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;
            if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && timeline.LastUpdatedTime != default)
                position += DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && position > duration) position = duration;
            State = new MediaState(true, properties.Title ?? "", properties.Artist ?? "",
                session.SourceAppUserModelId ?? "", position, duration,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        }
        catch { State = MediaState.Empty; }
        finally { gate.Release(); }
    }

    public async Task ToggleAsync() { try { if (session is not null) await session.TryTogglePlayPauseAsync(); } catch { } }

    public async Task SeekRelativeAsync(TimeSpan delta)
    {
        try
        {
            if (session is null || !State.Available) return;
            if (State.HasTimeline)
            {
                var ticks = Math.Clamp((State.Position + delta).Ticks, 0, State.Duration.Ticks);
                await session.TryChangePlaybackPositionAsync(ticks);
            }
            else
            {
                var count = Math.Max(1, (int)Math.Round(Math.Abs(delta.TotalSeconds) / 5d));
                for (var index = 0; index < count; index++)
                {
                    if (delta < TimeSpan.Zero) await session.TryRewindAsync();
                    else await session.TryFastForwardAsync();
                }
            }
        }
        catch { }
    }

    public async Task SeekAsync(double fraction)
    {
        try
        {
            if (session is null || !State.Available || !State.HasTimeline) return;
            await session.TryChangePlaybackPositionAsync((long)(State.Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        }
        catch { }
    }

    private GlobalSystemMediaTransportControlsSession? SelectBestSession(GlobalSystemMediaTransportControlsSessionManager source)
    {
        var current = source.GetCurrentSession();
        var sessions = source.GetSessions();
        GlobalSystemMediaTransportControlsSession? best = null;
        var bestScore = int.MinValue;
        foreach (var candidate in sessions)
        {
            var appId = candidate.SourceAppUserModelId ?? "";
            var score = ReferenceEquals(candidate, current) ? 20 : 0;
            try
            {
                if (candidate.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    score += 15;
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(foregroundProcess) && appId.Contains(foregroundProcess, StringComparison.OrdinalIgnoreCase)) score += 120;
            if (MatchesAlias(appId, "bilibili", "哔哩哔哩")) score += 110;
            if (MatchesAlias(appId, "cloudmusic", "netease", "网易云")) score += 110;
            if (MatchesAlias(appId, "chrome")) score += 90;
            if (MatchesAlias(appId, "msedge", "edge")) score += 90;
            if (MatchesAlias(appId, "firefox")) score += 90;
            if (score > bestScore) { best = candidate; bestScore = score; }
        }
        return best ?? current;
    }

    private bool MatchesAlias(string source, params string[] aliases)
    {
        var foregroundMatches = aliases.Any(alias => foregroundProcess.Contains(alias, StringComparison.OrdinalIgnoreCase) ||
            foregroundTitle.Contains(alias, StringComparison.OrdinalIgnoreCase));
        return foregroundMatches && aliases.Any(alias => source.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => gate.Dispose();
}
