using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;

namespace NOVAIsland.Infrastructure.Persistence;

public sealed class SqliteClipboardRepository : IClipboardRepository
{
    private readonly SqliteDbContext _db;

    public SqliteClipboardRepository(SqliteDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ClipboardItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _db.InitializeSchemaAsync(cancellationToken);
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, item_type, content, preview_title, language, image_path, source_app, captured_at_utc, pinned, sensitive
            FROM clipboard_items
            ORDER BY pinned DESC, captured_at_utc DESC;
            """;

        var list = new List<ClipboardItem>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ClipboardItem(
                Id: reader.GetString(0),
                Type: (ClipboardItemType)reader.GetInt32(1),
                Content: reader.GetString(2),
                PreviewTitle: reader.IsDBNull(3) ? null : reader.GetString(3),
                Language: reader.IsDBNull(4) ? null : reader.GetString(4),
                ImagePath: reader.IsDBNull(5) ? null : reader.GetString(5),
                SourceApp: reader.GetString(6),
                CapturedAtUtc: DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                Pinned: reader.GetInt32(8) == 1,
                Sensitive: reader.GetInt32(9) == 1));
        }

        return list;
    }

    public async Task UpsertAsync(ClipboardItem item, int historyLimit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _db.InitializeSchemaAsync(cancellationToken);
        var hash = ComputeHash($"{(int)item.Type}:{item.Content}:{item.ImagePath}");

        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);

        // Deduplicate identical non-pinned content by updating its timestamp
        await using (var findCmd = conn.CreateCommand())
        {
            findCmd.Transaction = tx;
            findCmd.CommandText = "SELECT id, pinned FROM clipboard_items WHERE content_hash = $hash LIMIT 1;";
            findCmd.Parameters.AddWithValue("$hash", hash);
            await using var r = await findCmd.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken))
            {
                var existingId = r.GetString(0);
                var existingPinned = r.GetInt32(1);
                await r.CloseAsync();

                await using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = """
                    UPDATE clipboard_items
                    SET captured_at_utc = $ts, source_app = $app
                    WHERE id = $id;
                    """;
                upd.Parameters.AddWithValue("$ts", item.CapturedAtUtc.ToString("O"));
                upd.Parameters.AddWithValue("$app", item.SourceApp);
                upd.Parameters.AddWithValue("$id", existingId);
                await upd.ExecuteNonQueryAsync(cancellationToken);
                _ = existingPinned;
                await tx.CommitAsync(cancellationToken);
                return;
            }
        }

        await using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO clipboard_items
                (id, item_type, content, content_hash, preview_title, language, image_path, source_app, captured_at_utc, pinned, sensitive)
                VALUES ($id, $type, $content, $hash, $preview, $lang, $img, $app, $ts, $pinned, $sensitive);
                """;
            ins.Parameters.AddWithValue("$id", item.Id);
            ins.Parameters.AddWithValue("$type", (int)item.Type);
            ins.Parameters.AddWithValue("$content", item.Content);
            ins.Parameters.AddWithValue("$hash", hash);
            ins.Parameters.AddWithValue("$preview", (object?)item.PreviewTitle ?? DBNull.Value);
            ins.Parameters.AddWithValue("$lang", (object?)item.Language ?? DBNull.Value);
            ins.Parameters.AddWithValue("$img", (object?)item.ImagePath ?? DBNull.Value);
            ins.Parameters.AddWithValue("$app", item.SourceApp);
            ins.Parameters.AddWithValue("$ts", item.CapturedAtUtc.ToString("O"));
            ins.Parameters.AddWithValue("$pinned", item.Pinned ? 1 : 0);
            ins.Parameters.AddWithValue("$sensitive", item.Sensitive ? 1 : 0);
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }

        // Enforce unpinned retention limit
        var safeLimit = Math.Max(1, historyLimit);
        await using (var trim = conn.CreateCommand())
        {
            trim.Transaction = tx;
            trim.CommandText = """
                DELETE FROM clipboard_items
                WHERE pinned = 0
                  AND id NOT IN (
                      SELECT id FROM clipboard_items
                      WHERE pinned = 0
                      ORDER BY captured_at_utc DESC
                      LIMIT $limit
                  );
                """;
            trim.Parameters.AddWithValue("$limit", safeLimit);
            await trim.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task SetPinnedAsync(string id, bool pinned, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE clipboard_items SET pinned = $pinned WHERE id = $id;";
        cmd.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM clipboard_items WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(bool keepPinned, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = keepPinned
            ? "DELETE FROM clipboard_items WHERE pinned = 0;"
            : "DELETE FROM clipboard_items;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PurgeExpiredAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(maxAge).ToString("O");
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM clipboard_items WHERE pinned = 0 AND captured_at_utc < $cutoff;";
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}

