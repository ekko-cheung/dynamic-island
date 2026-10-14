using System.Runtime.InteropServices;

namespace DynamicIsland.Core;

public static class PackageIdentity
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    /// <summary>True when running with MSIX package identity (required by UserNotificationListener and StartupTask).</summary>
    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        var length = 0;
        return GetCurrentPackageFullName(ref length, null) != APPMODEL_ERROR_NO_PACKAGE;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
