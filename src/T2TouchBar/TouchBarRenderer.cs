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

    public byte[] Render(ForegroundApp app, AppProfile? profile, MediaState media, MediaService mediaService)
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

            if (profile is not null)
                DrawProfile(canvas, app, profile);
            else if (media.Available && IsMediaForeground(app.Executable))
                DrawMedia(canvas, app, media, mediaService);
            else
                DrawDefault(canvas, app);

            surface.Flush();
        }
        finally { pinned.Free(); }
        return pixels;
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
