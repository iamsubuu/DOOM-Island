using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace NOVAIsland.App.Services;

public sealed record IslandThemePalette(
    Color SurfaceColor,
    Color PrimaryAccent,
    Color SecondaryAccent,
    byte BorderAlpha,
    byte AmbientTopAlpha,
    byte AmbientMidAlpha,
    byte GlossySheenAlpha);

/// <summary>
/// Synchronizes dynamic accent color, Dark/Light/System window theme, island hardware material,
/// and typography across all open WinUI 3 windows without restart.
/// Synchronizes dynamic theme presets (Glossy Pink, Venom, Spider-Man, Dr. Doom, Cyber Violet,
/// Artwork Dynamic Gradient), accent colors, Dark/Light/System window themes, and typography.
/// </summary>
public sealed class ThemeManager
{
    private readonly UISettings _uiSettings = new();
    private Color _artworkPrimary = Color.FromArgb(255, 168, 85, 247);
    private Color _artworkSecondary = Color.FromArgb(255, 34, 211, 238);

    public event EventHandler? ArtworkPaletteUpdated;

    public void UpdateArtworkPalette(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        _artworkPrimary = Color.FromArgb(255, r1, g1, b1);
        _artworkSecondary = Color.FromArgb(255, r2, g2, b2);
        ArtworkPaletteUpdated?.Invoke(this, EventArgs.Empty);
    }

    public ElementTheme ResolveElementTheme(ColorMode mode)
    {
        if (mode == ColorMode.Dark)
        {
            return ElementTheme.Dark;
        }

        if (mode == ColorMode.Light)
        {
            return ElementTheme.Light;
        }

        var bg = _uiSettings.GetColorValue(UIColorType.Background);
        var luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return luminance < 0.5 ? ElementTheme.Dark : ElementTheme.Light;
    }

    public IslandThemePalette ResolveIslandPalette(AppearanceSettings appearance)
    {
        var customAccent = TryParseHexColor(appearance.AccentColor, out var parsed)
            ? parsed
            : Color.FromArgb(255, 46, 229, 139);
        var alpha = (byte)Math.Clamp((int)Math.Round(appearance.Opacity * 2.38), 185, 246);

        return appearance.Theme switch
        {
            ThemePreset.DrDoom => new IslandThemePalette(
                SurfaceColor: Color.FromArgb(alpha, 6, 17, 11),
                PrimaryAccent: Color.FromArgb(255, 46, 229, 139),
                SecondaryAccent: Color.FromArgb(255, 16, 185, 129),
                BorderAlpha: 96,
                AmbientTopAlpha: 56,
                AmbientMidAlpha: 22,
                GlossySheenAlpha: 38),

            ThemePreset.Obsidian => new IslandThemePalette(
                SurfaceColor: Color.FromArgb(alpha, 9, 11, 20),
                PrimaryAccent: Color.FromArgb(255, 168, 85, 247),
                SecondaryAccent: Color.FromArgb(255, 34, 211, 238),
                BorderAlpha: 100,
                AmbientTopAlpha: 52,
                AmbientMidAlpha: 20,
                GlossySheenAlpha: 42),

            ThemePreset.Graphite => new IslandThemePalette(
                SurfaceColor: Color.FromArgb(alpha, 21, 25, 34),
                PrimaryAccent: customAccent,
                SecondaryAccent: Color.FromArgb(255, 148, 163, 184),
                BorderAlpha: 86,
                AmbientTopAlpha: 38,
                AmbientMidAlpha: 14,
                GlossySheenAlpha: 48),

            ThemePreset.GlossyPink => new IslandThemePalette(
                SurfaceColor: Color.FromArgb(alpha, 30, 10, 22),
                PrimaryAccent: Color.FromArgb(255, 255, 77, 166),
                SecondaryAccent: Color.FromArgb(255, 255, 168, 226),
                BorderAlpha: 128,
                AmbientTopAlpha: 72,
                AmbientMidAlpha: 28,
                GlossySheenAlpha: 78),

            ThemePreset.Venom => new IslandThemePalette(
                SurfaceColor: Color.FromArgb(Math.Max((byte)232, alpha), 5, 5, 8),
                PrimaryAccent: Color.FromArgb(255, 226, 232, 240),
                SecondaryAccent: Color.FromArgb(255, 147, 51, 234),
                BorderAlpha: 118,
                AmbientTopAlpha: 48,
                AmbientMidAlpha: 18,
                GlossySheenAlpha: 66),

            ThemePreset.SpiderMan => new IslandThemePalette(
                SurfaceColor: Color.FromArgb(alpha, 11, 16, 32),
                PrimaryAccent: Color.FromArgb(255, 239, 68, 68),
                SecondaryAccent: Color.FromArgb(255, 59, 130, 246),
                BorderAlpha: 124,
                AmbientTopAlpha: 66,
                AmbientMidAlpha: 24,
                GlossySheenAlpha: 54),

            ThemePreset.CyberViolet => new IslandThemePalette(
                SurfaceColor: Color.FromArgb(alpha, 17, 10, 34),
                PrimaryAccent: Color.FromArgb(255, 192, 132, 252),
                SecondaryAccent: Color.FromArgb(255, 34, 211, 238),
                BorderAlpha: 116,
                AmbientTopAlpha: 64,
                AmbientMidAlpha: 24,
                GlossySheenAlpha: 56),

            ThemePreset.ArtworkDynamic => new IslandThemePalette(
                SurfaceColor: Color.FromArgb(alpha,
                    (byte)Math.Clamp(_artworkPrimary.R / 7 + 4, 6, 36),
                    (byte)Math.Clamp(_artworkPrimary.G / 7 + 4, 6, 36),
                    (byte)Math.Clamp(_artworkPrimary.B / 7 + 6, 8, 40)),
                PrimaryAccent: _artworkPrimary,
                SecondaryAccent: _artworkSecondary,
                BorderAlpha: 124,
                AmbientTopAlpha: 74,
                AmbientMidAlpha: 28,
                GlossySheenAlpha: 56),

            ThemePreset.Contrast => new IslandThemePalette(
                SurfaceColor: Color.FromArgb(248, 4, 6, 8),
                PrimaryAccent: customAccent,
                SecondaryAccent: Color.FromArgb(255, 255, 255, 255),
                BorderAlpha: 175,
                AmbientTopAlpha: 52,
                AmbientMidAlpha: 20,
                GlossySheenAlpha: 40),

            _ => new IslandThemePalette( // PureBlack
                SurfaceColor: Color.FromArgb(alpha, 7, 8, 12),
                PrimaryAccent: customAccent,
                SecondaryAccent: Color.FromArgb(255,
                    (byte)Math.Max(30, (int)customAccent.R - 25),
                    (byte)Math.Min(255, (int)customAccent.G + 15),
                    (byte)Math.Min(255, (int)customAccent.B + 25)),
                BorderAlpha: 84,
                AmbientTopAlpha: 44,
                AmbientMidAlpha: 16,
                GlossySheenAlpha: 38)
        };
    }

    public void ApplyAppearance(AppearanceSettings appearance)
    {
        if (Application.Current is null)
        {
            return;
        }

        var resources = Application.Current.Resources;
        var palette = ResolveIslandPalette(appearance);

        resources["NovaAccentColor"] = palette.PrimaryAccent;
        if (resources.TryGetValue("NovaAccentBrush", out var accentObj) && accentObj is SolidColorBrush accentBrush)
        {
            accentBrush.Color = palette.PrimaryAccent;
        }

        SetBrushColor(resources, "IslandSurfaceBrush", palette.SurfaceColor);

        SetBrushColor(resources, "IslandBorderSubtleBrush", Color.FromArgb(48, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B));
        SetBrushColor(resources, "IslandBorderStrongBrush", Color.FromArgb(palette.BorderAlpha, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B));
        SetBrushColor(resources, "IslandBorderContrastBrush", Color.FromArgb(165, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B));
        SetBrushColor(resources, "IslandControlHoverBrush", Color.FromArgb(46, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B));
        SetBrushColor(resources, "IslandControlActiveBrush", Color.FromArgb(64, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B));
        SetBrushColor(resources, "IslandDividerBrush", Color.FromArgb(40, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B));

        var fontKey = appearance.FontFamily switch
        {
            FontFamilyOption.Manrope => "NovaManropeFont",
            FontFamilyOption.Outfit => "NovaOutfitFont",
            FontFamilyOption.SegoeUiVariable => "NovaSegoeFont",
            _ => "NovaPlusJakartaFont"
        };

        if (resources.TryGetValue(fontKey, out var fontObj) && fontObj is FontFamily ff)
        {
            resources["NovaActiveFont"] = ff;
        }

        ApplyWindowThemeBrushes(ResolveElementTheme(appearance.ColorMode), palette.PrimaryAccent, resources);
    }

    private static void ApplyWindowThemeBrushes(ElementTheme theme, Color accent, ResourceDictionary resources)
    {
        var isDark = theme != ElementTheme.Light;
        SetBrushColor(resources, "WindowBackgroundBrush", isDark
            ? Color.FromArgb(174, (byte)(accent.R / 24 + 5), (byte)(accent.G / 24 + 7), (byte)(accent.B / 24 + 10))
            : Color.FromArgb(198, 244, 248, 252));
        SetBrushColor(resources, "WindowSidebarBrush", isDark ? Color.FromArgb(24, 20, 26, 36) : Color.FromArgb(115, 255, 255, 255));
        SetBrushColor(resources, "WindowCardBrush", isDark ? Color.FromArgb(28, 18, 24, 34) : Color.FromArgb(180, 255, 255, 255));
        SetBrushColor(resources, "WindowCardHoverBrush", Color.FromArgb(44, accent.R, accent.G, accent.B));
        SetBrushColor(resources, "WindowBorderBrush", isDark ? Color.FromArgb(58, accent.R, accent.G, accent.B) : Color.FromArgb(30, 0, 0, 0));
        SetBrushColor(resources, "WindowDividerBrush", isDark ? Color.FromArgb(34, accent.R, accent.G, accent.B) : Color.FromArgb(18, 0, 0, 0));
        SetBrushColor(resources, "WindowInputBgBrush", isDark ? Color.FromArgb(165, 11, 14, 20) : Color.FromArgb(230, 255, 255, 255));
        SetBrushColor(resources, "WindowTextPrimaryBrush", isDark ? Color.FromArgb(255, 244, 244, 245) : Color.FromArgb(255, 15, 23, 42));
        SetBrushColor(resources, "WindowTextSecondaryBrush", isDark ? Color.FromArgb(255, 161, 161, 170) : Color.FromArgb(255, 71, 85, 105));
        SetBrushColor(resources, "WindowTextMutedBrush", isDark ? Color.FromArgb(255, 113, 113, 122) : Color.FromArgb(255, 100, 116, 139));
    }

    private static void SetBrushColor(ResourceDictionary resources, string key, Color color)
    {
        if (resources.TryGetValue(key, out var val) && val is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    public static bool TryParseHexColor(string? hex, out Color color)
    {
        color = Color.FromArgb(255, 46, 229, 139);
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 &&
            byte.TryParse(clean[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(clean[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(clean[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        if (clean.Length == 6 && uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            color = Color.FromArgb(255, r, g, b);
            color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            return true;
        }

        return false;
    }
}

