using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

        var cleanTitle = CleanMetadata(title);
        var cleanArtist = CleanArtist(artist);

        var cached = await _cache.GetCachedAsync(title, artist, cancellationToken)
                  ?? await _cache.GetCachedAsync(cleanTitle, cleanArtist, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        if (!allowNetwork)
        {
            return null;
        }

        foreach (var (t, a) in new[] { (title, artist), (cleanTitle, cleanArtist) }.Distinct())
        {
            try
            {
                var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(t)}&artist_name={Uri.EscapeDataString(a ?? string.Empty)}";
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
                    if (mapped is not null && mapped.Lines.Count > 0)
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
                // Fall through to cleaned/search fallback
            }
        }

        var results = await SearchLyricsAsync($"{cleanTitle} {cleanArtist}".Trim(), allowNetwork: true, cancellationToken);
        if (results.Count == 0 && !string.Equals(cleanTitle, title, StringComparison.OrdinalIgnoreCase))
        {
            results = await SearchLyricsAsync(cleanTitle, allowNetwork: true, cancellationToken);
        }
        var best = results.FirstOrDefault(r => r.IsSynced) ?? results.FirstOrDefault();
        if (best is not null)
        {
            await _cache.SaveCachedAsync(best, cancellationToken);
        }

        return best;
    }

    private static string CleanMetadata(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = Regex.Replace(raw, @"\s*[\(\[](?:official|lyric|lyrics|audio|video|hd|4k|mv|visualizer|from\s+[^\)\]]+)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*\|.*$", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(s) ? raw.Trim() : s;
    }

    private static string CleanArtist(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = Regex.Replace(raw, @"\s*-\s*Topic$", "", RegexOptions.IgnoreCase);
        var first = s.Split([',', '&'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(first) ? s.Trim() : first;
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
