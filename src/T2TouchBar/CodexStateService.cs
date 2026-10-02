using System.Text.Json;
using Microsoft.Data.Sqlite;

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
    private readonly string codexHome = ResolveCodexHome();

    public CodexState Read()
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.LastWriteTimeUtc != lastWriteUtc)
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
            if ((!info.Exists || age > TimeSpan.FromSeconds(10)) && TryReadTurnHistory(out var live))
                return live;
            if (cached.Phase == "complete" && age > TimeSpan.FromSeconds(8))
                return cached with { Active = false, Phase = "idle", Label = "Codex ready" };
            if (cached.Active && age > TimeSpan.FromMinutes(15))
                return cached with { Active = false, Phase = "idle", Label = "Codex disconnected" };
            return cached;
        }
        catch { return TryReadTurnHistory(out var live) ? live : CodexState.Empty; }
    }

    private bool TryReadTurnHistory(out CodexState state)
    {
        state = CodexState.Empty;
        try
        {
            var database = Path.Combine(codexHome, "thread_history_1.sqlite");
            if (!File.Exists(database)) return false;
            var backgroundThreads = LoadBackgroundThreadIds();
            using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Cache=Shared");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT thread_id, status, started_at, completed_at
                FROM thread_turns
                WHERE started_at >= $cutoff
                ORDER BY started_at DESC
                LIMIT 64;
                """;
            command.Parameters.AddWithValue("$cutoff", DateTimeOffset.Now.AddHours(-24).ToUnixTimeSeconds());
            using var reader = command.ExecuteReader();
            string? status = null;
            long startedSeconds = 0;
            long? completedSeconds = null;
            while (reader.Read())
            {
                if (backgroundThreads.Contains(reader.GetString(0))) continue;
                status = reader.GetString(1);
                startedSeconds = reader.GetInt64(2);
                completedSeconds = reader.IsDBNull(3) ? null : reader.GetInt64(3);
                break;
            }
            if (status is null) return false;
            var started = DateTimeOffset.FromUnixTimeSeconds(startedSeconds);
            var completed = completedSeconds.HasValue ? DateTimeOffset.FromUnixTimeSeconds(completedSeconds.Value) : started;
            if (status == "inProgress")
                state = new CodexState(true, true, "working", "Codex is working", "Processing", started);
            else
            {
                var recent = DateTimeOffset.Now - completed < TimeSpan.FromSeconds(8);
                state = new CodexState(true, false, recent ? "complete" : "idle",
                    recent ? "Codex finished" : "Codex ready", "", completed);
            }
            return true;
        }
        catch { return false; }
    }

    private HashSet<string> LoadBackgroundThreadIds()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var database = Path.Combine(codexHome, "state_5.sqlite");
            if (!File.Exists(database)) return result;
            using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Cache=Shared");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM threads WHERE source LIKE '%subagent%'";
            using var reader = command.ExecuteReader();
            while (reader.Read()) result.Add(reader.GetString(0));
        }
        catch { }
        return result;
    }

    private static string ResolveCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
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
