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

        var alpha = (byte)Math.Clamp((int)Math.Round(appearance.Opacity * 2.45), 215, 245);
        var surfaceColor = appearance.Theme switch
        {
            ThemePreset.PureBlack => Color.FromArgb(236, 4, 6, 10),
            ThemePreset.Obsidian => Color.FromArgb(alpha, 11, 13, 20),
            ThemePreset.Graphite => Color.FromArgb(alpha, 20, 23, 32),
            ThemePreset.Contrast => Color.FromArgb(255, 0, 0, 0),
            _ => Color.FromArgb(236, 4, 6, 10)
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
        SetBrush(resources, "WindowBackgroundBrush", isDark ? Color.FromArgb(172, 10, 13, 20) : Color.FromArgb(196, 246, 248, 252));
        SetBrush(resources, "WindowSidebarBrush", isDark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(110, 255, 255, 255));
        SetBrush(resources, "WindowCardBrush", isDark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(175, 255, 255, 255));
        SetBrush(resources, "WindowCardHoverBrush", isDark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(215, 255, 255, 255));
        SetBrush(resources, "WindowBorderBrush", isDark ? Color.FromArgb(44, 255, 255, 255) : Color.FromArgb(90, 255, 255, 255));
        SetBrush(resources, "WindowDividerBrush", isDark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(32, 0, 0, 0));
        SetBrush(resources, "WindowInputBgBrush", isDark ? Color.FromArgb(160, 20, 24, 34) : Color.FromArgb(215, 255, 255, 255));
        SetBrush(resources, "WindowTextPrimaryBrush", isDark ? Color.FromArgb(255, 244, 244, 248) : Color.FromArgb(255, 20, 22, 28));
        SetBrush(resources, "WindowTextSecondaryBrush", isDark ? Color.FromArgb(255, 180, 184, 196) : Color.FromArgb(255, 72, 76, 88));
        SetBrush(resources, "WindowTextMutedBrush", isDark ? Color.FromArgb(255, 128, 132, 145) : Color.FromArgb(255, 108, 112, 124));
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

