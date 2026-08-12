using RaysLoader.Settings;

namespace RaysLoader.Models;

public sealed record AuthenticationMethodOption(string Label, AuthenticationMode Mode)
{
    public override string ToString() => Label;
}

public sealed record BrowserCookieOption(string Label, string Value)
{
    public override string ToString() => Label;
}

public static class AuthenticationOptions
{
    public static IReadOnlyList<AuthenticationMethodOption> Methods { get; } =
    [
        new("No authentication", AuthenticationMode.None),
        new("Signed-in browser (recommended)", AuthenticationMode.Browser),
        new("Cookies file", AuthenticationMode.CookiesFile),
    ];

    public static IReadOnlyList<BrowserCookieOption> Browsers { get; } =
    [
        new("Firefox (recommended on Windows)", "firefox"),
        new("Microsoft Edge", "edge"),
        new("Google Chrome", "chrome"),
        new("Brave", "brave"),
        new("Vivaldi", "vivaldi"),
        new("Opera", "opera"),
        new("Chromium", "chromium"),
    ];

    public static AuthenticationMethodOption FindMethod(AuthenticationMode mode) =>
        Methods.First(option => option.Mode == mode);

    public static BrowserCookieOption FindBrowser(string? value) =>
        Browsers.FirstOrDefault(option => option.Value.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? Browsers[0];
}
