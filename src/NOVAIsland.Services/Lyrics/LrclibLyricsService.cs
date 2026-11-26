using System.Net.Http.Json;
using System.Text.Json.Serialization;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.Services;

namespace NOVAIsland.Services.Lyrics;

/// <summary>
/// Queries LRCLIB (https://lrclib.net/api), parses LRC timestamps,
/// and caches results in SQLite. Completely independent of Spotify APIs.
/// </summary>
public sealed class LrclibLyricsService : ILyricsService
{
    private readonly HttpClient _http;
    private readonly ILyricsCacheRepository _cache;

    public LrclibLyricsService(HttpClient http, ILyricsCacheRepository cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<LyricsSearchResult?> GetLyricsAsync(
        string title,
        string artist,
        string? album,
        double durationSeconds,
        bool allowNetwork,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var cached = await _cache.GetCachedAsync(title, artist, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        if (!allowNetwork)
        {
            return null;
        }

        try
        {
            var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist ?? string.Empty)}";
            if (!string.IsNullOrWhiteSpace(album))
            {
                url += $"&album_name={Uri.EscapeDataString(album)}";
            }
            if (durationSeconds > 1)
            {
                url += $"&duration={Math.Round(durationSeconds)}";
            }

            using var resp = await _http.GetAsync(url, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                var dto = await resp.Content.ReadFromJsonAsync<LrclibDto>(cancellationToken: cancellationToken);
                var mapped = MapDto(dto);
                if (mapped is not null)
                {
                    await _cache.SaveCachedAsync(mapped, cancellationToken);
                    return mapped;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall through to search fallback
        }

        var results = await SearchLyricsAsync($"{title} {artist}".Trim(), allowNetwork: true, cancellationToken);
        var best = results.FirstOrDefault(r => r.IsSynced) ?? results.FirstOrDefault();
        if (best is not null)
        {
            await _cache.SaveCachedAsync(best, cancellationToken);
        }

        return best;
    }

    public async Task<IReadOnlyList<LyricsSearchResult>> SearchLyricsAsync(
        string query,
        bool allowNetwork,
        CancellationToken cancellationToken = default)
    {
        if (!allowNetwork || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(query)}";
            var list = await _http.GetFromJsonAsync<List<LrclibDto>>(url, cancellationToken);
            if (list is null)
            {
                return [];
            }

            return list.Select(MapDto).Where(x => x is not null).Cast<LyricsSearchResult>().ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static LyricsSearchResult? MapDto(LrclibDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var syncedLines = LrcParser.ParseSynced(dto.SyncedLyrics);
        if (syncedLines.Count > 0)
        {
            return new LyricsSearchResult(
                Title: dto.TrackName ?? string.Empty,
                Artist: dto.ArtistName ?? string.Empty,
                Album: dto.AlbumName,
                DurationSeconds: dto.Duration,
                Source: LyricsSourceKind.Lrclib,
                IsSynced: true,
                IsInstrumental: dto.Instrumental,
                Lines: syncedLines,
                RawLrc: dto.SyncedLyrics ?? dto.PlainLyrics);
        }

        var plainLines = LrcParser.ParsePlainFallback(dto.PlainLyrics);
        return new LyricsSearchResult(
            Title: dto.TrackName ?? string.Empty,
            Artist: dto.ArtistName ?? string.Empty,
            Album: dto.AlbumName,
            DurationSeconds: dto.Duration,
            Source: plainLines.Count > 0 ? LyricsSourceKind.Lrclib : LyricsSourceKind.None,
            IsSynced: false,
            IsInstrumental: dto.Instrumental,
            Lines: plainLines,
            RawLrc: dto.PlainLyrics);
    }

    private sealed class LrclibDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("trackName")] public string? TrackName { get; set; }
        [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
        [JsonPropertyName("albumName")] public string? AlbumName { get; set; }
        [JsonPropertyName("duration")] public double Duration { get; set; }
        [JsonPropertyName("instrumental")] public bool Instrumental { get; set; }
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
    }
}
