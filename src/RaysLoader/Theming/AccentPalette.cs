using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RaysLoader.Settings;
using Windows.UI;

namespace RaysLoader.Theming;

/// <summary>
/// The four accent colours and their per-theme variants. On the light theme the
/// thin lines and text use darker shades so they stay readable on off-white.
/// </summary>
public static class AccentPalette
{
    private readonly record struct Variant(Color Fill, Color Ink, Color Line, Color Text, Color Soft);

    public static string Label(AppAccent accent) => accent switch
    {
        AppAccent.Red => "Red",
        AppAccent.Yellow => "Yellow",
        AppAccent.Navy => "Navy",
        _ => "Neon green",
    };

    /// <summary>The swatch colour shown in settings.</summary>
    public static Color Color(AppAccent accent) => Dark(accent).Fill;

    public static void Apply(AppAccent accent)
    {
        var resources = Application.Current.Resources;
        var dark = Dark(accent);
        var light = Light(accent);

        // The app's own theme dictionaries (Rl* tokens and Fluent overrides).
        if (Theme(resources, "Default") is { } appDark) Write(appDark, dark);
        if (Theme(resources, "Light") is { } appLight) Write(appLight, light);

        // Stock controls alias the framework's own accent brushes (for example
        // CheckBoxCheckBackgroundFillChecked is a StaticResource of
        // AccentFillColorDefaultBrush), which app-level keys do not override.
        // Recolour those accent-only brushes in place.
        foreach (var merged in resources.MergedDictionaries)
        {
            if (Theme(merged, "Default") is { } frameworkDefault) WriteFramework(frameworkDefault, dark);
            if (Theme(merged, "Dark") is { } frameworkDark) WriteFramework(frameworkDark, dark);
            if (Theme(merged, "Light") is { } frameworkLight) WriteFramework(frameworkLight, light);
        }
    }

    private static ResourceDictionary? Theme(ResourceDictionary resources, string key) =>
        resources.ThemeDictionaries.TryGetValue(key, out var value) ? value as ResourceDictionary : null;

    private static Variant Dark(AppAccent accent) => accent switch
    {
        AppAccent.Red => new(Hex(0xFF2B2B), Hex(0x0A0A0A), Hex(0xFF2B2B), Hex(0xFF5A5A), Hex(0x1F0B0B)),
        AppAccent.Yellow => new(Hex(0xFFD60A), Hex(0x0A0A0A), Hex(0xFFD60A), Hex(0xFFD60A), Hex(0x1C1904)),
        AppAccent.Navy => new(Hex(0x1C2F86), Hex(0xFFFFFF), Hex(0x4A6CF0), Hex(0x8FA6FF), Hex(0x0C1128)),
        _ => new(Hex(0xB8FF1F), Hex(0x0A0A0A), Hex(0xB8FF1F), Hex(0xB8FF1F), Hex(0x141C05)),
    };

    private static Variant Light(AppAccent accent) => accent switch
    {
        AppAccent.Red => new(Hex(0xE01414), Hex(0xFFFFFF), Hex(0xE01414), Hex(0xB80F0F), Hex(0xFBE3E3)),
        AppAccent.Yellow => new(Hex(0xFFD60A), Hex(0x0A0A0A), Hex(0x9A7B00), Hex(0x7A6100), Hex(0xFFF4C2)),
        AppAccent.Navy => new(Hex(0x1C2F86), Hex(0xFFFFFF), Hex(0x1C2F86), Hex(0x1C2F86), Hex(0xE1E6F7)),
        _ => new(Hex(0xB8FF1F), Hex(0x0A0A0A), Hex(0x4C8A00), Hex(0x3D6E00), Hex(0xE9F9C9)),
    };

    private static void Write(ResourceDictionary resources, Variant variant)
    {
        Set(resources, "RlAccentFillBrush", variant.Fill);
        Set(resources, "RlAccentInkBrush", variant.Ink);
        Set(resources, "RlAccentLineBrush", variant.Line);
        Set(resources, "RlAccentTextBrush", variant.Text);
        Set(resources, "RlAccentSoftBrush", variant.Soft);
        WriteFramework(resources, variant);
        Set(resources, "AccentControlElevationBorderBrush", variant.Line);
        Set(resources, "SystemFillColorAttentionBrush", variant.Line);
    }

    /// <summary>Brushes that only ever carry the accent colour.</summary>
    private static void WriteFramework(ResourceDictionary resources, Variant variant)
    {
        Set(resources, "AccentFillColorDefaultBrush", variant.Fill);
        Set(resources, "AccentFillColorSecondaryBrush", variant.Fill, 0.88);
        Set(resources, "AccentFillColorTertiaryBrush", variant.Fill, 0.72);
        Set(resources, "AccentFillColorSelectedTextBackgroundBrush", variant.Fill);
        Set(resources, "AccentAAFillColorDefaultBrush", variant.Fill);
        Set(resources, "AccentAAFillColorSecondaryBrush", variant.Fill, 0.88);
        Set(resources, "AccentAAFillColorTertiaryBrush", variant.Fill, 0.72);
        Set(resources, "AccentTextFillColorPrimaryBrush", variant.Text);
        Set(resources, "AccentTextFillColorSecondaryBrush", variant.Text);
        Set(resources, "AccentTextFillColorTertiaryBrush", variant.Text);
        Set(resources, "TextOnAccentFillColorPrimaryBrush", variant.Ink);
        Set(resources, "TextOnAccentFillColorSecondaryBrush", variant.Ink, 0.8);
        Set(resources, "TextOnAccentFillColorSelectedTextBrush", variant.Ink);
    }

    /// <summary>Recolours an existing brush in place so ThemeResource references repaint.</summary>
    private static void Set(ResourceDictionary resources, string key, Color color, double opacity = 1)
    {
        if (resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            brush.Color = color;
            brush.Opacity = opacity;
        }
    }

    private static Color Hex(uint rgb) => ColorHelper.FromArgb(
        255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
}
