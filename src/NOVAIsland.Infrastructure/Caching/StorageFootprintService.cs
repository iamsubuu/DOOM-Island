using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using NOVAIsland.Infrastructure.Persistence;

namespace NOVAIsland.Infrastructure.Caching;

public sealed class StorageFootprintService
{
    private readonly SqliteDbContext _db;
    private readonly ILyricsCacheRepository _lyricsCache;

    public string ArtworkCacheDir { get; }
    public string ClipboardImagesDir { get; }

    public StorageFootprintService(SqliteDbContext db, ILyricsCacheRepository lyricsCache)
    {
        _db = db;
        _lyricsCache = lyricsCache;
        ArtworkCacheDir = Path.Combine(db.DataDirectory, "cache", "artwork");
        ClipboardImagesDir = Path.Combine(db.DataDirectory, "cache", "clipboard");
        Directory.CreateDirectory(ArtworkCacheDir);
        Directory.CreateDirectory(ClipboardImagesDir);
    }

    public async Task<StorageFootprint> MeasureAsync(CancellationToken cancellationToken = default)
    {
        var dbBytes = File.Exists(_db.DatabasePath) ? new FileInfo(_db.DatabasePath).Length : 0L;
        var artBytes = MeasureDirectoryBytes(ArtworkCacheDir);
        var clipBytes = MeasureDirectoryBytes(ClipboardImagesDir);
        var lyricsMb = await _lyricsCache.GetSizeMbAsync(cancellationToken);

        return new StorageFootprint
        {
            AlbumCacheMb = Math.Round(artBytes / (1024.0 * 1024.0), 2),
            ClipboardImagesMb = Math.Round(clipBytes / (1024.0 * 1024.0), 2),
            LyricsCacheMb = lyricsMb,
            ConfigDbMb = Math.Round(dbBytes / (1024.0 * 1024.0), 2)
        };
    }

    public void ClearDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            try { File.Delete(file); } catch { /* ignore locked files */ }
        }
    }

    private static long MeasureDirectoryBytes(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return 0L;
        }

        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
    }
}
