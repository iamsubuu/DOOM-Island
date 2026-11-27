using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NOVAIsland.Core.Configuration;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace NOVAIsland.Infrastructure.Platform;

/// <summary>
/// Caches SMTC thumbnails, upgrades low-res media URLs / fetches 600x600+ cover art,
/// and extracts a 2-stop vibrant gradient palette from the active artwork.
/// </summary>
public sealed partial class ArtworkEnhancementService
{
    private static readonly HttpClient SharedHttp = CreateHttpClient();
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, string?> _lookupCache = new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(https?://i\.ytimg\.com/vi/[^/]+/)(?:default|mqdefault|hqdefault|sddefault)(\.jpg(?:\?.*)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeThumbRegex();

    [GeneratedRegex(@"(=w\d+-h\d+[^&\s]*|=s\d+[^&\s]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex GoogleMusicSizeRegex();

    [GeneratedRegex(@"/\d+x\d+bb(\.\w+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ItunesSizeRegex();

    [GeneratedRegex(@"\s*(?:\(|\[)(?:official\s*(?:music\s*)?video|lyric(?:s)?\s*video|audio|hd|4k|8k|remastered|feat\.?[^\)\]]*|ft\.?[^\)\]]*)(?:\)|\])", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseTagRegex();

    public ArtworkEnhancementService(string cacheDir)
    {
        _cacheDir = cacheDir;
        Directory.CreateDirectory(_cacheDir);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DOOMIsland/{AppConstants.AppVersion}");
        return client;
    }

    public static string? UpgradeKnownCdnUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var yt = YouTubeThumbRegex().Match(url);
        if (yt.Success)
        {
            return $"{yt.Groups[1].Value}maxresdefault{yt.Groups[2].Value}";
        }

        if (url.Contains("googleusercontent.com", StringComparison.OrdinalIgnoreCase) &&
            GoogleMusicSizeRegex().IsMatch(url))
        {
            return GoogleMusicSizeRegex().Replace(url, "=w720-h720-l90-rj");
        }

        if (ItunesSizeRegex().IsMatch(url))
        {
            return ItunesSizeRegex().Replace(url, "/600x600bb$1");
        }

        if (url.Contains("i.scdn.co/image/ab67616d00004851", StringComparison.OrdinalIgnoreCase))
        {
            return url.Replace("ab67616d00004851", "ab67616d0000b273", StringComparison.OrdinalIgnoreCase);
        }

        return url;
    }

    public async Task<(string? Uri, uint Width, uint Height)> CacheSmtcThumbnailAsync(
        IRandomAccessStreamReference thumbnail)
    {
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            using var netStream = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await netStream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
            {
                return (null, 0, 0);
            }

            uint width = 0;
            uint height = 0;
            try
            {
                ms.Position = 0;
                var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                width = decoder.PixelWidth;
                height = decoder.PixelHeight;
            }
            catch
            {
                // Keep raw image bytes even if metadata decode fails
            }

            var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16];
            var ext = bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 ? ".png" : ".jpg";
            var path = Path.Combine(_cacheDir, $"smtc_{hash}{ext}");
            if (!File.Exists(path))
            {
                await File.WriteAllBytesAsync(path, bytes);
            }

            return (new Uri(path).AbsoluteUri, width, height);
        }
        catch
        {
            return (null, 0, 0);
        }
    }

    public async Task<string?> TryFetchHighQualityArtworkAsync(
        string? title,
        string? artist,
        string? rawUrl,
        uint existingWidth,
        uint existingHeight,
        bool allowNetwork,
        CancellationToken ct = default)
    {
        if (!allowNetwork)
        {
            return null;
        }

        try
        {
            // 1. If we have an HTTP URL (e.g. from browser extension), try upgraded CDN URL first
            if (!string.IsNullOrWhiteSpace(rawUrl) &&
                (rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                var upgraded = UpgradeKnownCdnUrl(rawUrl) ?? rawUrl;
                var cachedFromUrl = await DownloadAndCacheUrlAsync(upgraded, ct)
                    ?? (upgraded != rawUrl ? await DownloadAndCacheUrlAsync(rawUrl, ct) : null);
                if (cachedFromUrl is not null)
                {
                    return cachedFromUrl;
                }
            }

            // 2. If SMTC art is already >= 320px on both edges, keep it as-is
            if (existingWidth >= 320 && existingHeight >= 320)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var cleanTitle = CleanTrackTerm(title);
            var cleanArtist = CleanArtistTerm(artist);
            var cacheKey = $"{cleanArtist}::{cleanTitle}";
            if (_lookupCache.TryGetValue(cacheKey, out var cachedUri))
            {
                return cachedUri;
            }

            var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey.ToLowerInvariant())))[..16];
            var diskPath = Path.Combine(_cacheDir, $"hq_{keyHash}.jpg");
            if (File.Exists(diskPath) && new FileInfo(diskPath).Length >= 2048)
            {
                var fileUri = new Uri(diskPath).AbsoluteUri;
                _lookupCache[cacheKey] = fileUri;
                return fileUri;
            }

            var query = string.IsNullOrWhiteSpace(cleanArtist) ? cleanTitle : $"{cleanArtist} {cleanTitle}";
            var apiUrl = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&media=music&entity=song&limit=1";
            using var jsonDoc = await SharedHttp.GetFromJsonAsync<JsonDocument>(apiUrl, ct);
            if (jsonDoc is null ||
                !jsonDoc.RootElement.TryGetProperty("results", out var results) ||
                results.GetArrayLength() == 0)
            {
                _lookupCache[cacheKey] = null;
                return null;
            }

            var first = results[0];
            var art100 = first.TryGetProperty("artworkUrl100", out var artProp) ? artProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(art100))
            {
                _lookupCache[cacheKey] = null;
                return null;
            }

            var hqUrl = UpgradeKnownCdnUrl(art100) ?? art100;
            var imgBytes = await SharedHttp.GetByteArrayAsync(hqUrl, ct);
            if (imgBytes.Length < 2048)
            {
                _lookupCache[cacheKey] = null;
                return null;
            }

            await File.WriteAllBytesAsync(diskPath, imgBytes, ct);
            var resolvedUri = new Uri(diskPath).AbsoluteUri;
            _lookupCache[cacheKey] = resolvedUri;
            return resolvedUri;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(byte R1, byte G1, byte B1, byte R2, byte G2, byte B2)?> ExtractGradientPaletteAsync(string? artworkUri)
    {
        if (string.IsNullOrWhiteSpace(artworkUri) || !Uri.TryCreate(artworkUri, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            return null;
        }

        var path = uri.LocalPath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            using var ms = new MemoryStream(bytes);
            var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());

            var transform = new BitmapTransform
            {
                ScaledWidth = 24,
                ScaledHeight = 24,
                InterpolationMode = BitmapInterpolationMode.Linear
            };

            var pixelProvider = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            var bgra = pixelProvider.DetachPixelData();
            return ComputeGradientFromBgraPixels(bgra);
        }
        catch
        {
            return null;
        }
    }

    internal static (byte R1, byte G1, byte B1, byte R2, byte G2, byte B2)? ComputeGradientFromBgraPixels(ReadOnlySpan<byte> bgra)
    {
        // Bucket quantized hues (12 hue sectors + weighted saturation/value) for vibrant dual accents
        var counts = new double[12];
        var sumR = new double[12];
        var sumG = new double[12];
        var sumB = new double[12];
        double totalR = 0, totalG = 0, totalB = 0, totalWeight = 0;

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var b = bgra[i];
            var g = bgra[i + 1];
            var r = bgra[i + 2];
            var a = bgra[i + 3];
            if (a < 160) continue;

            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;
            if (max < 28 || min > 235) continue;

            var sat = max == 0 ? 0.0 : delta / (double)max;
            var weight = 0.25 + (sat * 3.0);
            totalR += r * weight;
            totalG += g * weight;
            totalB += b * weight;
            totalWeight += weight;

            if (delta < 18) continue;

            double hue;
            if (max == r) hue = ((g - b) / (double)delta) % 6.0;
            else if (max == g) hue = ((b - r) / (double)delta) + 2.0;
            else hue = ((r - g) / (double)delta) + 4.0;
            hue *= 60.0;
            if (hue < 0) hue += 360.0;

            var bucket = Math.Clamp((int)(hue / 30.0), 0, 11);
            counts[bucket] += weight;
            sumR[bucket] += r * weight;
            sumG[bucket] += g * weight;
            sumB[bucket] += b * weight;
        }

        var bestIdx = -1;
        var secondIdx = -1;
        for (var i = 0; i < 12; i++)
        {
            if (counts[i] <= 0) continue;
            if (bestIdx < 0 || counts[i] > counts[bestIdx])
            {
                bestIdx = i;
            }
        }

        if (bestIdx >= 0)
        {
            for (var i = 0; i < 12; i++)
            {
                if (counts[i] <= 0 || i == bestIdx) continue;
                var hueDist = Math.Min(Math.Abs(i - bestIdx), 12 - Math.Abs(i - bestIdx));
                if (hueDist >= 2 && (secondIdx < 0 || counts[i] > counts[secondIdx]))
                {
                    secondIdx = i;
                }
            }

            var c1 = BoostForDarkGlass(sumR[bestIdx] / counts[bestIdx], sumG[bestIdx] / counts[bestIdx], sumB[bestIdx] / counts[bestIdx]);
            var c2 = secondIdx >= 0
                ? BoostForDarkGlass(sumR[secondIdx] / counts[secondIdx], sumG[secondIdx] / counts[secondIdx], sumB[secondIdx] / counts[secondIdx])
                : ShiftComplementary(c1);
            return (c1.R, c1.G, c1.B, c2.R, c2.G, c2.B);
        }

        if (totalWeight > 0)
        {
            var avg = BoostForDarkGlass(totalR / totalWeight, totalG / totalWeight, totalB / totalWeight);
            var sec = ShiftComplementary(avg);
            return (avg.R, avg.G, avg.B, sec.R, sec.G, sec.B);
        }

        return null;
    }

    private static (byte R, byte G, byte B) BoostForDarkGlass(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        if (max < 150 && max > 1)
        {
            var boost = 210.0 / max;
            r = Math.Min(255, r * boost);
            g = Math.Min(255, g * boost);
            b = Math.Min(255, b * boost);
        }
        return ((byte)Math.Clamp(r, 40, 255), (byte)Math.Clamp(g, 40, 255), (byte)Math.Clamp(b, 40, 255));
    }

    private static (byte R, byte G, byte B) ShiftComplementary((byte R, byte G, byte B) c) =>
        ((byte)Math.Clamp(c.B * 0.8 + 45, 50, 255), (byte)Math.Clamp(c.R * 0.65 + 45, 50, 255), (byte)Math.Clamp(c.G * 0.85 + 55, 60, 255));

    private async Task<string?> DownloadAndCacheUrlAsync(string url, CancellationToken ct)
    {
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
            var path = Path.Combine(_cacheDir, $"url_{hash}.jpg");
            if (File.Exists(path) && new FileInfo(path).Length >= 2048)
            {
                return new Uri(path).AbsoluteUri;
            }

            var bytes = await SharedHttp.GetByteArrayAsync(url, ct);
            if (bytes.Length < 2048)
            {
                return null;
            }

            await File.WriteAllBytesAsync(path, bytes, ct);
            return new Uri(path).AbsoluteUri;
        }
        catch
        {
            return null;
        }
    }

    private static string CleanTrackTerm(string title)
    {
        var cleaned = NoiseTagRegex().Replace(title, string.Empty);
        var pipeIdx = cleaned.IndexOf('|');
        if (pipeIdx > 3) cleaned = cleaned[..pipeIdx];
        return cleaned.Trim(' ', '-', '–');
    }

    private static string CleanArtistTerm(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        var a = artist.Trim();
        if (a.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
        {
            a = a[..^8].Trim();
        }
        if (a is "Google Chrome" or "Microsoft Edge" or "Opera" or "Opera GX" or "Brave" or "Vivaldi" or "Firefox")
        {
            return string.Empty;
        }
        return a;
    }
}

