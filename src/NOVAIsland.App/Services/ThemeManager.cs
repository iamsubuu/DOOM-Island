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

        var alpha = (byte)Math.Clamp((int)Math.Round(appearance.Opacity * 2.30), 198, 232);
        var surfaceColor = appearance.Theme switch
        {
            ThemePreset.PureBlack => Color.FromArgb(218, 6, 17, 11),
            ThemePreset.Obsidian => Color.FromArgb(alpha, 9, 22, 16),
            ThemePreset.Graphite => Color.FromArgb(alpha, 16, 29, 23),
            ThemePreset.Contrast => Color.FromArgb(245, 4, 12, 8),
            _ => Color.FromArgb(218, 6, 17, 11)
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
        SetBrush(resources, "WindowBackgroundBrush", isDark ? Color.FromArgb(168, 6, 14, 10) : Color.FromArgb(196, 244, 250, 246));
        SetBrush(resources, "WindowSidebarBrush", isDark ? Color.FromArgb(24, 20, 40, 30) : Color.FromArgb(115, 255, 255, 255));
        SetBrush(resources, "WindowCardBrush", isDark ? Color.FromArgb(26, 18, 36, 26) : Color.FromArgb(180, 255, 255, 255));
        SetBrush(resources, "WindowCardHoverBrush", isDark ? Color.FromArgb(44, 46, 229, 139) : Color.FromArgb(215, 236, 253, 245));
        SetBrush(resources, "WindowBorderBrush", isDark ? Color.FromArgb(52, 46, 229, 139) : Color.FromArgb(80, 16, 185, 129));
        SetBrush(resources, "WindowDividerBrush", isDark ? Color.FromArgb(34, 46, 229, 139) : Color.FromArgb(32, 0, 0, 0));
        SetBrush(resources, "WindowInputBgBrush", isDark ? Color.FromArgb(160, 13, 24, 18) : Color.FromArgb(220, 255, 255, 255));
        SetBrush(resources, "WindowTextPrimaryBrush", isDark ? Color.FromArgb(255, 242, 248, 245) : Color.FromArgb(255, 16, 24, 20));
        SetBrush(resources, "WindowTextSecondaryBrush", isDark ? Color.FromArgb(255, 174, 192, 182) : Color.FromArgb(255, 68, 86, 76));
        SetBrush(resources, "WindowTextMutedBrush", isDark ? Color.FromArgb(255, 118, 138, 128) : Color.FromArgb(255, 102, 118, 110));
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
        {
            color = Color.FromArgb(255, r, g, b);
            return true;
        }

        return false;
    }
}

