using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RaysLoader.Services;

public enum InstallerSignatureStatus
{
    Valid,
    NotSigned,
    Invalid,
}

public sealed record InstallerSignatureResult(InstallerSignatureStatus Status, string SignerThumbprint = "", string Message = "");

public interface IInstallerSignatureVerifier
{
    InstallerSignatureResult Verify(string installerPath);
}

public interface IInstallerLauncher
{
    void Launch(string installerPath);
}

public sealed class ShellInstallerLauncher : IInstallerLauncher
{
    public void Launch(string installerPath)
    {
        _ = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true })
            ?? throw new Win32Exception("Windows did not start the installer.");
    }
}

public sealed class AuthenticodeInstallerSignatureVerifier : IInstallerSignatureVerifier
{
    private const uint UiNone = 2;
    private const uint NoRevocation = 0;
    private const uint FileChoice = 1;
    private const uint StateActionIgnore = 0;
    private const uint CacheOnlyUrlRetrieval = 0x00001000;
    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustEProviderUnknown = unchecked((int)0x800B0001);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public InstallerSignatureResult Verify(string installerPath)
    {
        if (!OperatingSystem.IsWindows()) return new(InstallerSignatureStatus.Invalid, Message: "Authenticode verification requires Windows.");

        var fileInfo = new WinTrustFileInfo(installerPath);
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePointer, false);
            var trustData = new WinTrustData(filePointer);
            var result = WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, ref trustData);
            if (result is TrustENoSignature or TrustEProviderUnknown or TrustESubjectFormUnknown)
                return new(InstallerSignatureStatus.NotSigned, Message: "The installer is not Authenticode signed.");
            if (result != 0)
                return new(InstallerSignatureStatus.Invalid, Message: $"Authenticode verification failed with 0x{result:X8}.");

            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(installerPath));
            return new(InstallerSignatureStatus.Valid, certificate.Thumbprint ?? string.Empty);
        }
        catch (CryptographicException exception)
        {
            return new(InstallerSignatureStatus.Invalid, Message: exception.Message);
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo
    {
        private readonly uint structureSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        private readonly string filePath;
        private readonly IntPtr fileHandle = IntPtr.Zero;
        private readonly IntPtr knownSubject = IntPtr.Zero;

        public WinTrustFileInfo(string filePath) => this.filePath = filePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        private uint structureSize;
        private IntPtr policyCallbackData;
        private IntPtr sipClientData;
        private uint uiChoice;
        private uint revocationChecks;
        private uint unionChoice;
        private IntPtr fileInfo;
        private uint stateAction;
        private IntPtr stateData;
        private string? urlReference;
        private uint providerFlags;
        private uint uiContext;

        public WinTrustData(IntPtr fileInfo)
        {
            structureSize = (uint)Marshal.SizeOf<WinTrustData>();
            policyCallbackData = IntPtr.Zero;
            sipClientData = IntPtr.Zero;
            uiChoice = UiNone;
            revocationChecks = NoRevocation;
            unionChoice = FileChoice;
            this.fileInfo = fileInfo;
            stateAction = StateActionIgnore;
            stateData = IntPtr.Zero;
            urlReference = null;
            providerFlags = CacheOnlyUrlRetrieval;
            uiContext = 0;
        }
    }
}

public sealed record UpdateSecurityOptions(string TrustedPublisherThumbprint)
{
    public static UpdateSecurityOptions FromAssembly(Assembly assembly)
    {
        // Signed release builds set this assembly metadata from the build's
        // AuthenticodePublisherThumbprint property. Empty means no certificate
        // identity has been defined; invalid signatures are still rejected.
        var thumbprint = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key.Equals("AuthenticodePublisherThumbprint", StringComparison.Ordinal))?.Value;
        return new UpdateSecurityOptions(NormalizeThumbprint(thumbprint));
    }

    public static string NormalizeThumbprint(string? value) =>
        string.Concat((value ?? string.Empty).Where(Uri.IsHexDigit)).ToUpperInvariant();
}
