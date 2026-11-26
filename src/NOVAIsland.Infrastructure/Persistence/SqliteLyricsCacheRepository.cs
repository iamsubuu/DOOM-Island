using System.Text.Json;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;

namespace NOVAIsland.Infrastructure.Persistence;

public sealed class SqliteLyricsCacheRepository : ILyricsCacheRepository
{
    private readonly SqliteDbContext _db;

    public SqliteLyricsCacheRepository(SqliteDbContext db)
    {
        _db = db;
    }

    public static string BuildKey(string title, string artist) =>
        $"{artist.Trim().ToLowerInvariant()}::{title.Trim().ToLowerInvariant()}";

    public async Task<LyricsSearchResult?> GetCachedAsync(string title, string artist, CancellationToken cancellationToken = default)
    {
        await _db.InitializeSchemaAsync(cancellationToken);
        var key = BuildKey(title, artist);

        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT track_name, artist_name, album_name, duration_seconds, is_synced, is_instrumental, source_kind, lines_json, raw_plain_lyrics
            FROM lyrics_cache WHERE cache_key = $key LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$key", key);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return null;
        }

        var lines = JsonSerializer.Deserialize<List<LyricLine>>(r.GetString(7)) ?? [];
        return new LyricsSearchResult(
            Title: r.GetString(0),
            Artist: r.GetString(1),
            Album: r.IsDBNull(2) ? null : r.GetString(2),
            DurationSeconds: r.GetDouble(3),
            Source: (LyricsSourceKind)r.GetInt32(6),
            IsSynced: r.GetInt32(4) == 1,
            IsInstrumental: r.GetInt32(5) == 1,
            Lines: lines,
            RawLrc: r.IsDBNull(8) ? null : r.GetString(8));
    }

    public async Task SaveCachedAsync(LyricsSearchResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        await _db.InitializeSchemaAsync(cancellationToken);
        var key = BuildKey(result.Title, result.Artist);
        var linesJson = JsonSerializer.Serialize(result.Lines);

        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO lyrics_cache
            (cache_key, track_name, artist_name, album_name, duration_seconds, is_synced, is_instrumental, source_kind, lines_json, raw_plain_lyrics, cached_at_utc)
            VALUES ($key, $track, $artist, $album, $dur, $synced, $inst, $src, $lines, $plain, $now)
            ON CONFLICT(cache_key) DO UPDATE SET
                album_name = excluded.album_name,
                duration_seconds = excluded.duration_seconds,
                is_synced = excluded.is_synced,
                is_instrumental = excluded.is_instrumental,
                source_kind = excluded.source_kind,
                lines_json = excluded.lines_json,
                raw_plain_lyrics = excluded.raw_plain_lyrics,
                cached_at_utc = excluded.cached_at_utc;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$track", result.Title);
        cmd.Parameters.AddWithValue("$artist", result.Artist);
        cmd.Parameters.AddWithValue("$album", result.Album ?? string.Empty);
        cmd.Parameters.AddWithValue("$dur", result.DurationSeconds);
        cmd.Parameters.AddWithValue("$synced", result.IsSynced ? 1 : 0);
        cmd.Parameters.AddWithValue("$inst", result.IsInstrumental ? 1 : 0);
        cmd.Parameters.AddWithValue("$src", (int)result.Source);
        cmd.Parameters.AddWithValue("$lines", linesJson);
        cmd.Parameters.AddWithValue("$plain", (object?)result.RawLrc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _db.InitializeSchemaAsync(cancellationToken);
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM lyrics_cache;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<double> GetSizeMbAsync(CancellationToken cancellationToken = default)
    {
        await _db.InitializeSchemaAsync(cancellationToken);
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(LENGTH(lines_json) + COALESCE(LENGTH(raw_plain_lyrics), 0)), 0) FROM lyrics_cache;";
        var bytes = Convert.ToDouble(await cmd.ExecuteScalarAsync(cancellationToken));
        return Math.Round(bytes / (1024.0 * 1024.0), 2);
    }
}
