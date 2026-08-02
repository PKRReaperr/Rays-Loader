namespace RaysLoader;

internal static class AppIdentity
{
#if RAYSLOADER_DEV
    public const string DisplayName = "Ray's Loader DEV";
    public const string LogoAssetPath = "Assets/RaysLoader.Dev.png";
    public const string IconFileName = "RaysLoader.Dev.ico";
#else
    public const string DisplayName = "Ray's Loader";
    public const string LogoAssetPath = "Assets/RaysLoader.png";
    public const string IconFileName = "RaysLoader.ico";
#endif

    public static string LogoUri => $"ms-appx:///{LogoAssetPath}";
}
