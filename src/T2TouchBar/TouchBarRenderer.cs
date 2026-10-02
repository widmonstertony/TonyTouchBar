using System.Drawing;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace T2TouchBar;

internal sealed class TouchBarRenderer : IDisposable
{
    private sealed record Hit(SKRect Rect, Action<TouchEvent, double> Action);
    private readonly object sync = new();
    private readonly List<Hit> hits = [];
    private readonly Dictionary<string, SKBitmap?> iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SKTypeface regular = SKTypeface.FromFamilyName("Microsoft YaHei UI", SKFontStyle.Normal);
    private readonly SKTypeface bold = SKTypeface.FromFamilyName("Microsoft YaHei UI", SKFontStyle.Bold);
    private int width = 2008;
    private int height = 60;

    public byte[] Render(ForegroundApp app, AppProfile? profile, MediaState media, MediaService mediaService, CodexState codex,
        ForzaTelemetryState forza, bool fnPressed)
    {
        var pixels = new byte[checked(width * height * 4)];
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var surface = SKSurface.Create(info, pinned.AddrOfPinnedObject(), width * 4);
            var canvas = surface.Canvas;
            canvas.Clear(SKColor.Parse(profile is null ? "#05070A" : "#09070D"));
            lock (sync) hits.Clear();

            if (fnPressed)
                DrawFn(canvas);
            else if (profile is not null)
            {
                if (profile.UsesForzaDashboard)
                    DrawForza(canvas, app, profile, forza);
                else
                    DrawProfile(canvas, app, profile);
            }
            else if (media.Available && IsMediaForeground(app.Executable))
                DrawMedia(canvas, app, media, mediaService);
            else if (codex.Available && (codex.Active || IsCodexForeground(app.Executable)))
                DrawCodex(canvas, IsCodexForeground(app.Executable) ? app : ForegroundApp.Empty, codex);
            else
                DrawDefault(canvas, app);

            surface.Flush();
        }
        finally { pinned.Free(); }
        return pixels;
    }

    private void DrawCodex(SKCanvas canvas, ForegroundApp app, CodexState state)
    {
        var accent = state.Phase switch
        {
            "approval" => new SKColor(226, 151, 44),
            "complete" => new SKColor(46, 160, 112),
            "interrupted" => new SKColor(174, 66, 73),
            _ => new SKColor(35, 118, 105)
        };
        DrawIdentity(canvas, app, "Codex", accent, 0, 340);

        var stateRect = new SKRect(350, 5, 875, 55);
        using var statePaint = new SKPaint { Color = Dim(accent, .55f), IsAntialias = true };
        canvas.DrawRoundRect(stateRect, 9, 9, statePaint);
        DrawText(canvas, Fit(state.Label, 300, 20, true), 370, 29, 20, SKColors.White, true);
        if (!string.IsNullOrWhiteSpace(state.Detail))
            DrawText(canvas, Fit(state.Detail, 315, 14, false), 370, 47, 14, new SKColor(210, 220, 225), false);

        if (state.Active && state.Phase is "thinking" or "working")
        {
            var track = new SKRect(690, 43, 850, 48);
            using var dim = new SKPaint { Color = Dim(accent, .75f), IsAntialias = true };
            canvas.DrawRoundRect(track, 3, 3, dim);
            var fraction = (DateTimeOffset.Now.ToUnixTimeMilliseconds() % 1800) / 1800f;
            var left = track.Left + (track.Width + 50) * fraction - 50;
            using var bright = new SKPaint { Color = SKColors.White.WithAlpha(220), IsAntialias = true };
            canvas.Save();
            canvas.ClipRect(track);
            canvas.DrawRoundRect(new SKRect(left, track.Top, left + 50, track.Bottom), 3, 3, bright);
            canvas.Restore();
        }

        DrawButton(canvas, 887, 180, "Open Codex", new SKColor(28, 70, 72), _ => Native.FocusProcess("Codex", "ChatGPT"));
        DrawButton(canvas, 1079, 180, "Copy", new SKColor(42, 50, 65), _ => Native.TapKey("CTRL+C"));
        DrawButton(canvas, 1271, 180, "Paste", new SKColor(42, 50, 65), _ => Native.TapKey("CTRL+V"));
        DrawStatus(canvas, 1463);
    }

    public void SetDimensions(int newWidth, int newHeight)
    {
        width = newWidth;
        height = newHeight;
    }

    public void HandleTouch(TouchEvent touch)
    {
        Hit? match;
        lock (sync) match = hits.LastOrDefault(item => item.Rect.Contains(touch.X, touch.Y));
        if (match is null) return;
        var fraction = match.Rect.Width <= 0 ? 0 : (touch.X - match.Rect.Left) / match.Rect.Width;
        match.Action(touch, Math.Clamp(fraction, 0, 1));
    }

    private void DrawProfile(SKCanvas canvas, ForegroundApp app, AppProfile profile)
    {
        var accent = Parse(profile.Accent, new SKColor(91, 42, 134));
        DrawIdentity(canvas, app, profile.Title, accent, 0, 540);
        DrawStatus(canvas, 548);
        var x = 920f;
        foreach (var shortcut in profile.Shortcuts.Take(5))
        {
            var key = shortcut.Key;
            DrawButton(canvas, x, 190, shortcut.Label, Dim(accent, .72f), _ => Native.TapKey(key));
            x += 198;
        }
    }

    private void DrawForza(SKCanvas canvas, ForegroundApp app, AppProfile profile, ForzaTelemetryState telemetry)
    {
        var accent = Parse(profile.Accent, new SKColor(101, 39, 143));
        DrawIdentity(canvas, app, profile.Title, accent, 0, 420);

        var gearRect = new SKRect(430, 5, 550, 55);
        using (var gearPaint = new SKPaint { Color = new SKColor(18, 24, 32), IsAntialias = true })
            canvas.DrawRoundRect(gearRect, 9, 9, gearPaint);
        DrawCentered(canvas, telemetry.Available ? FormatGear(telemetry.Gear) : DateTime.Now.ToString("HH:mm"), gearRect, telemetry.Available ? 36 : 20, SKColors.White, true);

        var speedRect = new SKRect(560, 5, 760, 55);
        using (var speedPaint = new SKPaint { Color = new SKColor(13, 31, 42), IsAntialias = true })
            canvas.DrawRoundRect(speedRect, 9, 9, speedPaint);
        var (percent, charging) = Native.GetBattery();
        var idleBattery = percent < 0 ? "电 --%" : $"{(charging ? "充" : "电")} {percent}%";
        DrawCentered(canvas, telemetry.Available ? $"{telemetry.SpeedKmh:0} km/h" : idleBattery, speedRect, telemetry.Available ? 25 : 20, SKColors.White, true);

        var rpmRect = new SKRect(770, 5, 1328, 55);
        using (var rpmBackground = new SKPaint { Color = new SKColor(18, 22, 30), IsAntialias = true })
            canvas.DrawRoundRect(rpmRect, 9, 9, rpmBackground);
        if (telemetry.Available)
        {
            DrawRpmStrip(canvas, rpmRect, telemetry.CurrentEngineRpm, telemetry.EngineMaxRpm);
            DrawCentered(canvas, $"{telemetry.CurrentEngineRpm:0} RPM", new SKRect(rpmRect.Left, 26, rpmRect.Right, rpmRect.Bottom), 16, SKColors.White, true);
        }
        else
        {
            DrawIdleStrip(canvas, rpmRect);
            DrawCentered(canvas, $"{profile.Title.ToUpperInvariant()}  //  GAME MODE", new SKRect(rpmRect.Left, 25, rpmRect.Right, rpmRect.Bottom), 16, SKColors.White, true);
        }

        var lapRect = new SKRect(1338, 5, 1588, 55);
        using (var lapPaint = new SKPaint { Color = new SKColor(22, 35, 45), IsAntialias = true })
            canvas.DrawRoundRect(lapRect, 9, 9, lapPaint);
        var lap = telemetry.CurrentLapSeconds > 0 ? telemetry.CurrentLapSeconds : telemetry.LastLapSeconds;
        var position = telemetry.RacePosition > 0 ? telemetry.RacePosition.ToString() : "–";
        var lapTitle = telemetry.Available
            ? $"L{telemetry.LapNumber + 1}  P{position}  {FormatLap(lap)}"
            : "READY";
        DrawCentered(canvas, lapTitle, lapRect, 18, SKColors.White, true);

        DrawButton(canvas, 1598, 190, "Screenshot", Dim(accent, .72f), _ => Native.TapKey("F12"));
        var photoKey = profile.Shortcuts.FirstOrDefault(item => item.Label.Contains("Photo", StringComparison.OrdinalIgnoreCase))?.Key ?? "P";
        DrawButton(canvas, 1798, 200, "Photo Mode", new SKColor(97, 39, 99), _ => Native.TapKey(photoKey));
    }

    private void DrawRpmStrip(SKCanvas canvas, SKRect rect, float rpm, float maxRpm)
    {
        const int segments = 14;
        var ratio = maxRpm > 0 ? Math.Clamp(rpm / maxRpm, 0, 1.1f) : 0;
        var flashOff = ratio >= .94f && (DateTimeOffset.Now.ToUnixTimeMilliseconds() / 80) % 2 == 0;
        var gap = 4f;
        var segmentWidth = (rect.Width - 24 - gap * (segments - 1)) / segments;
        for (var index = 0; index < segments; index++)
        {
            var threshold = .52f + index * (.45f / (segments - 1));
            var lit = ratio >= threshold && !flashOff;
            var color = index < 8 ? new SKColor(25, 196, 158) : index < 11 ? new SKColor(241, 183, 53) : new SKColor(235, 68, 78);
            if (!lit) color = new SKColor((byte)(color.Red / 5), (byte)(color.Green / 5), (byte)(color.Blue / 5));
            var left = rect.Left + 12 + index * (segmentWidth + gap);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(left, 10, left + segmentWidth, 24), 3, 3, paint);
        }
    }

    private void DrawIdleStrip(SKCanvas canvas, SKRect rect)
    {
        const int segments = 14;
        var gap = 4f;
        var segmentWidth = (rect.Width - 24 - gap * (segments - 1)) / segments;
        var pulse = (int)(DateTimeOffset.Now.ToUnixTimeMilliseconds() / 180) % segments;
        for (var index = 0; index < segments; index++)
        {
            var distance = Math.Min(Math.Abs(index - pulse), segments - Math.Abs(index - pulse));
            var alpha = (byte)Math.Max(42, 210 - distance * 40);
            var color = index < 8 ? new SKColor(34, 188, 181, alpha) : new SKColor(179, 73, 220, alpha);
            var left = rect.Left + 12 + index * (segmentWidth + gap);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(left, 10, left + segmentWidth, 24), 3, 3, paint);
        }
    }

    private void DrawFn(SKCanvas canvas)
    {
        string[] labels = ["亮 −", "亮 +", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "音 −", "音 +"];
        string[] actions = ["BRIGHTNESS_DOWN", "BRIGHTNESS_UP", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "VOLUME_DOWN", "VOLUME_UP"];
        const float gap = 10;
        var itemWidth = (width - gap * (labels.Length - 1)) / labels.Length;
        for (var index = 0; index < labels.Length; index++)
        {
            var action = actions[index];
            var color = index < 2 ? new SKColor(38, 55, 72) : index >= 12 ? new SKColor(48, 45, 61) : new SKColor(43, 40, 54);
            DrawButton(canvas, index * (itemWidth + gap), itemWidth, labels[index], color, _ =>
            {
                if (action == "BRIGHTNESS_DOWN") Native.AdjustBrightness(-5);
                else if (action == "BRIGHTNESS_UP") Native.AdjustBrightness(5);
                else Native.TapKey(action);
            });
        }
    }

    private static string FormatGear(byte gear) => gear switch { 0 => "R", 11 => "N", > 11 => "–", _ => gear.ToString() };
    private static string FormatLap(float seconds)
    {
        if (seconds <= 0) return "--:--.---";
        var value = TimeSpan.FromSeconds(seconds);
        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss\.fff") : value.ToString(@"m\:ss\.fff");
    }

    private void DrawDefault(SKCanvas canvas, ForegroundApp app)
    {
        DrawIdentity(canvas, app, string.IsNullOrWhiteSpace(app.Title) ? "Windows" : app.Title, new SKColor(20, 77, 100), 0, 660);
        DrawStatus(canvas, 670);
        DrawButton(canvas, 1060, 195, "Previous", new SKColor(32, 58, 75), _ => Native.TapKey("MEDIA_PREVIOUS"));
        DrawButton(canvas, 1263, 195, "Play / Pause", new SKColor(31, 91, 76), _ => Native.TapKey("MEDIA_PLAY_PAUSE"));
        DrawButton(canvas, 1466, 195, "Next", new SKColor(32, 58, 75), _ => Native.TapKey("MEDIA_NEXT"));
        DrawButton(canvas, 1669, 105, "Vol -", new SKColor(55, 55, 68), _ => Native.TapKey("VOLUME_DOWN"));
        DrawButton(canvas, 1782, 105, "Mute", new SKColor(77, 50, 58), _ => Native.TapKey("VOLUME_MUTE"));
        DrawButton(canvas, 1895, 105, "Vol +", new SKColor(55, 55, 68), _ => Native.TapKey("VOLUME_UP"));
    }

    private void DrawMedia(SKCanvas canvas, ForegroundApp app, MediaState media, MediaService service)
    {
        DrawIdentity(canvas, app, media.Title, new SKColor(125, 34, 92), 0, 480);
        DrawButton(canvas, 490, 105, "-10s", new SKColor(61, 47, 72), touch => { _ = service.SeekRelativeAsync(TimeSpan.FromSeconds(-10)); });
        DrawButton(canvas, 603, 135, media.Playing ? "Pause" : "Play", new SKColor(33, 104, 84), touch => { _ = service.ToggleAsync(); });
        DrawButton(canvas, 746, 105, "+10s", new SKColor(61, 47, 72), touch => { _ = service.SeekRelativeAsync(TimeSpan.FromSeconds(10)); });

        var track = new SKRect(872, 10, 1662, 50);
        using var trackPaint = new SKPaint { Color = new SKColor(35, 40, 50), IsAntialias = true };
        canvas.DrawRoundRect(track, 12, 12, trackPaint);
        var progress = media.Duration.Ticks > 0 ? Math.Clamp((double)media.Position.Ticks / media.Duration.Ticks, 0, 1) : 0;
        using var fillPaint = new SKPaint { Color = new SKColor(221, 63, 145), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(track.Left, track.Top, track.Left + (float)(track.Width * progress), track.Bottom), 12, 12, fillPaint);
        DrawCentered(canvas, $"{FormatTime(media.Position)} / {FormatTime(media.Duration)}", track, 18, SKColors.White, false);
        AddHit(track, (touch, value) => { if (touch.Kind is TouchKind.Up) _ = service.SeekAsync(value); });

        DrawButton(canvas, 1674, 150, "Mute", new SKColor(75, 48, 57), _ => Native.TapKey("VOLUME_MUTE"));
        DrawStatus(canvas, 1834, compact: true);
    }

    private void DrawIdentity(SKCanvas canvas, ForegroundApp app, string title, SKColor accent, float x, float itemWidth)
    {
        var rect = new SKRect(x, 4, x + itemWidth, height - 4);
        using var background = new SKPaint { Color = Dim(accent, .48f), IsAntialias = true };
        canvas.DrawRoundRect(rect, 10, 10, background);
        var icon = LoadIcon(app.Path);
        var textLeft = x + 18;
        if (icon is not null)
        {
            canvas.DrawBitmap(icon, new SKRect(x + 10, 10, x + 50, 50));
            textLeft = x + 62;
        }
        DrawText(canvas, Fit(title, itemWidth - (textLeft - x) - 12, 22, true), textLeft, 38, 22, SKColors.White, true);
    }

    private void DrawStatus(SKCanvas canvas, float x, bool compact = false)
    {
        var (percent, charging) = Native.GetBattery();
        var battery = percent < 0 ? "Battery --" : charging ? $"充电 {percent}%" : $"电池 {percent}%";
        var width1 = compact ? 82 : 155;
        DrawPill(canvas, new SKRect(x, 5, x + width1, 55), DateTime.Now.ToString("HH:mm"), new SKColor(25, 47, 65), compact ? 17 : 21);
        if (!compact) DrawPill(canvas, new SKRect(x + width1 + 8, 5, x + width1 + 173, 55), battery, new SKColor(25, 47, 65), 19);
    }

    private void DrawButton(SKCanvas canvas, float x, float itemWidth, string label, SKColor color, Action<TouchEvent> action)
    {
        var rect = new SKRect(x, 5, x + itemWidth, 55);
        DrawPill(canvas, rect, label, color, 19);
        AddHit(rect, (touch, _) => { if (touch.Kind == TouchKind.Down) action(touch); });
    }

    private void DrawPill(SKCanvas canvas, SKRect rect, string text, SKColor color, float fontSize)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawRoundRect(rect, 9, 9, paint);
        DrawCentered(canvas, Fit(text, rect.Width - 12, fontSize, false), rect, fontSize, SKColors.White, false);
    }

    private void DrawCentered(SKCanvas canvas, string text, SKRect rect, float size, SKColor color, bool isBold)
    {
        using var paint = TextPaint(size, color, isBold);
        var bounds = new SKRect();
        paint.MeasureText(text, ref bounds);
        canvas.DrawText(text, rect.MidX - bounds.MidX, rect.MidY - bounds.MidY, paint);
    }

    private void DrawText(SKCanvas canvas, string text, float x, float baseline, float size, SKColor color, bool isBold)
    {
        using var paint = TextPaint(size, color, isBold);
        canvas.DrawText(text, x, baseline, paint);
    }

    private SKPaint TextPaint(float size, SKColor color, bool isBold) => new() { Typeface = isBold ? bold : regular, TextSize = size, Color = color, IsAntialias = true };

    private string Fit(string text, float maxWidth, float fontSize, bool isBold)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        using var paint = TextPaint(fontSize, SKColors.White, isBold);
        if (paint.MeasureText(text) <= maxWidth) return text;
        while (text.Length > 1 && paint.MeasureText(text + "...") > maxWidth) text = text[..^1];
        return text + "...";
    }

    private SKBitmap? LoadIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (iconCache.TryGetValue(path, out var cached)) return cached;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            using var bitmap = icon?.ToBitmap();
            using var stream = new MemoryStream();
            bitmap?.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            cached = SKBitmap.Decode(stream);
        }
        catch { cached = null; }
        iconCache[path] = cached;
        return cached;
    }

    private void AddHit(SKRect rect, Action<TouchEvent, double> action) { lock (sync) hits.Add(new Hit(rect, action)); }
    private static bool IsMediaForeground(string executable) => executable.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) || executable.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) || executable.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase) || executable.Contains("bilibili", StringComparison.OrdinalIgnoreCase);
    private static bool IsCodexForeground(string executable) => executable.Contains("codex", StringComparison.OrdinalIgnoreCase) || executable.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
    private static SKColor Parse(string value, SKColor fallback) { try { return SKColor.Parse(value); } catch { return fallback; } }
    private static SKColor Dim(SKColor value, float factor) => new((byte)(value.Red * factor), (byte)(value.Green * factor), (byte)(value.Blue * factor), value.Alpha);

    public void Dispose()
    {
        foreach (var icon in iconCache.Values) icon?.Dispose();
        regular.Dispose();
        bold.Dispose();
    }
}
