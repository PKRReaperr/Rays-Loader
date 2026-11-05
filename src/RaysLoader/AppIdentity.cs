namespace RaysLoader;

internal static class AppIdentity
{
#if RAYSLOADER_DEV
    public static bool IsDevelopment => true;
    public const string DataDirectoryName = "RaysLoaderDev";
    public const string DisplayName = "Ray's Loader DEV";
    public const string LogoAssetPath = "Assets/RaysLoader.Dev.png";
    public const string IconFileName = "RaysLoader.Dev.ico";
#else
    public static bool IsDevelopment => false;
    public const string DataDirectoryName = "RaysLoader";
    public const string DisplayName = "Ray's Loader";
    public const string LogoAssetPath = "Assets/RaysLoader.png";
    public const string IconFileName = "RaysLoader.ico";
#endif

    public static string LogoUri => $"ms-appx:///{LogoAssetPath}";
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataDirectoryName);
}
