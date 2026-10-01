using System.Text.Json;

namespace T2TouchBar;

internal sealed record CodexState(
    bool Available,
    bool Active,
    string Phase,
    string Label,
    string Detail,
    DateTimeOffset UpdatedAt)
{
    public static readonly CodexState Empty = new(false, false, "idle", "", "", DateTimeOffset.MinValue);
}

internal sealed class CodexStateService
{
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TonyTouchBar", "integrations", "codex-state.json");
    private DateTime lastWriteUtc;
    private CodexState cached = CodexState.Empty;

    public CodexState Read()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return CodexState.Empty;
            if (info.LastWriteTimeUtc != lastWriteUtc)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                var phase = Text(root, "phase", "idle");
                var updated = DateTimeOffset.TryParse(Text(root, "updatedAt", ""), out var parsed) ? parsed : info.LastWriteTimeUtc;
                cached = new CodexState(
                    true,
                    Boolean(root, "active"),
                    phase,
                    Text(root, "label", PhaseLabel(phase)),
                    Text(root, "detail", ""),
                    updated);
                lastWriteUtc = info.LastWriteTimeUtc;
            }

            var age = DateTimeOffset.Now - cached.UpdatedAt;
            if (cached.Phase == "complete" && age > TimeSpan.FromSeconds(8))
                return cached with { Active = false, Phase = "idle", Label = "Codex ready" };
            if (cached.Active && age > TimeSpan.FromMinutes(15))
                return cached with { Active = false, Phase = "idle", Label = "Codex disconnected" };
            return cached;
        }
        catch { return CodexState.Empty; }
    }

    private static string Text(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? fallback : fallback;

    private static bool Boolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var item) && item.ValueKind is JsonValueKind.True;

    private static string PhaseLabel(string phase) => phase switch
    {
        "thinking" => "Codex is thinking",
        "working" => "Codex is working",
        "approval" => "Codex needs approval",
        "complete" => "Codex finished",
        "interrupted" => "Codex interrupted",
        _ => "Codex ready"
    };
}
