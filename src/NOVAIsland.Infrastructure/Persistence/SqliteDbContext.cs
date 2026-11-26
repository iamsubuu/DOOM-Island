using Microsoft.Data.Sqlite;
using NOVAIsland.Core.Configuration;

namespace NOVAIsland.Infrastructure.Persistence;

public sealed class SqliteDbContext
{
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string ConnectionString { get; }

    public SqliteDbContext(string? customDataDirectory = null)
    {
        DataDirectory = customDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.AppDataFolderName);

        Directory.CreateDirectory(DataDirectory);
        DatabasePath = Path.Combine(DataDirectory, AppConstants.DatabaseFileName);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);

        return conn;
    }

    public async Task InitializeSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);

        const string ddl = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                json_payload TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS clipboard_items (
                id TEXT PRIMARY KEY,
                item_type INTEGER NOT NULL,
                content TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                preview_title TEXT NULL,
                language TEXT NULL,
                image_path TEXT NULL,
                source_app TEXT NOT NULL,
                captured_at_utc TEXT NOT NULL,
                pinned INTEGER NOT NULL DEFAULT 0,
                sensitive INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS ix_clipboard_captured ON clipboard_items(pinned DESC, captured_at_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_clipboard_hash ON clipboard_items(content_hash);

            CREATE TABLE IF NOT EXISTS lyrics_cache (
                cache_key TEXT PRIMARY KEY,
                track_name TEXT NOT NULL,
                artist_name TEXT NOT NULL,
                album_name TEXT NOT NULL,
                duration_seconds REAL NOT NULL,
                is_synced INTEGER NOT NULL,
                is_instrumental INTEGER NOT NULL,
                source_kind INTEGER NOT NULL,
                lines_json TEXT NOT NULL,
                raw_plain_lyrics TEXT NULL,
                cached_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS notification_history (
                id TEXT PRIMARY KEY,
                app_name TEXT NOT NULL,
                aumid TEXT NULL,
                title TEXT NOT NULL,
                body TEXT NOT NULL,
                received_at_utc TEXT NOT NULL,
                is_read INTEGER NOT NULL DEFAULT 0,
                is_dismissed INTEGER NOT NULL DEFAULT 0
            );
            """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await using var migCmd = conn.CreateCommand();
        migCmd.CommandText = """
            INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
            VALUES (1, $now);
            """;
        migCmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await migCmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

