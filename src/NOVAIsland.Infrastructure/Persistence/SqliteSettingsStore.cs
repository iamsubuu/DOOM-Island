using System.Text.Json;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.Protocol;

namespace NOVAIsland.Infrastructure.Persistence;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly SqliteDbContext _db;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public SqliteSettingsStore(SqliteDbContext db)
    {
        _db = db;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _db.InitializeSchemaAsync(cancellationToken);
            await using var conn = await _db.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT json_payload FROM app_settings WHERE id = 1;";

            var raw = await cmd.ExecuteScalarAsync(cancellationToken) as string;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new AppSettings();
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(raw, JsonOptions);
            return NormalizeDefaults(loaded);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _db.InitializeSchemaAsync(cancellationToken);
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            await using var conn = await _db.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO app_settings (id, json_payload, updated_at_utc)
                VALUES (1, $json, $now)
                ON CONFLICT(id) DO UPDATE SET json_payload = excluded.json_payload, updated_at_utc = excluded.updated_at_utc;
                """;
            cmd.Parameters.AddWithValue("$json", json);
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<string> ExportJsonAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        Task.FromResult(JsonSerializer.Serialize(settings, JsonOptions));

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _db.InitializeSchemaAsync(cancellationToken);
            await using var conn = await _db.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM app_settings WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static AppSettings NormalizeDefaults(AppSettings? loaded)
    {
        if (loaded is null)
        {
            return new AppSettings();
        }

        loaded.General ??= new GeneralSettings();
        loaded.Position ??= new PositionSettings();
        loaded.Appearance ??= new AppearanceSettings();
        loaded.Animation ??= new AnimationSettings();
        loaded.Music ??= new MusicSettings();
        loaded.BrowserIntegration ??= new BrowserIntegrationSettings();
        loaded.Lyrics ??= new LyricsSettings();
        loaded.Clipboard ??= new ClipboardSettings();
        loaded.Notifications ??= new NotificationSettings();
        loaded.Widgets ??= new WidgetSettings();
        loaded.Privacy ??= new PrivacySettings();
        if (loaded.Shortcuts is null || loaded.Shortcuts.Count == 0)
        {
            loaded.Shortcuts = AppSettings.CreateDefaultShortcuts();
        }

        return loaded;
    }
}
