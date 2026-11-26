using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace NOVAIsland.App.Services;

/// <summary>
/// Synchronizes dynamic accent color, Dark/Light/System window theme, island hardware material,
/// and typography across all open WinUI 3 windows without restart.
/// </summary>
public sealed class ThemeManager
{
    private readonly UISettings _uiSettings = new();

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

    public void ApplyAppearance(AppearanceSettings appearance)
    {
        if (Application.Current is null)
        {
            return;
        }

        var resources = Application.Current.Resources;

        if (TryParseHexColor(appearance.AccentColor, out var accent))
        {
            resources["NovaAccentColor"] = accent;
            if (resources["NovaAccentBrush"] is SolidColorBrush accentBrush)
            {
                accentBrush.Color = accent;
            }
        }

        var alpha = (byte)Math.Clamp((int)Math.Round(appearance.Opacity * 2.55), 204, 255);
        var surfaceColor = appearance.Theme switch
        {
            ThemePreset.PureBlack => Color.FromArgb(alpha, 0, 0, 0),
            ThemePreset.Obsidian => Color.FromArgb(alpha, 10, 11, 14),
            ThemePreset.Graphite => Color.FromArgb(alpha, 22, 23, 27),
            ThemePreset.Contrast => Color.FromArgb(255, 0, 0, 0),
            _ => Color.FromArgb(alpha, 0, 0, 0)
        };

        if (resources["IslandSurfaceBrush"] is SolidColorBrush surfaceBrush)
        {
            surfaceBrush.Color = surfaceColor;
        }

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

        ApplyWindowThemeBrushes(ResolveElementTheme(appearance.ColorMode), resources);
    }

    private static void ApplyWindowThemeBrushes(ElementTheme theme, ResourceDictionary resources)
    {
        var isDark = theme != ElementTheme.Light;
        SetBrush(resources, "WindowBackgroundBrush", isDark ? Color.FromArgb(245, 16, 17, 22) : Color.FromArgb(245, 250, 250, 252));
        SetBrush(resources, "WindowSidebarBrush", isDark ? Color.FromArgb(10, 255, 255, 255) : Color.FromArgb(160, 244, 244, 245));
        SetBrush(resources, "WindowCardBrush", isDark ? Color.FromArgb(8, 255, 255, 255) : Color.FromArgb(255, 255, 255, 255));
        SetBrush(resources, "WindowCardHoverBrush", isDark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(255, 244, 244, 246));
        SetBrush(resources, "WindowBorderBrush", isDark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(220, 228, 228, 231));
        SetBrush(resources, "WindowDividerBrush", isDark ? Color.FromArgb(16, 255, 255, 255) : Color.FromArgb(200, 228, 228, 231));
        SetBrush(resources, "WindowInputBgBrush", isDark ? Color.FromArgb(255, 23, 25, 34) : Color.FromArgb(255, 255, 255, 255));
        SetBrush(resources, "WindowTextPrimaryBrush", isDark ? Color.FromArgb(255, 244, 244, 245) : Color.FromArgb(255, 24, 24, 27));
        SetBrush(resources, "WindowTextSecondaryBrush", isDark ? Color.FromArgb(255, 161, 161, 170) : Color.FromArgb(255, 82, 82, 91));
        SetBrush(resources, "WindowTextMutedBrush", isDark ? Color.FromArgb(255, 113, 113, 122) : Color.FromArgb(255, 113, 113, 122));
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    public static bool TryParseHexColor(string? hex, out Color color)
    {
        color = Color.FromArgb(255, 122, 162, 247);
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 &&
            byte.TryParse(clean[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(clean[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(clean[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            color = Color.FromArgb(255, r, g, b);
            return true;
        }

        return false;
    }
}

