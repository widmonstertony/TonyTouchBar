using Windows.Media.Control;

namespace T2TouchBar;

internal sealed record MediaState(bool Available, string Title, string Artist, TimeSpan Position, TimeSpan Duration, bool Playing)
{
    public static readonly MediaState Empty = new(false, "", "", TimeSpan.Zero, TimeSpan.Zero, false);
}

internal sealed class MediaService : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    public MediaState State { get; private set; } = MediaState.Empty;

    public async Task RefreshAsync()
    {
        if (!await gate.WaitAsync(0)) return;
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            session = manager.GetCurrentSession();
            if (session is null) { State = MediaState.Empty; return; }
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            var properties = await session.TryGetMediaPropertiesAsync();
            var duration = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;
            if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && timeline.LastUpdatedTime != default)
                position += DateTimeOffset.Now - timeline.LastUpdatedTime;
            position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, Math.Max(0, duration.Ticks)));
            State = new MediaState(duration > TimeSpan.Zero, properties.Title ?? "", properties.Artist ?? "", position, duration,
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
            var ticks = Math.Clamp((State.Position + delta).Ticks, 0, State.Duration.Ticks);
            await session.TryChangePlaybackPositionAsync(ticks);
        }
        catch { }
    }

    public async Task SeekAsync(double fraction)
    {
        try
        {
            if (session is null || !State.Available) return;
            await session.TryChangePlaybackPositionAsync((long)(State.Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        }
        catch { }
    }

    public void Dispose() => gate.Dispose();
}
