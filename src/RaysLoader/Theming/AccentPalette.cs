using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RaysLoader.Settings;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace RaysLoader.Theming;

public static class AccentPalette
{
    public static string Label(AppAccent accent) => accent switch
    {
        AppAccent.System => "Windows accent",
        AppAccent.Ocean => "Ocean",
        AppAccent.Mint => "Mint",
        AppAccent.Coral => "Coral",
        AppAccent.Gold => "Gold",
        AppAccent.Rose => "Rose",
        AppAccent.Violet => "Violet",
        _ => "Windows accent",
    };

    public static Color Color(AppAccent accent) => accent switch
    {
        AppAccent.Ocean => FromHex(0x4C, 0xC2, 0xFF),
        AppAccent.Mint => FromHex(0x45, 0xD4, 0x83),
        AppAccent.Coral => FromHex(0xFF, 0x7A, 0x66),
        AppAccent.Gold => FromHex(0xF5, 0xC5, 0x42),
        AppAccent.Rose => FromHex(0xFF, 0x70, 0xAE),
        AppAccent.Violet => FromHex(0xB4, 0x98, 0xFF),
        _ => new UISettings().GetColorValue(UIColorType.AccentLight2),
    };

    public static void Apply(AppAccent accent, AppTheme theme)
    {
        var color = Color(accent);
        var foreground = RelativeLuminance(color) > 0.48 ? Colors.Black : Colors.White;
        var isLight = theme is AppTheme.Light ||
            (theme is AppTheme.System && RelativeLuminance(new UISettings().GetColorValue(UIColorType.Background)) > 0.5);
        var background = isLight
            ? Blend(FromHex(0xF7, 0xF8, 0xFA), color, 0.07)
            : Blend(FromHex(0x08, 0x09, 0x0B), color, 0.08);
        var surface = isLight
            ? Blend(FromHex(0xF0, 0xF2, 0xF5), color, 0.09)
            : Blend(FromHex(0x10, 0x12, 0x16), color, 0.10);
        var card = isLight
            ? Blend(Colors.White, color, 0.06)
            : Blend(FromHex(0x17, 0x19, 0x1D), color, 0.12);
        var settingsHeader = isLight
            ? Blend(FromHex(0xEC, 0xEE, 0xF2), color, 0.06)
            : Blend(FromHex(0x1D, 0x20, 0x26), color, 0.08);
        var settingsLabel = isLight
            ? Blend(FromHex(0xE4, 0xE7, 0xEC), color, 0.07)
            : Blend(FromHex(0x22, 0x26, 0x2D), color, 0.09);
        var settingsBody = isLight
            ? Blend(FromHex(0xFA, 0xFA, 0xFB), color, 0.04)
            : Blend(FromHex(0x0D, 0x0F, 0x13), color, 0.06);
        var settingsDivider = isLight
            ? Blend(FromHex(0xD3, 0xD6, 0xDC), color, 0.04)
            : Blend(FromHex(0x08, 0x09, 0x0B), color, 0.04);

        SetBrush("AppBackgroundBrush", background, 1);
        SetBrush("AppSurfaceBrush", surface, 1);
        SetBrush("AppCardBrush", card, 1);
        SetBrush("SettingsPageHeaderBrush", settingsHeader, 1);
        SetBrush("SettingsSectionLabelBrush", settingsLabel, 1);
        SetBrush("SettingsSectionBodyBrush", settingsBody, 1);
        SetBrush("SettingsSectionDividerBrush", settingsDivider, 1);
        SetBrush("SolidBackgroundFillColorBaseBrush", background, 1);
        SetBrush("SolidBackgroundFillColorSecondaryBrush", surface, 1);
        SetBrush("LayerFillColorDefaultBrush", surface, 1);
        SetBrush("LayerFillColorAltBrush", background, 1);
        SetBrush("LayerOnMicaBaseAltFillColorDefaultBrush", surface, 1);
        SetBrush("CardBackgroundFillColorDefaultBrush", card, 1);
        SetBrush("CardBackgroundFillColorSecondaryBrush", surface, 1);
        SetBrush("AccentFillColorSelectedTextBackgroundBrush", color, 1);
        SetBrush("AccentFillColorDefaultBrush", color, 1);
        SetBrush("AccentFillColorSecondaryBrush", color, 0.9);
        SetBrush("AccentFillColorTertiaryBrush", color, 0.8);
        SetBrush("AccentTextFillColorPrimaryBrush", color, 1);
        SetBrush("AccentTextFillColorSecondaryBrush", color, 0.9);
        SetBrush("AccentTextFillColorTertiaryBrush", color, 0.8);
        SetBrush("SystemFillColorAttentionBrush", color, 1);
        SetBrush("AccentAAFillColorDefaultBrush", color, 1);
        SetBrush("AccentAAFillColorSecondaryBrush", color, 0.9);
        SetBrush("AccentAAFillColorTertiaryBrush", color, 0.8);
        SetBrush("TextOnAccentFillColorPrimaryBrush", foreground, 1);
        SetBrush("TextOnAccentFillColorSecondaryBrush", foreground, 0.8);
    }

    private static void SetBrush(string key, Color color, double opacity)
    {
        if (Application.Current.Resources[key] is not SolidColorBrush brush) return;
        brush.Color = color;
        brush.Opacity = opacity;
    }

    private static Color FromHex(byte red, byte green, byte blue) => ColorHelper.FromArgb(255, red, green, blue);

    private static Color Blend(Color baseColor, Color tint, double tintAmount)
    {
        static byte Mix(byte left, byte right, double amount) =>
            (byte)Math.Clamp(Math.Round(left + ((right - left) * amount)), byte.MinValue, byte.MaxValue);

        return ColorHelper.FromArgb(
            255,
            Mix(baseColor.R, tint.R, tintAmount),
            Mix(baseColor.G, tint.G, tintAmount),
            Mix(baseColor.B, tint.B, tintAmount));
    }

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }
}
